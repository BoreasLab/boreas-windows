using System.Collections.Immutable;
using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// The control surface over a real Boreas tunnel.
/// </summary>
/// <remarks>
/// <para>
/// Everything that touches the operating system is behind
/// <see cref="ITunnelHost"/> and everything that touches the UI thread is
/// behind the posting delegate, so what is left here is a state machine: which
/// transitions are legal, what the bounded event window holds, and what the
/// user is told. That is why this compiles against plain net10.0 and its laws
/// run without Windows.
/// </para>
/// <para>
/// <b>Resolutions are deliberately not recorded.</b> The event window is a
/// bounded control-plane record - transitions, commands, channel changes,
/// failures - and one event per DNS question would fill two hundred slots in
/// seconds and evict every transition that explains what the tunnel is doing. A
/// "what did it block" screen is a different surface with its own store, and
/// this is not it.
/// </para>
/// </remarks>
public sealed class NativeControlChannel : IControlChannel
{
    /// <summary>
    /// Runs an action on the UI thread.
    /// </summary>
    /// <remarks>
    /// Injected rather than taken from <c>DispatcherQueue</c> so this type owes
    /// nothing to Microsoft.UI. The obligation it carries is the interface's:
    /// <c>Changed</c> is raised on the UI thread, and events arrive from the
    /// tunnel's reader thread, so something has to move them.
    /// </remarks>
    public delegate void Post(Action action);

    private readonly ITunnelHost _host;
    private readonly Post _post;
    private readonly Lock _gate = new();

    private ImmutableArray<ControlEvent> _events = [];
    private IRunningTunnel? _tunnel;
    private ValidatedConfiguration _configuration;

    public NativeControlChannel(
        ITunnelHost host,
        ValidatedConfiguration configuration,
        Post post,
        ControlChannelState channel)
    {
        _host = host;
        _configuration = configuration;
        _post = post;
        Channel = channel;
        State = new ServiceState.Stopped();

        Record(ControlEventKind.Channel, channel is ControlChannelState.Connected
            ? "Control channel connected."
            : "Control channel unavailable.");
    }

    /// <summary>
    /// Fixed at construction, because the ABI check that decides it runs once,
    /// before anything else.
    /// </summary>
    /// <remarks>
    /// A mismatch is reported as a version disagreement rather than as a failed
    /// start: a stale library beside a newer header reads every field at the
    /// wrong offset, so there is nothing to retry and nothing to fix in the
    /// configuration. The build to install is the one whose boreas.dll shipped
    /// with it.
    /// </remarks>
    public ControlChannelState Channel { get; }

    public ServiceState State { get; private set; }

    public ImmutableArray<ControlEvent> Events => _events;

    public event EventHandler? Changed;

    /// <summary>
    /// Idempotent, and there is nothing to ask: this process holds the tunnel,
    /// so the authoritative state is the one already in hand. Refreshing
    /// re-reads the counters and republishes.
    /// </summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Republish();
        return Task.CompletedTask;
    }

    public async Task<ServiceState> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!Channel.CanSendCommands || State is ServiceState.Running or ServiceState.Starting)
        {
            return State;
        }

        Transition(new ServiceState.Starting(), "Start requested.");

        var configuration = _configuration;

        try
        {
            // Off the UI thread: start blocks for as long as the first
            // connection takes, which is a DNS lookup and a handshake.
            var tunnel = await Task.Run(() => _host.Start(configuration, OnTunnelEvent), cancellationToken);

            lock (_gate)
            {
                _tunnel = tunnel;
            }

            Transition(
                new ServiceState.Running(
                    new SessionIdentity(Guid.CreateVersion7().ToString()),
                    Snapshot(configuration, tunnel, DateTimeOffset.Now)),
                "Session running.");
        }
        catch (OperationCanceledException)
        {
            Transition(new ServiceState.Stopped(), "Start cancelled.");
        }
        catch (Exception failure)
        {
            Fail(ControlOperation.Start, failure);
        }

        return State;
    }

    public async Task<ServiceState> StopAsync(CancellationToken cancellationToken = default)
    {
        IRunningTunnel? tunnel;

        lock (_gate)
        {
            tunnel = _tunnel;
            _tunnel = null;
        }

        if (tunnel is null)
        {
            return State;
        }

        var session = State is ServiceState.Running running
            ? running.Session
            : new SessionIdentity("unknown");

        Transition(new ServiceState.Stopping(session), "Stop requested.");

        try
        {
            // Off the UI thread: shutdown takes as long as an ordered shutdown
            // takes, and the join after it waits for a reader that may be
            // inside a call.
            var status = await Task.Run(tunnel.Stop, CancellationToken.None);

            tunnel.Dispose();

            Transition(
                new ServiceState.Stopped(),
                status is BoreasStatus.Ok or BoreasStatus.Stopped
                    ? "Session stopped."
                    : $"Session stopped, and shutdown reported {status}.");
        }
        catch (Exception failure)
        {
            Fail(ControlOperation.Stop, failure);
        }

        return State;
    }

    /// <summary>
    /// Accepts a configuration that has already passed the parse boundary.
    /// </summary>
    /// <remarks>
    /// <b>A running session never takes one.</b> Reload replaces the rules in
    /// force and nothing else: the egress, the certificate authority, the
    /// resolver, the ceilings, the intercepted host list and the MTU are all
    /// fixed at start. Every field this form edits is on that list, so the
    /// honest answer is that it takes effect on the next start - which is what
    /// <see cref="ConfigurationOutcome.RestartRequired"/> says. Claiming it was
    /// applied would be the partial silent application the contract forbids.
    /// </remarks>
    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _configuration = configuration;

        var outcome = State is ServiceState.Running
            ? new ConfigurationOutcome.RestartRequired()
            : (ConfigurationOutcome)new ConfigurationOutcome.Applied();

        Record(ControlEventKind.Command, outcome is ConfigurationOutcome.RestartRequired
            ? "Configuration stored. It takes effect on the next start."
            : "Configuration stored.");

        Republish();

        return Task.FromResult(outcome);
    }

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_configuration.ToDraft());

    private static SessionStatus Snapshot(
        ValidatedConfiguration configuration, IRunningTunnel tunnel, DateTimeOffset since) => new(
        AdapterName: configuration.Adapter.Value,
        InterfaceAddress: configuration.Address.ToString(),
        Mtu: configuration.PacketSize.Value,
        RunningSince: since,
        Counters: tunnel.Counters,
        Bypass: tunnel.Bypass);

    /// <summary>
    /// Called from the tunnel's reader thread, never the UI's.
    /// </summary>
    /// <remarks>
    /// This is the boundary the interface's "raise Changed on the UI thread"
    /// obligation is discharged at. It must also not throw: the reader catches,
    /// but a channel that throws here would be relying on that.
    /// </remarks>
    private void OnTunnelEvent(TunnelEvent tunnelEvent) => _post(() =>
    {
        tunnelEvent.Match<object?>(
            // Not recorded. See the note on this class: one per DNS question
            // would evict every transition inside a minute.
            resolved: static _ => null,

            reloaded: r =>
            {
                Record(
                    ControlEventKind.Command,
                    $"Rules reloaded: {r.Allowed} allowed, {r.BlockedRules} blocked, {r.Inspected} inspected.");
                return null;
            },

            counted: c =>
            {
                // Every counter is a thing that went wrong or was refused, so a
                // quiet interval is not news and a loud one is, without this
                // having to know what any single field means.
                if (!c.Counters.IsQuiet)
                {
                    Record(ControlEventKind.Failure, Describe(c.Counters), Diagnose(c.Counters));
                }

                return null;
            });

        Republish();
    });

    private static string Describe(TunnelCounters counters) =>
        "The tunnel reported " + string.Join(", ", Nonzero(counters)) + ".";

    private static IEnumerable<string> Nonzero(TunnelCounters counters)
    {
        if (counters.DatagramsDropped > 0) { yield return $"{counters.DatagramsDropped} datagrams dropped"; }
        if (counters.PacketsRejected > 0) { yield return $"{counters.PacketsRejected} packets rejected"; }
        if (counters.QuicSteered > 0) { yield return $"{counters.QuicSteered} QUIC flows steered"; }
        if (counters.PathsReported > 0) { yield return $"{counters.PathsReported} paths reported"; }
        if (counters.EventsLost > 0) { yield return $"{counters.EventsLost} events lost"; }
        if (counters.TasksPanicked > 0) { yield return $"{counters.TasksPanicked} tasks panicked"; }
    }

    /// <summary>
    /// The one sentence a person can act on, for the counters that mean
    /// something specific.
    /// </summary>
    /// <remarks>
    /// Ordered by what the reader should do first. A panicked task is a defect
    /// in Boreas and is worth reporting whatever else is happening; a reported
    /// path is a misconfiguration that will not stop on its own; dropped
    /// datagrams are a ceiling. The rest are conditions of the network, which
    /// the summary already names.
    /// </remarks>
    private static TypedError Diagnose(TunnelCounters counters) => counters switch
    {
        { TasksPanicked: > 0 } => new TypedError(
            "core.task_panicked",
            "A part of the tunnel stopped unexpectedly.",
            "Traffic keeps flowing. Please report this with what the device was doing.",
            $"tasks_panicked={counters.TasksPanicked}"),

        { PathsReported: > 0 } => new TypedError(
            "core.path_mtu",
            "The adapter is carrying packets wider than the tunnel was told about.",
            "Stop and start the tunnel. If it continues, the adapter's MTU does not match the "
            + "configured one.",
            $"paths_reported={counters.PathsReported}"),

        { DatagramsDropped: > 0 } => new TypedError(
            "core.ceiling",
            "The tunnel dropped datagrams because it had nowhere to put them.",
            "This device's traffic needs more headroom than the tunnel was given.",
            $"datagrams_dropped={counters.DatagramsDropped}"),

        { EventsLost: > 0 } => new TypedError(
            "core.events_lost",
            "Some tunnel events were not read in time and were counted instead.",
            "No traffic was affected. The gap in this list is recorded rather than silent.",
            $"events_lost={counters.EventsLost}"),

        _ => new TypedError(
            "core.counted",
            "The tunnel reported activity worth noticing.",
            "No action is needed unless this continues.",
            Describe(counters)),
    };

    private void Fail(ControlOperation operation, Exception failure)
    {
        var error = failure switch
        {
            BoreasAbiMismatchException mismatch => new TypedError(
                "core.abi_mismatch",
                "This build and the installed Boreas library do not match.",
                "Reinstall Boreas so the application and its library come from one build.",
                mismatch.Message),

            BoreasException boreas => new TypedError(
                $"core.{boreas.Status.ToString().ToLowerInvariant()}",
                Summarise(boreas.Status),
                NextStep(boreas.Status),
                boreas.Message),

            DllNotFoundException => new TypedError(
                "core.missing",
                "The Boreas library is not installed beside this application.",
                "Reinstall Boreas.",
                failure.Message),

            _ => new TypedError(
                "host.failed",
                "The tunnel could not be started.",
                "Check that Boreas is installed and that this application is running with the "
                + "privileges it needs.",
                failure.Message),
        };

        // Recoverable is the service's judgement, and a defect in the core is
        // the one thing retrying cannot help with.
        var recoverable = failure is not BoreasAbiMismatchException
            && failure is not BoreasException { IsDefect: true };

        State = new ServiceState.Failed(operation, error, recoverable);
        Record(ControlEventKind.Failure, error.Summary, error);
        Republish();
    }

    private static string Summarise(BoreasStatus status) => status switch
    {
        BoreasStatus.Config => "The tunnel's settings describe something that cannot run.",
        BoreasStatus.Authority => "The stored certificate authority could not be restored.",
        BoreasStatus.Egress => "The tunnel could not reach the network it was told to leave by.",
        BoreasStatus.Termination => "The tunnel was not given room for every port it inspects.",
        BoreasStatus.Io => "A connection the tunnel needs could not be opened.",
        BoreasStatus.Panic => "The tunnel stopped because of a defect in Boreas.",
        _ => "The tunnel could not be started.",
    };

    private static string NextStep(BoreasStatus status) => status switch
    {
        BoreasStatus.Config => "Check the network settings on this page.",
        BoreasStatus.Authority => "Boreas will generate a new one. You will be asked to trust it again.",
        BoreasStatus.Egress => "Check the egress settings on this page.",
        BoreasStatus.Termination => "Raise the connection ceiling, or intercept fewer ports.",
        BoreasStatus.Io => "Check that this device has a working network connection.",
        BoreasStatus.Panic => "Please report this. Starting again is not expected to help.",
        _ => "Try again. If it continues, please report it.",
    };

    private void Transition(ServiceState next, string summary)
    {
        State = next;
        Record(ControlEventKind.Transition, summary);
        Republish();
    }

    /// <summary>
    /// Records one event, newest first, within the shared bound.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than mutated so a render can never observe the buffer
    /// while its producer is writing it. Each record is O(window) in copying,
    /// against a window of two hundred and a producer that is a control-plane
    /// transition rather than a packet.
    /// </remarks>
    private void Record(ControlEventKind kind, string summary, TypedError? error = null)
    {
        var kept = _events.Length < ControlProtocol.EventWindow
            ? _events
            : _events[..(ControlProtocol.EventWindow - 1)];

        _events = [new ControlEvent(DateTimeOffset.Now, kind, summary, error), .. kept];
    }

    /// <summary>
    /// Re-reads the counters into the running status and announces the change.
    /// </summary>
    /// <remarks>
    /// The counters live on the tunnel rather than in a field here, so there is
    /// one copy of them and this is a read rather than a second place they
    /// could drift.
    /// </remarks>
    private void Republish()
    {
        if (State is ServiceState.Running running)
        {
            IRunningTunnel? tunnel;

            lock (_gate)
            {
                tunnel = _tunnel;
            }

            if (tunnel is not null)
            {
                State = running with { Status = running.Status with { Counters = tunnel.Counters, Bypass = tunnel.Bypass } };
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops the tunnel if one is running, then releases it.
    /// </summary>
    /// <remarks>
    /// Disposing without stopping would free a handle a reader is blocked
    /// inside. <c>NativeTunnel.Dispose</c> is <c>Stop</c> for that reason, and
    /// this calls it rather than reaching past it.
    /// </remarks>
    public void Dispose()
    {
        IRunningTunnel? tunnel;

        lock (_gate)
        {
            tunnel = _tunnel;
            _tunnel = null;
        }

        tunnel?.Dispose();
    }
}

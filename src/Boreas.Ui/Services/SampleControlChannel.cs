#if DEBUG
using System.Collections.Immutable;
using Boreas.Ui.Contracts;
using Microsoft.UI.Dispatching;

namespace Boreas.Ui.Services;

/// <summary>
/// SAMPLE DATA. Not a service, not a pipe, not a measurement.
/// </summary>
/// <remarks>
/// It exercises every state before W2 provides a real channel. Values are
/// invented, so release builds omit it and the window marks its use.
/// </remarks>
public sealed class SampleControlChannel : IControlChannel
{
    private static readonly SessionIdentity Session = new("sample-0000-0000");
    private static readonly EgressBypass Bypass = new EgressBypass.Bound("Wi-Fi (sample)");

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Timer _tick;

    // Keep session data inside ServiceState.Running to avoid duplicate state.
    private ImmutableArray<ControlEvent> _events = [];

    public SampleControlChannel()
    {
        State = new ServiceState.Stopped();
        Record(ControlEventKind.Channel, "Control channel connected (sample).");
        // Marshal timer updates to the UI thread before changing bound state.
        _tick = new Timer(_ => _dispatcher.TryEnqueue(Advance), state: null, dueTime: 1000, period: 1000);
    }

    /// <summary>Fixed at construction; sample data never loses its channel.</summary>
    public ControlChannelState Channel { get; } = new ControlChannelState.Connected(ControlProtocol.Version);

    public ServiceState State { get; private set; }

    public ImmutableArray<ControlEvent> Events => _events;

    public event EventHandler? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Notify();
        return Task.CompletedTask;
    }

    public async Task<ServiceState> StartAsync(CancellationToken cancellationToken = default)
    {
        Transition(new ServiceState.Starting(), "Start requested (sample).");
        await Task.Delay(1400, cancellationToken);

        Transition(
            new ServiceState.Running(Session, Snapshot(DateTimeOffset.Now, default)),
            "Session running (sample).");
        return State;
    }

    public async Task<ServiceState> StopAsync(CancellationToken cancellationToken = default)
    {
        Transition(new ServiceState.Stopping(Session), "Stop requested (sample).");
        await Task.Delay(900, cancellationToken);
        Transition(new ServiceState.Stopped(), "Session stopped (sample).");
        return State;
    }

    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        Record(ControlEventKind.Command, "Configuration applied (sample).");
        Notify();
        return Task.FromResult<ConfigurationOutcome>(
            State is ServiceState.Running
                ? new ConfigurationOutcome.RestartRequired()
                : new ConfigurationOutcome.Applied());
    }

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationDraft(
            AdapterName: "Boreas",
            InterfaceAddress: "10.7.0.2/24",
            Mtu: "1420",
            DnsServers: "10.7.0.1",
            Routes: RouteMode.Default,
            Egress: EgressPolicy.Direct));

    private static SessionStatus Snapshot(DateTimeOffset since, SessionCounters counters) => new(
        AdapterName: "Boreas (sample)",
        InterfaceAddress: "10.7.0.2/24",
        Mtu: 1420,
        RunningSince: since,
        Counters: counters,
        Bypass: Bypass);

    /// <summary>
    /// Advances invented traffic for the running session.
    /// </summary>
    private void Advance()
    {
        if (State is not ServiceState.Running running)
        {
            return;
        }

        var counters = running.Status.Counters;

        State = running with
        {
            Status = running.Status with
            {
                Counters = new SessionCounters(
                    counters.PacketsIn + 37,
                    counters.PacketsOut + 41,
                    counters.BytesIn + 24_800,
                    counters.BytesOut + 9_100),
            },
        };

        Notify();
    }

    private void Transition(ServiceState next, string summary)
    {
        State = next;
        Record(ControlEventKind.Transition, summary);
        Notify();
    }

    private void Record(ControlEventKind kind, string summary, TypedError? error = null)
    {
        // Rebuild newest-first snapshots within the shared event bound.
        var kept = _events.Length < ControlProtocol.EventWindow
            ? _events
            : _events[..(ControlProtocol.EventWindow - 1)];

        _events = [new ControlEvent(DateTimeOffset.Now, kind, summary, error), .. kept];
    }

    /// <summary>
    /// Raises changes on the UI thread, including timer-originated updates.
    /// </summary>
    private void Notify()
    {
        if (_dispatcher.HasThreadAccess)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.TryEnqueue(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Dispose() => _tick.Dispose();
}
#endif

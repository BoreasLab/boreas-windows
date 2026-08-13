#if DEBUG
using System.Collections.Immutable;
using Boreas.Ui.Contracts;
using Microsoft.UI.Dispatching;

namespace Boreas.Ui.Services;

/// <summary>
/// SAMPLE DATA. Not a service, not a pipe, not a measurement.
/// </summary>
/// <remarks>
/// This exists so the interface can be walked through every state before W2
/// makes a real channel available. Every value it produces is invented, which
/// is why it is compiled out of release builds and why the window shows a
/// "Sample data" marker whenever it is in use.
///
/// The counters increase while a sample session runs so that transitions,
/// tabular figures and live-region announcements can be seen behaving. They
/// measure nothing.
/// </remarks>
public sealed class SampleControlChannel : IControlChannel
{
    private static readonly SessionIdentity Session = new("sample-0000-0000");
    private static readonly EgressBypass Bypass = new EgressBypass.Bound("Wi-Fi (sample)");

    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Timer _tick;

    // Two mutable cells, and the running session is not one of them. It used to
    // be four: State alongside a separate counters struct and start time that
    // Snapshot read back. Those were a second copy of what State already
    // carried, and a copy that can disagree with the thing it copies is a
    // disagreement waiting to be rendered. The session now lives in
    // ServiceState.Running and is advanced by rebuilding it.
    private ImmutableArray<ControlEvent> _events = [];

    public SampleControlChannel()
    {
        State = new ServiceState.Stopped();
        Record(ControlEventKind.Channel, "Control channel connected (sample).");
        // Advance runs on the UI thread, not the timer's pool thread. It writes
        // _counters, a 32-byte struct the UI reads through Snapshot, and a
        // struct that wide is not written atomically: a render landing mid-write
        // could show half of one reading and half of the next. Marshalling
        // first removes the race rather than locking around it.
        _tick = new Timer(_ => _dispatcher.TryEnqueue(Advance), state: null, dueTime: 1000, period: 1000);
    }

    /// <summary>Fixed at construction: the sample never loses its channel.</summary>
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
    /// One tick of invented traffic, as a function of the session that is
    /// running rather than of fields kept beside it.
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
        // Newest first, bounded to the window the interface promises, from the
        // one constant that promises it. Rebuilt rather than mutated: the value
        // already handed to a reader stays exactly as it was handed over.
        // O(window) per event, and events arrive at the speed a person presses
        // buttons.
        var kept = _events.Length < ControlProtocol.EventWindow
            ? _events
            : _events[..(ControlProtocol.EventWindow - 1)];

        _events = [new ControlEvent(DateTimeOffset.Now, kind, summary, error), .. kept];
    }

    /// <summary>
    /// Raised on the UI thread. The timer runs on a pool thread, and the real
    /// pipe client will have the same obligation when a pushed status arrives
    /// on its own reader.
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

#if DEBUG
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
public sealed class SampleControlChannel : IControlChannel, IDisposable
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly List<ControlEvent> _events = [];
    private readonly Timer _tick;
    private readonly SessionIdentity _session = new("sample-0000-0000");
    private SessionCounters _counters;
    private DateTimeOffset _since;
    private readonly EgressBypass _bypass = new EgressBypass.Bound("Wi-Fi (sample)");

    public SampleControlChannel()
    {
        Channel = new ControlChannelState.Connected(ProtocolVersion: 1);
        State = new ServiceState.Stopped();
        Record(ControlEventKind.Channel, "Control channel connected (sample).");
        // Advance runs on the UI thread, not the timer's pool thread. It writes
        // _counters, a 32-byte struct the UI reads through Snapshot, and a
        // struct that wide is not written atomically: a render landing mid-write
        // could show half of one reading and half of the next. Marshalling
        // first removes the race rather than locking around it.
        _tick = new Timer(_ => _dispatcher.TryEnqueue(Advance), state: null, dueTime: 1000, period: 1000);
    }

    public ControlChannelState Channel { get; private set; }

    public ServiceState State { get; private set; }

    public IReadOnlyList<ControlEvent> Events => _events;

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

        _since = DateTimeOffset.Now;
        _counters = default;
        Transition(
            new ServiceState.Running(_session, Snapshot()),
            "Session running (sample).");
        return State;
    }

    public async Task<ServiceState> StopAsync(CancellationToken cancellationToken = default)
    {
        Transition(new ServiceState.Stopping(_session), "Stop requested (sample).");
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

    private SessionStatus Snapshot() => new(
        AdapterName: "Boreas (sample)",
        InterfaceAddress: "10.7.0.2/24",
        Mtu: 1420,
        RunningSince: _since,
        Counters: _counters,
        Bypass: _bypass);

    private void Advance()
    {
        if (State is not ServiceState.Running)
        {
            return;
        }

        _counters = new SessionCounters(
            _counters.PacketsIn + 37,
            _counters.PacketsOut + 41,
            _counters.BytesIn + 24_800,
            _counters.BytesOut + 9_100);

        State = new ServiceState.Running(_session, Snapshot());
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
        _events.Insert(0, new ControlEvent(DateTimeOffset.Now, kind, summary, error));

        // Bounded, like the real subscription has to be.
        if (_events.Count > 200)
        {
            _events.RemoveRange(200, _events.Count - 200);
        }
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

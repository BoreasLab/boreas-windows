using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The status screen. Renders what the service reported and sends intent.
/// </summary>
public sealed class StatusViewModel : ObservableObject, IDisposable
{
    private readonly IControlChannel _channel;

    public StatusViewModel(IControlChannel channel)
    {
        _channel = channel;

        Primary = new AsyncCommand(InvokePrimaryAsync, () => Presentation.Action != PrimaryAction.None);
        Refresh = new AsyncCommand(_channel.RefreshAsync);

        _channel.Changed += OnChannelChanged;
    }

    public AsyncCommand Primary { get; }

    public AsyncCommand Refresh { get; }

    /// <summary>
    /// Derived on read rather than stored and synchronised. Two fields that
    /// must always agree are one field and a function; keeping a cached copy
    /// here would render one stale frame on every transition.
    /// </summary>
    public StatusPresentation Presentation =>
        StatusPresentation.For(_channel.Channel, _channel.State);

    public ChannelPresentation Channel => ChannelPresentation.For(_channel.Channel);

    /// <summary>
    /// The label for the one prominent action, always a verb naming what will
    /// happen.
    /// </summary>
    /// <remarks>
    /// Stop drops every connection running through the tunnel, so it is
    /// destructive in one sense. It is still the accent button and it is
    /// still unconfirmed, because it is undone by pressing start and it is
    /// the expected action for as long as the tunnel is up. Spending the
    /// danger colour and a confirmation on the most frequent press is how
    /// people learn to dismiss the one confirmation that matters.
    /// </remarks>
    public string PrimaryLabel => Presentation.Action switch
    {
        PrimaryAction.None => string.Empty,
        PrimaryAction.Start => "Start Boreas",
        PrimaryAction.Stop => "Stop Boreas",
        PrimaryAction.Retry => "Try starting again",
        PrimaryAction.Reconnect => "Reconnect",
        _ => throw Unreachable.Value(Presentation.Action),
    };

    public bool HasPrimaryAction => Presentation.Action != PrimaryAction.None;

    /// <summary>
    /// Session facts. Never null: <see cref="SessionFacts.None"/> stands in
    /// while there is no session, so no binding has to survive a null path.
    /// </summary>
    public SessionFacts Facts => _channel.Channel is ControlChannelState.Connected
        ? _channel.State.Match(
            stopped: static _ => SessionFacts.None,
            starting: static _ => SessionFacts.None,
            running: static r => SessionFacts.From(r.Session, r.Status),
            stopping: static _ => SessionFacts.None,
            failed: static _ => SessionFacts.None)
        : SessionFacts.None;

    public bool HasFacts => Facts.SessionId.Length > 0;

    /// <summary>
    /// The bypass warning, or null when there is nothing to warn about. It is
    /// kept out of the band so the band answers exactly one question.
    /// </summary>
    public TypedError? BypassDegradation => _channel.State.Match(
        stopped: static _ => (TypedError?)null,
        starting: static _ => null,
        running: static r => r.Status.Bypass.Match(
            bound: static _ => (TypedError?)null,
            degraded: static d => d.Error),
        stopping: static _ => null,
        failed: static _ => null);

    private async Task InvokePrimaryAsync(CancellationToken cancellationToken)
    {
        // Re-read the action at press time. The state may have changed between
        // the last render and this press, and the service is the authority on
        // what is valid now.
        switch (Presentation.Action)
        {
            case PrimaryAction.Start:
            case PrimaryAction.Retry:
                await _channel.StartAsync(cancellationToken);
                break;
            case PrimaryAction.Stop:
                await _channel.StopAsync(cancellationToken);
                break;
            case PrimaryAction.Reconnect:
                await _channel.RefreshAsync(cancellationToken);
                break;
            case PrimaryAction.None:
                break;
        }
    }

    private void OnChannelChanged(object? sender, EventArgs e)
    {
        Raise(nameof(Presentation));
        Raise(nameof(Channel));
        Raise(nameof(PrimaryLabel));
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(Facts));
        Raise(nameof(HasFacts));
        Raise(nameof(BypassDegradation));
        Primary.RaiseCanExecuteChanged();
    }

    public void Dispose() => _channel.Changed -= OnChannelChanged;
}

/// <summary>A label and the value under it. Formatted once, on the way out.</summary>
public sealed record LabelledValue(string Label, string Value);

/// <summary>
/// What a live session shows, already formatted.
/// </summary>
/// <remarks>
/// Column choice is the design. Identity answers "is this the tunnel I think
/// it is", and the counters answer "is anything actually moving". Everything
/// else the snapshot carries stays in diagnostics.
///
/// Uptime is shown as the wall-clock time the session started rather than a
/// counting-up duration. An absolute time is what someone reads out to
/// support, and it does not need a timer running behind the window to stay
/// truthful between status pushes.
/// </remarks>
public sealed record SessionFacts(
    string SessionId,
    string RunningSince,
    IReadOnlyList<LabelledValue> Identity,
    IReadOnlyList<LabelledValue> Counters)
{
    /// <summary>The stand-in for "there is no session", so nothing binds to null.</summary>
    public static SessionFacts None { get; } = new(
        SessionId: string.Empty,
        RunningSince: string.Empty,
        Identity: [],
        Counters: []);

    public static SessionFacts From(SessionIdentity session, SessionStatus status) => new(
        SessionId: session.Value,
        RunningSince: $"Running since {status.RunningSince:HH:mm} on {status.RunningSince:d MMMM}",
        Identity:
        [
            new LabelledValue("Adapter", status.AdapterName),
            new LabelledValue("Address", status.InterfaceAddress),
            new LabelledValue("Packet size", $"{status.Mtu:N0} bytes"),
            new LabelledValue("Upstream", status.Bypass.Match(
                bound: static b => b.InterfaceName,
                degraded: static _ => "not bound")),
        ],
        Counters:
        [
            new LabelledValue("Packets in", status.Counters.PacketsIn.ToString("N0")),
            new LabelledValue("Packets out", status.Counters.PacketsOut.ToString("N0")),
            new LabelledValue("Data in", FormatBytes(status.Counters.BytesIn)),
            new LabelledValue("Data out", FormatBytes(status.Counters.BytesOut)),
        ]);

    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

    private static string FormatBytes(ulong value)
    {
        double scaled = value;
        var unit = 0;
        while (scaled >= 1024 && unit < Units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:N0} {Units[unit]}"
            : $"{scaled:N1} {Units[unit]}";
    }
}

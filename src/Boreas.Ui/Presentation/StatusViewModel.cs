using System.Numerics;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The status screen. Renders what the service reported and sends intent.
/// </summary>
public sealed class StatusViewModel : ObservableObject, IDisposable
{
    private readonly IControlChannel _channel;

    // Plain fields cache derived values; these properties have no setter for
    // the C# 14 `field` keyword to support.
    private StatusPresentation? _presentation;
    private ChannelPresentation? _channelPresentation;
    private SessionFacts? _facts;

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
    /// Derived from the channel, never stored as independent state, and
    /// memoized only for the lifetime of one channel state.
    /// </summary>
    /// <remarks>
    /// The change handler clears the cache before notifying bindings, so each
    /// channel state is derived once without serving stale data.
    /// </remarks>
    public StatusPresentation Presentation =>
        _presentation ??= StatusPresentation.For(_channel.Channel, _channel.State);

    public ChannelPresentation Channel =>
        _channelPresentation ??= ChannelPresentation.For(_channel.Channel);

    /// <summary>
    /// Verb label for the primary action.
    /// </summary>
    /// <remarks>
    /// Stop is expected and reversible, so it does not use destructive styling
    /// or confirmation.
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
    /// Session facts, with <see cref="SessionFacts.None"/> for no session.
    /// </summary>
    public SessionFacts Facts => _facts ??= _channel.Channel is ControlChannelState.Connected
        ? _channel.State.Match(
            stopped: static _ => SessionFacts.None,
            starting: static _ => SessionFacts.None,
            running: static r => SessionFacts.From(r.Session, r.Status),
            stopping: static _ => SessionFacts.None,
            failed: static _ => SessionFacts.None)
        : SessionFacts.None;

    /// <summary>
    /// Whether the connected service reports a running session.
    /// </summary>
    public bool HasFacts =>
        _channel.Channel is ControlChannelState.Connected && _channel.State is ServiceState.Running;

    /// <summary>
    /// Bypass warning, kept separate from the primary status.
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
        // Re-read at press time; the service state may have changed since render.
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
        // Invalidate before notifying so bindings recompute current values.
        _presentation = null;
        _channelPresentation = null;
        _facts = null;

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
/// The status view shows identity, counters, and absolute start time; other
/// snapshot fields stay in diagnostics.
/// </remarks>
public sealed record SessionFacts(
    string SessionId,
    string RunningSince,
    IReadOnlyList<LabelledValue> Identity,
    IReadOnlyList<LabelledValue> Counters)
{
    /// <summary>Empty session facts, so bindings never receive null.</summary>
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

    /// <summary>
    /// A byte count in the largest unit that leaves it above one.
    /// </summary>
    /// <remarks>
    /// Base-1024 units use <see cref="BitOperations.Log2(ulong)"/>; zero maps
    /// to bytes by convention, and powers of two make shift scaling exact.
    /// </remarks>
    private static string FormatBytes(ulong value)
    {
        var unit = Math.Min(BitOperations.Log2(value) / 10, Units.Length - 1);

        return unit == 0
            ? $"{value:N0} {Units[unit]}"
            : $"{value / (double)(1UL << (unit * 10)):N1} {Units[unit]}";
    }
}

namespace Boreas.Ui.Contracts;

/// <summary>
/// One control-plane event, as the service reported it.
/// </summary>
/// <remarks>
/// This bounded lifecycle record carries transitions and typed errors, never
/// packet data or arbitrary service logs.
/// </remarks>
public sealed record ControlEvent(
    DateTimeOffset At,
    ControlEventKind Kind,
    string Summary,
    TypedError? Error = null)
{
    /// <summary>
    /// Stable identity for list virtualisation and keyed updates.
    /// </summary>
    /// <remarks>
    /// Version 7 embeds creation time, so IDs sort with the event list.
    /// </remarks>
    public Guid Id { get; } = Guid.CreateVersion7();
}

public enum ControlEventKind
{
    /// <summary>The service reported a state transition.</summary>
    Transition,

    /// <summary>The client sent a command and the service correlated a response.</summary>
    Command,

    /// <summary>The control channel connected, dropped, or was refused.</summary>
    Channel,

    /// <summary>The service reported a typed failure.</summary>
    Failure,
}

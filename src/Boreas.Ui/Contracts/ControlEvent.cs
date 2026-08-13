namespace Boreas.Ui.Contracts;

/// <summary>
/// One control-plane event, as the service reported it.
/// </summary>
/// <remarks>
/// This is a lifecycle record, not a log stream. The pipe "does not carry
/// packet payloads, native pointers, Wintun handles, arbitrary file paths, or
/// arbitrary log streams", so the diagnostics view shows transitions and typed
/// errors and nothing else. Anyone who needs the service log reads it where the
/// service writes it, with the privileges that requires.
/// </remarks>
public sealed record ControlEvent(
    DateTimeOffset At,
    ControlEventKind Kind,
    string Summary,
    TypedError? Error = null)
{
    /// <summary>Stable identity for list virtualisation and keyed updates.</summary>
    public Guid Id { get; } = Guid.NewGuid();
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

namespace Boreas.Ui.Contracts;

/// <summary>
/// The service state model from docs/platform-integration.md, closed.
/// </summary>
/// <remarks>
/// Only the service drives transitions; the client never infers state from UI
/// state, process liveness, or command duration. The private constructor keeps
/// cases closed, so adding one requires every <see cref="Match"/> caller to
/// handle it.
/// </remarks>
public abstract record ServiceState
{
    private ServiceState() { }

    public sealed record Stopped : ServiceState;

    public sealed record Starting : ServiceState;

    public sealed record Running(SessionIdentity Session, SessionStatus Status) : ServiceState;

    public sealed record Stopping(SessionIdentity Session) : ServiceState;

    /// <summary>
    /// A typed failure of a named operation. <paramref name="Recoverable"/> is
    /// the service's judgement, not the client's: it decides whether the user
    /// is offered a retry or told what to fix first.
    /// </summary>
    public sealed record Failed(ControlOperation Operation, TypedError Error, bool Recoverable) : ServiceState;

    public TResult Match<TResult>(
        Func<Stopped, TResult> stopped,
        Func<Starting, TResult> starting,
        Func<Running, TResult> running,
        Func<Stopping, TResult> stopping,
        Func<Failed, TResult> failed) => this switch
        {
            Stopped s => stopped(s),
            Starting s => starting(s),
            Running s => running(s),
            Stopping s => stopping(s),
            Failed s => failed(s),
            _ => throw Unreachable.Value(this),
        };
}

/// <summary>The operations the control pipe exposes, per the logical interface v1.</summary>
public enum ControlOperation
{
    Start,
    Stop,
    ConfigurationChanged,
    NetworkChanged,
    StatusSnapshot,
}

/// <summary>
/// Opaque session identity. The client displays it and correlates responses
/// against it; it never interprets its structure.
/// </summary>
public readonly record struct SessionIdentity(string Value)
{
    public override string ToString() => Value;
}

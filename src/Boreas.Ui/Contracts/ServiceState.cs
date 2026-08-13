namespace Boreas.Ui.Contracts;

/// <summary>
/// The service state model from docs/platform-integration.md, closed.
/// </summary>
/// <remarks>
/// Only the service drives transitions. The client renders what it is given
/// and never derives state from button state, process liveness, or how long a
/// command has been outstanding.
///
/// The private constructor plus nested cases makes the hierarchy genuinely
/// closed: no other assembly and no other file can add a case. Adding one here
/// changes <see cref="Match"/>'s signature, which fails the build at every
/// site that renders a state.
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

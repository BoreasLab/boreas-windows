namespace Boreas.Ui.Contracts;

/// <summary>
/// The state of the local control pipe, which is not the state of the tunnel.
/// </summary>
/// <remarks>
/// Conflating these two is the single most damaging mistake this client could
/// make. "The service is unreachable" and "the tunnel is stopped" look similar
/// and mean opposite things: the first says nothing at all about whether
/// traffic is being carried, and telling a user their tunnel is off when the
/// service simply restarted would be a lie about their network.
///
/// So the client shows the channel separately, always, and suppresses every
/// tunnel claim while the channel is not <see cref="Connected"/>.
/// </remarks>
public abstract record ControlChannelState
{
    private ControlChannelState() { }

    /// <summary>First connection, or reconnection after the service restarted.</summary>
    public sealed record Connecting : ControlChannelState;

    public sealed record Connected(int ProtocolVersion) : ControlChannelState;

    /// <summary>
    /// The pipe is not answering. The service may be stopped, starting, or
    /// mid-upgrade; the client cannot tell and does not guess.
    /// </summary>
    public sealed record Unavailable(TypedError Error) : ControlChannelState;

    /// <summary>
    /// The service rejected this Windows principal before decoding any command
    /// payload. This is an installation policy outcome, not a bug to retry.
    /// </summary>
    public sealed record Unauthorized : ControlChannelState;

    /// <summary>
    /// Client and service disagree on the protocol version. Commands are not
    /// sent, because an unknown version is rejected by the service anyway and
    /// sending one would only produce a confusing typed error.
    /// </summary>
    public sealed record VersionMismatch(int ClientVersion, int ServiceVersion) : ControlChannelState;

    public TResult Match<TResult>(
        Func<Connecting, TResult> connecting,
        Func<Connected, TResult> connected,
        Func<Unavailable, TResult> unavailable,
        Func<Unauthorized, TResult> unauthorized,
        Func<VersionMismatch, TResult> versionMismatch) => this switch
        {
            Connecting s => connecting(s),
            Connected s => connected(s),
            Unavailable s => unavailable(s),
            Unauthorized s => unauthorized(s),
            VersionMismatch s => versionMismatch(s),
            _ => throw Unreachable.Value(this),
        };

    /// <summary>True only when a control command may be sent.</summary>
    public bool CanSendCommands => this is Connected;
}

namespace Boreas.Ui.Contracts;

/// <summary>
/// The state of the local control pipe, which is not the state of the tunnel.
/// </summary>
/// <remarks>
/// An unavailable service does not prove that the tunnel stopped. The client
/// therefore shows channel state separately and suppresses tunnel claims until
/// <see cref="Connected"/>.
/// </remarks>
public abstract record ControlChannelState
{
    private ControlChannelState() { }

    /// <summary>Initial connection or reconnection after service restart.</summary>
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
    /// <remarks>
    /// Client version is read from <see cref="ControlProtocol.Version"/>, not
    /// supplied as a constructor value that could describe another client.
    /// </remarks>
    public sealed record VersionMismatch(int ServiceVersion) : ControlChannelState
    {
        public int ClientVersion => ControlProtocol.Version;
    }

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

    public bool CanSendCommands => this is Connected;
}

using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// A tunnel that is carrying traffic.
/// </summary>
/// <remarks>
/// Narrow on purpose: this is everything <see cref="NativeControlChannel"/>
/// needs from a running session and nothing else. Reading counters, replacing
/// rules, and stopping are the three things a control surface does; the handle,
/// the reader thread, and the teardown order stay behind it, in
/// <c>NativeTunnel</c>, where they are the same three steps whatever is driving
/// them.
/// </remarks>
public interface IRunningTunnel : IDisposable
{
    /// <summary>Traffic across the adapter since the session started.</summary>
    SessionCounters Counters { get; }

    /// <summary>Whether upstream sockets are provably outside the tunnel.</summary>
    EgressBypass Bypass { get; }

    /// <summary>
    /// Replaces the rules in force without restarting or dropping a connection.
    /// </summary>
    TunnelEvent.Reloaded Reload(IReadOnlyCollection<string> lists);

    /// <summary>Shutdown, join the reader, free the handle. In that order.</summary>
    BoreasStatus Stop();
}

/// <summary>
/// Creates adapters and starts tunnels on them.
/// </summary>
/// <remarks>
/// <para>
/// The one seam between the control surface and the operating system. Behind it
/// are the acts that need a privileged process - creating a Wintun adapter,
/// setting an interface MTU, writing the current user's trust store - and in
/// front of it is a state machine that needs none of them.
/// </para>
/// <para>
/// That split is why <see cref="NativeControlChannel"/> compiles and its laws
/// run on a machine with neither Windows nor a boreas library.
/// </para>
/// </remarks>
public interface ITunnelHost
{
    /// <summary>
    /// Brings up an adapter and starts a tunnel on it.
    /// </summary>
    /// <param name="onEvent">
    /// Called from the tunnel's own reader thread, never the caller's. The
    /// channel is what moves it to the UI thread.
    /// </param>
    /// <remarks>
    /// Blocks for as long as the first connection takes: a DNS lookup, a
    /// handshake. The channel calls it off the UI thread.
    /// </remarks>
    IRunningTunnel Start(ValidatedConfiguration configuration, Action<TunnelEvent> onEvent);
}

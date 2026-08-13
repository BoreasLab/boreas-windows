namespace Boreas.Ui.Contracts;

/// <summary>
/// The immutable status snapshot for a running session.
/// </summary>
/// <remarks>
/// Everything here comes from <c>status_snapshot</c>, which "never returns
/// packet payloads". Counters are bounded and coarse by design: they exist so
/// a user can tell a live tunnel from a silent one, not so anyone can inspect
/// traffic from the control client.
/// </remarks>
public sealed record SessionStatus(
    string AdapterName,
    string InterfaceAddress,
    int Mtu,
    DateTimeOffset RunningSince,
    SessionCounters Counters,
    EgressBypass Bypass);

/// <param name="PacketsIn">Packets admitted from the adapter since the session started.</param>
/// <param name="PacketsOut">Packets written back to the adapter since the session started.</param>
/// <param name="BytesIn">Bytes admitted from the adapter since the session started.</param>
/// <param name="BytesOut">Bytes written back to the adapter since the session started.</param>
public readonly record struct SessionCounters(
    ulong PacketsIn,
    ulong PacketsOut,
    ulong BytesIn,
    ulong BytesOut);

/// <summary>
/// Whether upstream sockets are provably outside the tunnel.
/// </summary>
/// <remarks>
/// docs/platform-integration.md makes this a first-class outcome rather than a
/// detail: an upstream socket that follows the default route loops back into
/// Boreas, and the host must report typed degradation when it cannot bind the
/// physical interface. A user whose bypass has degraded is in a materially
/// different situation from one whose tunnel is simply running, so the status
/// view says so instead of showing an unqualified "Protected".
/// </remarks>
public abstract record EgressBypass
{
    private EgressBypass() { }

    /// <summary>Upstream traffic is bound to a named physical interface.</summary>
    public sealed record Bound(string InterfaceName) : EgressBypass;

    /// <summary>The host could not establish the bypass and reported why.</summary>
    public sealed record Degraded(TypedError Error) : EgressBypass;

    public TResult Match<TResult>(
        Func<Bound, TResult> bound,
        Func<Degraded, TResult> degraded) => this switch
        {
            Bound s => bound(s),
            Degraded s => degraded(s),
        };
}

namespace Boreas.Ui.Contracts;

/// <summary>
/// The immutable status snapshot for a running session.
/// </summary>
/// <remarks>
/// <c>status_snapshot</c> returns bounded, coarse counters for liveness, never
/// packet payloads for inspection.
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
/// Without a physical-interface binding, upstream sockets can loop back into
/// Boreas. Degradation is therefore reported instead of claiming protection.
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
            _ => throw Unreachable.Value(this),
        };
}

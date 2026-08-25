using System.Collections.Immutable;
using Boreas.Interop.Native;

namespace Boreas.Interop.Tunnel;

/// <summary>What the NAT in front of this host does to a mapping.</summary>
/// <remarks>
/// Boreas cannot measure this. <see cref="AddressAndPortDependent"/> is the
/// conservative answer: it never claims more than is true, at the cost of
/// steering some flows that would have worked.
/// </remarks>
public enum NatBehavior
{
    EndpointIndependent = 0,
    AddressDependent = 1,
    AddressAndPortDependent = 2,
}

/// <summary>The cryptographic peer, and where it currently lives.</summary>
/// <remarks>
/// The endpoint is not part of the key material: a peer that roams keeps its
/// keys and changes its address.
/// </remarks>
public sealed record WireGuardPeer(
    HostPort Endpoint,
    Key32 PrivateKey,
    Key32 PeerPublicKey,
    Key32? PresharedKey)
{
    /// <summary>
    /// Null and "thirty-two zero bytes" are different peers, which is why the
    /// ABI carries a separate flag and this carries a nullable rather than an
    /// all-zero sentinel.
    /// </summary>
    public bool HasPresharedKey => PresharedKey is not null;
}

/// <summary>
/// Where traffic leaves by.
/// </summary>
/// <remarks>
/// Two arms, and that is the whole set the C ABI exposes today. The proxy
/// egresses the Rust API has - SOCKS5, Shadowsocks, VLESS, Hysteria2 - are
/// listed in api/abi.md as not exposed yet. A third arm here would be a
/// promise this build cannot keep.
/// </remarks>
public abstract record Egress
{
    private Egress() { }

    /// <summary>Out by the host's own routes. Nothing is proxied.</summary>
    public sealed record Direct(NatBehavior NatBehavior) : Egress;

    /// <summary>A WireGuard peer, carrying whole IP packets.</summary>
    public sealed record WireGuard(WireGuardPeer Peer) : Egress;

    public TResult Match<TResult>(
        Func<Direct, TResult> direct,
        Func<WireGuard, TResult> wireGuard) => this switch
        {
            Direct e => direct(e),
            WireGuard e => wireGuard(e),
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(Egress)}: {this}"),
        };
}

/// <summary>
/// The certificate authority interception mints leaves from.
/// </summary>
/// <remarks>
/// <b>Both halves or neither.</b> Supplying one is BOREAS_CONFIG, and a closed
/// sum is why that combination cannot be written here. The two halves live in
/// different stores - a certificate the trust installer can read, keys under
/// DPAPI - so one really can be written without the other, and the failure that
/// produces is the one nothing downstream can detect: every parse succeeds, the
/// session starts, and it mints leaves the installed root cannot vouch for.
/// </remarks>
public abstract record Trust
{
    private Trust() { }

    /// <summary>Generate a fresh authority. The user must trust the new root.</summary>
    public sealed record Generate : Trust
    {
        public static readonly Generate Instance = new();
    }

    /// <summary>Hand back what was stored last launch.</summary>
    public sealed record Restore(ImmutableArray<byte> RootCertificate, ImmutableArray<byte> Keys) : Trust;

    public TResult Match<TResult>(
        Func<Generate, TResult> generate,
        Func<Restore, TResult> restore) => this switch
        {
            Generate t => generate(t),
            Restore t => restore(t),
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(Trust)}: {this}"),
        };
}

/// <summary>
/// The hosts interception applies to: at least one, always.
/// </summary>
/// <remarks>
/// Interception with an empty host list is BOREAS_CONFIG, because forging
/// certificates for the empty set is the name tier with extra machinery. A
/// refined collection is what stops that being writable.
/// </remarks>
public sealed record InterceptHosts
{
    public const string Requirement = "Name at least one host to intercept.";

    private InterceptHosts(ImmutableArray<Hostname> value) => Value = value;

    public ImmutableArray<Hostname> Value { get; }

    public static InterceptHosts? TryCreate(IEnumerable<Hostname> hosts)
    {
        var value = hosts.ToImmutableArray();

        return value.IsEmpty ? null : new InterceptHosts(value);
    }

    /// <summary>Structural, because ImmutableArray compares by reference.</summary>
    public bool Equals(InterceptHosts? other) =>
        other is not null && Value.SequenceEqual(other.Value);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var host in Value)
        {
            hash.Add(host);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Terminating TLS for a readable allowlist of hosts, and what to do inside.
/// </summary>
/// <remarks>
/// Nesting document rewriting here makes it impossible to request rewriting
/// without interception.
/// </remarks>
public sealed record Interception(InterceptHosts Hosts, Trust Trust, bool RewriteDocuments);

/// <summary>
/// How names are answered, and therefore whether anything can be filtered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lists live inside <see cref="Local"/>, and that placement is the
/// point.</b> A tunnel that filters but never sees a question is refused,
/// because on the packet fast path a flow is selected for inspection
/// <i>because a DNS answer named its address</i> - so a tunnel with no resolver
/// can never select one. It would carry traffic, filter nothing, and look
/// configured. Nesting the lists under the resolver makes that combination
/// unrepresentable rather than rejected at startup.
/// </para>
/// <para>
/// <b>The upstream is cleartext DNS.</b> DoT, DoH and DoQ are on api/abi.md's
/// not-yet-exposed list, so an upstream reached across an untrusted network is
/// readable by anything on the path. That is a real gap, not a preference, and
/// it is why nothing here offers to pick a public resolver.
/// </para>
/// </remarks>
public abstract record Resolution
{
    private Resolution() { }

    /// <summary>
    /// Queries cross the tunnel untouched. Nothing is filtered and nothing is
    /// intercepted, which is why neither is representable on this arm.
    /// </summary>
    public sealed record Passthrough : Resolution
    {
        public static readonly Passthrough Instance = new();
    }

    /// <summary>
    /// Answered here against the lists, forwarding what policy allows.
    /// </summary>
    /// <param name="Lists">
    /// Filter-list text in AdGuard/uBlock syntax. Empty is legal: it answers
    /// locally and blocks nothing. Malformed lines are counted and skipped, so
    /// one bad line in fifty thousand does not cost a rule set.
    /// </param>
    public sealed record Local(
        HostPort Upstream,
        ImmutableArray<string> Lists,
        Interception? Interception) : Resolution;

    public TResult Match<TResult>(
        Func<Passthrough, TResult> passthrough,
        Func<Local, TResult> local) => this switch
        {
            Passthrough r => passthrough(r),
            Local r => local(r),
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(Resolution)}: {this}"),
        };
}

/// <summary>
/// Resource ceilings. Zero selects the core default for that ceiling.
/// </summary>
/// <remarks>
/// The host supplies these because the core cannot identify whether it runs on
/// a phone or desktop. <see cref="Desktop"/> is the product's desktop profile.
/// </remarks>
public readonly record struct Ceilings(
    nuint BufferSlices = 0,
    nuint DatagramsPerFlow = 0,
    nuint TerminatedConnections = 0,
    nuint Associations = 0,
    nuint InspectedAddresses = 0,
    nuint PendingReassemblies = 0)
{
    /// <summary>The core's own defaults, which are sized for a phone.</summary>
    public static Ceilings Phone => default;

    /// <summary>
    /// Four times the phone defaults, except the connection ceiling.
    /// </summary>
    /// <remarks>
    /// <c>terminated_connections</c> also sizes the forged-certificate cache
    /// and has a floor of (inspected ports x 64) beneath which start is refused
    /// with BOREAS_TERMINATION, so it is raised furthest. The rest are
    /// proportional: a desktop's traffic is wider than a phone's in the same
    /// direction, not differently shaped.
    /// </remarks>
    public static Ceilings Desktop => new(
        BufferSlices: 8192,
        DatagramsPerFlow: 128,
        TerminatedConnections: 4096,
        Associations: 1024,
        InspectedAddresses: 4096,
        PendingReassemblies: 256);

    internal BoreasCeilings ToNative() => new()
    {
        BufferSlices = BufferSlices,
        DatagramsPerFlow = DatagramsPerFlow,
        TerminatedConnections = TerminatedConnections,
        Associations = Associations,
        InspectedAddresses = InspectedAddresses,
        PendingReassemblies = PendingReassemblies,
    };
}

/// <remarks>
/// <para>
/// api/abi.md lists ten combinations that produce BOREAS_CONFIG. Six of them
/// cannot be written down here at all: the two vtable callbacks are filled in
/// by code rather than by a caller, the MTU is one value used for both fields,
/// the authority is a closed sum so one half without the other has no spelling,
/// the lists sit under the resolver, and the intercept host list is a refined
/// non-empty collection. The other four are the smart constructors on
/// <see cref="Mtu"/>, <see cref="HostPort"/> and <see cref="Hostname"/>, which
/// reject with a sentence naming the field rather than with a status code the
/// caller has to bisect.
/// </para>
/// </remarks>
public sealed record TunnelConfiguration(
    Egress Egress,
    Resolution Resolution,
    Mtu Mtu,
    Ceilings Ceilings);

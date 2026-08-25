using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Boreas.Interop.Native;

/// <summary>Where traffic leaves by. Four bytes, signed.</summary>
public enum BoreasEgress
{
    /// <summary>Out by the host's own routes. Nothing is proxied.</summary>
    Direct = 0,

    /// <summary>A WireGuard peer, carrying whole IP packets.</summary>
    WireGuard = 1,
}

/// <summary>What the NAT in front of this host does to a mapping.</summary>
/// <remarks>
/// Boreas cannot measure this. <see cref="AddressAndPortDependent"/> is the
/// conservative answer: it never claims more than is true, at the cost of
/// steering some flows that would have worked.
/// </remarks>
public enum BoreasNat
{
    EndpointIndependent = 0,
    AddressDependent = 1,
    AddressAndPortDependent = 2,
}

public enum BoreasEventKind
{
    Resolved = 0,
    Reloaded = 1,
    Counted = 2,
}

/// <summary>
/// Thirty-two raw key bytes, laid out as <c>uint8_t[32]</c>.
/// </summary>
/// <remarks>
/// An inline array rather than a <c>fixed</c> buffer: both produce thirty-two
/// contiguous bytes at alignment one, and this one is indexable and
/// span-convertible without the enclosing struct becoming unsafe. Raw bytes,
/// never the base64 a WireGuard configuration file carries.
/// </remarks>
[InlineArray(Length)]
public struct BoreasKey
{
    public const int Length = 32;

#pragma warning disable IDE0044, CS0169 // The inline array's single element is written through the indexer.
    private byte _element;
#pragma warning restore IDE0044, CS0169
}

/// <summary>
/// The client's TUN. <b>Every callback here is called from an arbitrary worker
/// thread, and not always the same one.</b>
/// </summary>
/// <remarks>
/// Function pointers rather than delegates. api/windows.md requires
/// <c>[UnmanagedCallersOnly]</c> and <c>&amp;Method</c>, because
/// <c>Marshal.GetFunctionPointerForDelegate</c> obliges the caller to root the
/// delegate for as long as native code might call it, and a collected delegate
/// is a call through freed memory. There is no heap object here to collect.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoreasDevice
{
    /// <summary>Passed back to every call, untouched. May be null.</summary>
    public nint Context;

    /// <summary>
    /// Reads one IP packet. Returns the byte count, <b>zero for "nothing yet,
    /// ask again"</b>, or a negative errno. Required.
    /// </summary>
    public delegate* unmanaged<nint, byte*, nuint, nint> Recv;

    /// <summary>
    /// Writes one IP packet, whole. Returns zero or a negative errno; a short
    /// write is an error, not a success with a count. Required.
    /// </summary>
    public delegate* unmanaged<nint, byte*, nuint, nint> Send;

    /// <summary>
    /// Makes an in-flight <see cref="Recv"/> return promptly. Called before
    /// <see cref="Release"/> and possibly while a <see cref="Recv"/> is
    /// running. May be null when <see cref="Recv"/> never blocks indefinitely.
    /// </summary>
    public delegate* unmanaged<nint, void> Close;

    /// <summary>
    /// Releases <see cref="Context"/>. Called exactly once, after every other
    /// callback has returned. May be null.
    /// </summary>
    public delegate* unmanaged<nint, void> Release;

    /// <summary>The MTU the interface is configured with. At least 1280.</summary>
    public ushort Mtu;
}

/// <summary>
/// Sockets that do not re-enter the tunnel.
/// </summary>
/// <remarks>
/// <see cref="Protect"/> is required rather than defaultable, and that is
/// deliberate: a default would be "do nothing", which is correct on a desktop
/// whose default route is not the tunnel and catastrophically wrong everywhere
/// else. A <c>protect</c> that returns zero without doing anything is the bug,
/// written out.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BoreasBypass
{
    public nint Context;

    /// <summary>
    /// Excludes one socket. Returns zero on success, negative on refusal. The
    /// socket is a Windows <c>SOCKET</c> widened to signed 64 bits, not a file
    /// descriptor. Required.
    /// </summary>
    public delegate* unmanaged<nint, long, int> Protect;

    public delegate* unmanaged<nint, void> Release;
}

/// <summary>
/// How much this tunnel may hold. <b>Zero in any field means "use the default
/// for it"</b>, so <c>default</c> is a valid value and gives the phone-sized
/// defaults throughout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoreasCeilings
{
    /// <summary>Payload buffers, shared by everything in flight. Default 2048.</summary>
    public nuint BufferSlices;

    /// <summary>Queued datagrams before one flow starts dropping. Default 32.</summary>
    public nuint DatagramsPerFlow;

    /// <summary>
    /// Live locally-terminated connections. Default 512, and it has a floor:
    /// below (inspected ports x 64), start fails with
    /// <see cref="BoreasStatus.Termination"/>.
    /// </summary>
    public nuint TerminatedConnections;

    /// <summary>Datagram associations through a proxy egress. Default 256.</summary>
    public nuint Associations;

    /// <summary>Addresses remembered as belonging to an intercepted host. Default 1024.</summary>
    public nuint InspectedAddresses;

    /// <summary>Fragmented packets held awaiting the rest of themselves. Default 64.</summary>
    public nuint PendingReassemblies;
}

/// <summary>The cryptographic peer, read only when egress is WireGuard.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoreasWireGuard
{
    /// <summary>
    /// NUL-terminated UTF-8 <c>"host:port"</c>, with a numeric address. Not
    /// part of the key material: a peer that roams keeps its keys and changes
    /// its address.
    /// </summary>
    public nint Endpoint;

    public BoreasKey PrivateKey;

    public BoreasKey PeerPublicKey;

    public BoreasKey PresharedKey;

    /// <remarks>
    /// A separate flag because thirty-two zero bytes is a key somebody may
    /// legitimately have configured, so "all zero" cannot mean "absent".
    /// </remarks>
    [MarshalAs(UnmanagedType.U1)]
    public bool HasPresharedKey;
}

/// <summary>
/// One tunnel, described completely. Every pointer in it is borrowed for the
/// duration of <see cref="Boreas.boreas_tunnel_start"/> only.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoreasConfig
{
    public BoreasEgress Egress;

    /// <summary>Read only when <see cref="Egress"/> is WireGuard.</summary>
    public BoreasWireGuard WireGuard;

    /// <summary>Read only when <see cref="Egress"/> is Direct.</summary>
    public BoreasNat NatBehavior;

    /// <summary>
    /// NUL-terminated UTF-8 <c>"host:port"</c> of a DNS upstream to filter
    /// through, or null to forward queries untouched.
    /// </summary>
    /// <remarks>
    /// Null here with a non-empty list set is <see cref="BoreasStatus.Config"/>:
    /// on the packet path a flow is selected for inspection because a DNS
    /// answer named its address, so a tunnel that never sees a question can
    /// filter nothing. It would carry traffic, filter nothing, and look
    /// configured.
    /// </remarks>
    public nint Resolver;

    /// <summary>A <c>const char *const *</c> of filter-list text.</summary>
    public nint Lists;

    public nuint ListCount;

    /// <summary>
    /// Hosts to intercept: <b>an allowlist, never a pattern</b>. Zero means no
    /// interception, which needs no certificate authority.
    /// </summary>
    public nint InterceptHosts;

    public nuint InterceptHostCount;

    /// <summary>Stored authority material, or null to generate. Both halves together.</summary>
    public nint RootCertificate;

    public nuint RootCertificateLen;

    public nint AuthorityKeys;

    public nuint AuthorityKeysLen;

    /// <summary>Whether to rewrite HTML bodies as they stream. Interception only.</summary>
    [MarshalAs(UnmanagedType.U1)]
    public bool RewriteDocuments;

    /// <summary>
    /// The MTU configured on the TUN. The same number as
    /// <see cref="BoreasDevice.Mtu"/>, and at least 1280.
    /// </summary>
    /// <remarks>
    /// Telling the two sides different numbers produces a tunnel that starts,
    /// reports itself healthy, and spends its time answering Packet Too Big to
    /// senders that never converge. The only symptom is a sustained non-zero
    /// <see cref="BoreasCounters.PathsReported"/>.
    /// </remarks>
    public ushort Mtu;

    public BoreasCeilings Ceilings;
}

/// <summary>
/// Occurrences since the previous counted event.
/// </summary>
/// <remarks>
/// Every field is a thing that went wrong or was refused, so a tunnel working
/// normally reports zeroes and a host can surface any non-zero field without
/// knowing what it means. Values are per-interval, so they sum rather than
/// diff.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct BoreasCounters
{
    /// <summary>Ceilings too small for this device's traffic.</summary>
    public ulong DatagramsDropped;

    /// <summary>Something upstream is producing malformed packets.</summary>
    public ulong PacketsRejected;

    /// <summary>Expected while intercepting: browsers pushed off HTTP/3.</summary>
    public ulong QuicSteered;

    /// <summary>A misconfiguration: the TUN's MTU is wider than the configured one.</summary>
    public ulong PathsReported;

    /// <summary>Events are not being read fast enough. Counted so a gap never reads as quiet.</summary>
    public ulong EventsLost;

    /// <summary><b>A defect in Boreas</b>, not a condition of the network. Report it.</summary>
    public ulong TasksPanicked;
}

/// <summary>
/// One event. <b>Only the fields <see cref="Kind"/> names carry meaning</b>;
/// the rest are zero.
/// </summary>
/// <remarks>
/// A tag and every arm's fields side by side rather than a union, so no binding
/// generator has to perform an unsafe read. <see cref="BoreasEventView"/> is
/// where this flat product becomes the closed sum it describes.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct BoreasEvent
{
    public BoreasEventKind Kind;

    /// <summary>
    /// Resolved only: the answer came from policy without anything leaving the
    /// device.
    /// </summary>
    /// <remarks>
    /// This is the field the marshalling trap moves. A bare <c>bool</c> is four
    /// bytes to the .NET marshaller and one byte to C, and every field after it
    /// is then read from the wrong offset with no error anywhere.
    /// <c>EventLayout</c> pins it at offset four.
    /// </remarks>
    [MarshalAs(UnmanagedType.U1)]
    public bool Blocked;

    /// <summary>
    /// Resolved only: the <b>full</b> byte length of the name before
    /// truncation. Larger than the capacity passed means it did not all fit.
    /// </summary>
    public nuint NameLen;

    /// <summary>
    /// Resolved only: the full byte length of the rule. Zero means no rule
    /// decided it.
    /// </summary>
    public nuint RuleLen;

    /// <summary>Reloaded only.</summary>
    public nuint Allowed;

    /// <summary>Reloaded only.</summary>
    public nuint BlockedRules;

    /// <summary>Reloaded only.</summary>
    public nuint Inspected;

    /// <summary>Counted only.</summary>
    public BoreasCounters Counters;
}

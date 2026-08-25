using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Boreas.Interop.Bypass;

/// <summary>
/// Naming the outgoing physical interface on a socket, in the byte order each
/// address family asks for.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is a byte-order asymmetry between the two options and it is
/// documented, not folklore.</b> Microsoft's IPPROTO_IP page says the input
/// value for <c>IP_UNICAST_IF</c> is "an interface index in network byte
/// order"; the IPPROTO_IPV6 page says the input value for
/// <c>IPV6_UNICAST_IF</c> is "a 4-byte interface index of the desired outgoing
/// interface in host byte order". Both <i>get</i> in host byte order.
/// </para>
/// <para>
/// Byte-swapping one and not the other is the classic "why doesn't binding
/// work" bug, and here it is worse than that: an unprotected socket works
/// perfectly until the tunnel comes up, at which point every packet it sends
/// re-enters the tunnel it was serving. Nothing errors. The symptom is a
/// resolver that hangs and a proxy that never connects.
/// </para>
/// <para>
/// The two conversions are separated from the socket call so they can be
/// asserted directly. <c>BypassLaws</c> pins them against the wire bytes rather
/// than against each other, which is what makes the law true on a big-endian
/// host as well.
/// </para>
/// </remarks>
internal static partial class UnicastInterface
{
    private const int IpprotoIp = 0;
    private const int IpprotoIpv6 = 41;
    private const int IpUnicastIf = 31;
    private const int Ipv6UnicastIf = 31;

    /// <summary>
    /// The IPv4 option value: the index in <b>network</b> byte order.
    /// </summary>
    public static int IPv4Value(uint index) => IPAddress.HostToNetworkOrder((int)index);

    /// <summary>
    /// The IPv6 option value: the index in <b>host</b> byte order. Not a typo,
    /// and not an oversight; see the remarks on this class.
    /// </summary>
    public static int IPv6Value(uint index) => (int)index;

    /// <returns>Zero when the socket is bound to the interface, negative when it is not.</returns>
    /// <remarks>
    /// <para>
    /// Both are attempted because Boreas hands the socket over <b>before its
    /// family is fixed by a connect</b>, so which of the two is the meaningful
    /// one is not yet knowable here.
    /// </para>
    /// <para>
    /// <b>Success is "at least one applied", not "both applied", and that is a
    /// deliberate departure from the sample in api/windows.md.</b> A socket has
    /// one address family; setting an <c>IPPROTO_IP</c> option on an AF_INET6
    /// socket is rejected by Winsock. A sample that returns -1 when either call
    /// fails therefore refuses every socket it was written to protect, and a
    /// <c>protect</c> that always refuses is a tunnel that never starts.
    /// Reported upstream. Requiring both is the safer-looking rule and the one
    /// that cannot work.
    /// </para>
    /// <para>
    /// <b>Unverified on a device.</b> The reasoning above is from the Winsock
    /// option documentation, not from a run. What must be checked on hardware
    /// is that the surviving call is the one matching the socket's family, so
    /// that "at least one applied" never means "the wrong one applied".
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static unsafe int Bind(long socket, uint index)
    {
        var handle = (nint)socket;

        var v4 = IPv4Value(index);
        var v6 = IPv6Value(index);

        var boundV4 = setsockopt(handle, IpprotoIp, IpUnicastIf, &v4, sizeof(int)) == 0;
        var boundV6 = setsockopt(handle, IpprotoIpv6, Ipv6UnicastIf, &v6, sizeof(int)) == 0;

        return boundV4 || boundV6 ? 0 : -1;
    }

    /// <summary>
    /// <c>SetLastError</c> is on here and off on every Boreas declaration, and
    /// the difference is real: Winsock reports through
    /// <c>WSAGetLastError</c>, whereas every Boreas function returns a status
    /// and nothing sets <c>errno</c>.
    /// </summary>
    [LibraryImport("ws2_32.dll", SetLastError = true)]
    private static unsafe partial int setsockopt(
        nint socket, int level, int optionName, void* optionValue, int optionLength);
}

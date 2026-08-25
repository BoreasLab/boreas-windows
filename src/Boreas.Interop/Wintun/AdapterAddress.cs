using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Boreas.Interop.Wintun;

/// <summary>
/// The address arithmetic the Windows configuration commands need.
/// </summary>
/// <remarks>
/// Pure, and separate from the commands that use it, because this is the part
/// that is silently wrong rather than loudly wrong: a mask off by one bit
/// configures an interface that works for most destinations and not for the
/// ones near the edge of the prefix, which is a fault diagnosed by guesswork
/// weeks later.
/// </remarks>
public static class AdapterAddress
{
    /// <summary>
    /// The dotted-quad subnet mask for an IPv4 prefix length.
    /// </summary>
    /// <remarks>
    /// IPv4 only: <c>netsh interface ipv4 set address</c> takes a mask, while
    /// the IPv6 form takes the prefix length as written. Refusing to answer for
    /// IPv6 is deliberate - returning something would invite a caller to use it.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The prefix length is not one an IPv4 address can carry.
    /// </exception>
    public static IPAddress Mask(int prefixLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, 32);

        // A shift by 32 is undefined for a 32-bit operand in C# as it is in C -
        // it shifts by 32 & 31, which is zero, and /0 would produce 0.0.0.0
        // instead of 255.255.255.255. Widening to 64 bits removes the special
        // case rather than adding a branch for it.
        var bits = (uint)(0xFFFF_FFFFUL << (32 - prefixLength));

        Span<byte> octets = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(octets, bits);

        return new IPAddress(octets);
    }

    /// <summary>Whether an address is one the IPv6 commands apply to.</summary>
    public static bool IsIPv6(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6;
}

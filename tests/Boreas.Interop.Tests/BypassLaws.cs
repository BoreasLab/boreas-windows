using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Boreas.Interop.Bypass;

namespace Boreas.Interop.Tests;

/// <summary>
/// The bypass vtable, and the byte-order asymmetry that is silent when it is
/// wrong.
/// </summary>
public sealed unsafe class BypassLaws
{
    /// <summary>
    /// The four bytes <c>setsockopt</c> reads, given an option value.
    /// </summary>
    /// <remarks>
    /// Winsock is handed a pointer and a length, so what it sees is the value's
    /// representation in memory. Going through the bytes rather than comparing
    /// the two integers is what makes these laws true on a big-endian host as
    /// well as on the two this ships to.
    /// </remarks>
    private static byte[] OptionBytes(int value)
    {
        var bytes = new byte[sizeof(int)];
        MemoryMarshal.Write(bytes, in value);
        return bytes;
    }

    /// <summary>
    /// <b>IPv4 takes the index in network byte order.</b> Microsoft's
    /// IPPROTO_IP page: "This DWORD parameter must be an interface index in
    /// network byte order."
    /// </summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(0x01020304u)]
    [InlineData(uint.MaxValue)]
    public void The_ipv4_option_carries_the_index_in_network_byte_order(uint index) =>
        Assert.Equal(index, BinaryPrimitives.ReadUInt32BigEndian(OptionBytes(UnicastInterface.IPv4Value(index))));

    /// <summary>
    /// <b>IPv6 takes the index in host byte order.</b> Microsoft's IPPROTO_IPV6
    /// page: "a 4-byte interface index of the desired outgoing interface in
    /// host byte order."
    /// </summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(0x01020304u)]
    [InlineData(uint.MaxValue)]
    public void The_ipv6_option_carries_the_index_in_host_byte_order(uint index) =>
        Assert.Equal(index, MemoryMarshal.Read<uint>(OptionBytes(UnicastInterface.IPv6Value(index))));

    /// <summary>
    /// The two really are different values, which is the entire content of the
    /// trap. A refactor that "tidied" one call to match the other would pass
    /// both laws above only on a big-endian host, and this is what refuses it
    /// on the two architectures that actually ship.
    /// </summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(0x01020304u)]
    public void The_two_families_disagree_about_the_same_index(uint index)
    {
        if (!BitConverter.IsLittleEndian)
        {
            return;
        }

        Assert.NotEqual(UnicastInterface.IPv4Value(index), UnicastInterface.IPv6Value(index));
    }

    /// <summary>
    /// Index zero is not an interface. Winsock accepts it as "unspecified",
    /// which leaves the socket on the default route - and once the tunnel is
    /// up, the default route is the tunnel. Refusing is the only answer that is
    /// not a silent leak.
    /// </summary>
    [Fact]
    public void An_unknown_interface_is_refused_rather_than_left_unspecified()
    {
        var attempts = 0;
        var bypass = new PhysicalInterfaceBypass(0, (_, _) => { attempts++; return 0; });

        Assert.True(bypass.Protect(4242) < 0);
        Assert.Equal(0, attempts);
    }

    /// <summary>
    /// A laptop moving from Wi-Fi to Ethernet changes the index, so it is read
    /// on every call rather than captured once.
    /// </summary>
    [Fact]
    public void The_interface_index_is_read_on_every_call()
    {
        var seen = new List<uint>();
        var bypass = new PhysicalInterfaceBypass(7, (_, index) => { seen.Add(index); return 0; });

        Assert.Equal(0, bypass.Protect(1));

        bypass.InterfaceIndex = 9;
        Assert.Equal(0, bypass.Protect(2));

        Assert.Equal([7u, 9u], seen);
    }

    /// <summary>
    /// The socket crosses as <c>int64_t</c> because a Windows SOCKET is an
    /// unsigned pointer-width handle and one of the two platforms uses the top
    /// bit. It must arrive unchanged.
    /// </summary>
    [Fact]
    public void The_socket_arrives_unchanged_including_its_top_bit()
    {
        var seen = new List<long>();
        var bypass = new PhysicalInterfaceBypass(7, (socket, _) => { seen.Add(socket); return 0; });
        var vtable = BypassVtable.For(bypass);

        try
        {
            long[] sockets = [0, 1, int.MaxValue, long.MaxValue, -1, long.MinValue];

            foreach (var socket in sockets)
            {
                _ = vtable.Protect(vtable.Context, socket);
            }

            Assert.Equal(sockets, seen);
        }
        finally
        {
            BypassVtable.Abandon(vtable);
        }
    }

    /// <summary>
    /// Refusing is the safe answer. A socket reported as protected when it is
    /// not re-enters the tunnel, and nothing downstream can detect that.
    /// </summary>
    [Fact]
    public void A_binder_that_throws_becomes_a_refusal_not_a_crash()
    {
        var bypass = new PhysicalInterfaceBypass(7, (_, _) => throw new InvalidOperationException("winsock"));
        var vtable = BypassVtable.For(bypass);

        try
        {
            Assert.True(vtable.Protect(vtable.Context, 4242) < 0);
        }
        finally
        {
            BypassVtable.Abandon(vtable);
        }
    }

    /// <summary>
    /// A refusal from the binder is passed through rather than converted into
    /// success. This is the one the whole vtable exists for.
    /// </summary>
    [Fact]
    public void A_refusal_from_winsock_is_a_refusal_from_protect()
    {
        var bypass = new PhysicalInterfaceBypass(7, (_, _) => -1);
        var vtable = BypassVtable.For(bypass);

        try
        {
            Assert.True(vtable.Protect(vtable.Context, 4242) < 0);
        }
        finally
        {
            BypassVtable.Abandon(vtable);
        }
    }

    [Fact]
    public void Both_callbacks_are_present()
    {
        var vtable = BypassVtable.For(new PhysicalInterfaceBypass(7, (_, _) => 0));

        try
        {
            Assert.False(vtable.Protect is null);
            Assert.False(vtable.Release is null);
            Assert.NotEqual(nint.Zero, vtable.Context);
        }
        finally
        {
            BypassVtable.Abandon(vtable);
        }
    }
}

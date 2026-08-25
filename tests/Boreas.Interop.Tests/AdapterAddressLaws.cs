using System.Net;
using Boreas.Interop.Wintun;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the mask arithmetic, which is wrong quietly rather than loudly.
/// </summary>
public sealed class AdapterAddressLaws
{
    [Theory]
    [InlineData(0, "0.0.0.0")]
    [InlineData(1, "128.0.0.0")]
    [InlineData(8, "255.0.0.0")]
    [InlineData(16, "255.255.0.0")]
    [InlineData(23, "255.255.254.0")]
    [InlineData(24, "255.255.255.0")]
    [InlineData(25, "255.255.255.128")]
    [InlineData(30, "255.255.255.252")]
    [InlineData(31, "255.255.255.254")]
    // The boundary the obvious implementation gets wrong: shifting a 32-bit
    // value by 32 shifts by zero, and /32 comes out as 0.0.0.0.
    [InlineData(32, "255.255.255.255")]
    public void A_prefix_length_becomes_its_mask(int prefixLength, string expected) =>
        Assert.Equal(IPAddress.Parse(expected), AdapterAddress.Mask(prefixLength));

    /// <summary>
    /// Every mask is a run of ones followed by a run of zeroes, and its
    /// population count is the prefix length it came from. Stated as a property
    /// so all thirty-three are checked rather than the ten written above.
    /// </summary>
    [Fact]
    public void Every_mask_is_a_contiguous_run_of_ones_of_the_right_length()
    {
        for (var prefixLength = 0; prefixLength <= 32; prefixLength++)
        {
            var bits = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                AdapterAddress.Mask(prefixLength).GetAddressBytes());

            Assert.Equal(prefixLength, System.Numerics.BitOperations.PopCount(bits));
            // TrailingZeroCount(0) is 32, which is exactly what a /0 mask owes.
            Assert.Equal(32 - prefixLength, System.Numerics.BitOperations.TrailingZeroCount(bits));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    [InlineData(128)]
    public void A_prefix_length_no_ipv4_address_can_carry_is_refused(int prefixLength) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdapterAddress.Mask(prefixLength));

    [Fact]
    public void The_family_test_separates_the_two_command_forms()
    {
        Assert.True(AdapterAddress.IsIPv6(IPAddress.Parse("fd00::2")));
        Assert.False(AdapterAddress.IsIPv6(IPAddress.Parse("10.7.0.2")));
    }
}

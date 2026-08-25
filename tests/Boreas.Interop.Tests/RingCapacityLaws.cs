using Boreas.Interop.Wintun;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the one Wintun constraint that fails silently.
/// </summary>
/// <remarks>
/// <c>WintunStartSession</c> returns a null handle for a capacity outside the
/// range or not a power of two, and api/windows.md gives the signature with
/// neither constraint attached. A refined type turns "the session would not
/// start and nothing said why" into a value that could not be built.
/// </remarks>
public sealed class RingCapacityLaws
{
    [Theory]
    [InlineData(RingCapacity.Minimum)]
    [InlineData(RingCapacity.Maximum)]
    [InlineData(0x400000u)]
    [InlineData(0x800000u)]
    public void A_power_of_two_within_the_range_is_accepted(uint bytes) =>
        Assert.Equal(bytes, Assert.NotNull(RingCapacity.TryCreate(bytes)).Bytes);

    [Theory]
    // Powers of two, but outside the range wintun.h states.
    [InlineData(RingCapacity.Minimum / 2)]
    [InlineData(RingCapacity.Maximum * 2)]
    // Inside the range, but not powers of two. A round decimal number of
    // megabytes is the mistake this exists to catch: 4_000_000 looks more
    // deliberate than 0x400000 and is refused by the driver.
    [InlineData(4_000_000u)]
    [InlineData(RingCapacity.Minimum + 1)]
    [InlineData(0x300000u)]
    // Zero is a power of two to nobody, and is what a default-constructed
    // struct would carry if the type had a public constructor.
    [InlineData(0u)]
    public void Anything_else_is_refused(uint bytes) =>
        Assert.Null(RingCapacity.TryCreate(bytes));

    /// <summary>
    /// The default has to satisfy its own rule, which is not automatic: it is
    /// built through the private constructor, bypassing the check.
    /// </summary>
    [Fact]
    public void The_default_satisfies_the_rule() =>
        Assert.Equal(RingCapacity.Default, Assert.NotNull(RingCapacity.TryCreate(RingCapacity.Default.Bytes)));
}

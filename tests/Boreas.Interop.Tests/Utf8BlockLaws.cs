using System.Text;
using Boreas.Interop.Native;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the boundary where a managed string becomes the NUL-terminated
/// UTF-8 Boreas reads.
/// </summary>
/// <remarks>
/// The third type rule in api/windows.md is that strings are UTF-8, not UTF-16,
/// and that there is no <c>Ansi</c> option to get wrong. These laws are what
/// hold that rule, because every string in a configuration passes through here.
/// </remarks>
public sealed unsafe class Utf8BlockLaws
{
    private static string Read(byte* block)
    {
        var length = 0;
        while (block[length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(block, length);
    }

    [Theory]
    [InlineData("1.1.1.1:53")]
    [InlineData("")]
    [InlineData("news.example.com")]
    // Non-ASCII, so a UTF-16 or single-byte encoding produces different bytes.
    [InlineData("münchen.example")]
    [InlineData("例.example")]
    // Above the basic multilingual plane: one rune, two UTF-16 code units.
    [InlineData("\U0001F300.example")]
    public void A_string_round_trips_as_nul_terminated_utf8(string value)
    {
        using var block = new Utf8Block();

        var encoded = block.Add(value);

        Assert.False(encoded is null);
        Assert.Equal(value, Read(encoded));
        Assert.Equal(0, encoded[Encoding.UTF8.GetByteCount(value)]);
    }

    /// <summary>
    /// Null in, null out. A null resolver means "forward queries untouched",
    /// which is a different tunnel from one pointing at the empty string, so
    /// the two cannot collapse into each other here.
    /// </summary>
    [Fact]
    public void Null_encodes_as_null_and_empty_does_not()
    {
        using var block = new Utf8Block();

        Assert.True(block.Add(null) is null);

        var empty = block.Add(string.Empty);
        Assert.False(empty is null);
        Assert.Equal(0, empty[0]);
    }

    /// <summary>
    /// An empty sequence is a null array with a zero count. A zero-length
    /// allocation would be a pointer Boreas is entitled to read nothing from,
    /// distinguished from null only by luck.
    /// </summary>
    [Fact]
    public void An_empty_sequence_is_a_null_array()
    {
        using var block = new Utf8Block();

        var items = block.AddArray([], out var count);

        Assert.True(items is null);
        Assert.Equal(0u, (uint)count);
    }

    [Fact]
    public void An_array_keeps_its_order_and_its_count()
    {
        using var block = new Utf8Block();
        string[] hosts = ["news.example.com", "shop.example.com", "例.example"];

        var items = block.AddArray(hosts, out var count);

        Assert.Equal((uint)hosts.Length, (uint)count);

        for (var index = 0; index < hosts.Length; index++)
        {
            Assert.Equal(hosts[index], Read(items[index]));
        }
    }

    /// <summary>
    /// Disposing twice is a no-op rather than a double free, so a
    /// <c>using</c> around a path that also disposes explicitly stays correct.
    /// </summary>
    [Fact]
    public void Disposing_twice_frees_once()
    {
        var block = new Utf8Block();
        _ = block.Add("1.1.1.1:53");

        block.Dispose();
        block.Dispose();
    }
}

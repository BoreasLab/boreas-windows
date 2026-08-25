using System.Collections.Immutable;
using Boreas.Interop.Authority;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the frame that makes half an authority unwritable.
/// </summary>
/// <remarks>
/// api/lifecycle.md names two ways restoring fails and says the second is the
/// one worth understanding: two halves that both parse but are not halves of
/// one authority. Nothing downstream detects it - every parse succeeds and the
/// session mints leaves the installed root cannot vouch for. One frame, one
/// file, one rename is how that stops being reachable.
/// </remarks>
public sealed class AuthorityBlobLaws
{
    private static AuthorityMaterial Material(int certificateLength = 300, int keysLength = 121) => new(
        [.. Enumerable.Range(0, certificateLength).Select(static i => (byte)i)],
        [.. Enumerable.Range(0, keysLength).Select(static i => (byte)(255 - i))]);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(300, 121)]
    [InlineData(4096, 2048)]
    public void A_material_round_trips_through_the_frame(int certificateLength, int keysLength)
    {
        var original = Material(certificateLength, keysLength);

        var read = Present.Value(AuthorityBlob.TryDecode(AuthorityBlob.Encode(original)));

        Assert.Equal(original.RootCertificate, read.RootCertificate);
        Assert.Equal(original.Keys, read.Keys);
        Assert.Equal(original, read);
    }

    /// <summary>
    /// Every prefix of a valid blob has to decode as nothing. This is the
    /// torn-write case, and it is the one the frame exists for: a prefix that
    /// decoded as <i>different</i> material would be the failure with no
    /// symptom a user could act on.
    /// </summary>
    [Fact]
    public void Every_prefix_of_a_blob_decodes_as_nothing()
    {
        var blob = AuthorityBlob.Encode(Material());

        for (var length = 0; length < blob.Length; length++)
        {
            Assert.Null(AuthorityBlob.TryDecode(blob.AsSpan(0, length)));
        }

        Assert.NotNull(AuthorityBlob.TryDecode(blob));
    }

    /// <summary>Trailing bytes are not a longer authority.</summary>
    [Fact]
    public void A_blob_with_anything_appended_decodes_as_nothing()
    {
        var blob = AuthorityBlob.Encode(Material());

        Assert.Null(AuthorityBlob.TryDecode([.. blob, 0]));
        Assert.Null(AuthorityBlob.TryDecode([.. blob, .. blob]));
    }

    [Fact]
    public void A_foreign_file_decodes_as_nothing()
    {
        Assert.Null(AuthorityBlob.TryDecode([]));
        Assert.Null(AuthorityBlob.TryDecode("not a boreas authority at all"u8));
        Assert.Null(AuthorityBlob.TryDecode(new byte[64]));
    }

    /// <summary>
    /// A version this build predates is storage it cannot read, which means the
    /// same thing as storage that lost it: generate afresh.
    /// </summary>
    [Fact]
    public void A_version_this_build_predates_decodes_as_nothing()
    {
        var blob = AuthorityBlob.Encode(Material());
        blob[4]++;

        Assert.Null(AuthorityBlob.TryDecode(blob));
    }

    /// <summary>
    /// A zero-length half is not an authority. Handing one back would be the
    /// exactly-one-supplied combination that BOREAS_CONFIG names, arriving from
    /// storage rather than from a caller.
    /// </summary>
    [Theory]
    [InlineData(0, 121)]
    [InlineData(300, 0)]
    [InlineData(0, 0)]
    public void A_frame_with_an_empty_half_decodes_as_nothing(int certificateLength, int keysLength)
    {
        var blob = AuthorityBlob.Encode(Material(Math.Max(certificateLength, 1), Math.Max(keysLength, 1)));

        // Rewrite the lengths to the empty ones and trim to match, so the frame
        // is internally consistent and rejected on its content rather than on
        // its size.
        var forged = AuthorityBlob.Encode(
            new AuthorityMaterial(
                [.. new byte[certificateLength]],
                [.. new byte[keysLength]]));

        Assert.NotNull(AuthorityBlob.TryDecode(blob));
        Assert.Null(AuthorityBlob.TryDecode(forged));
    }

    /// <summary>
    /// Lengths that claim more than the blob holds are refused rather than
    /// read past. The addition is widened first, so a pair that sums past
    /// uint cannot wrap into a total the blob happens to match.
    /// </summary>
    [Theory]
    [InlineData(5, uint.MaxValue)]
    [InlineData(9, uint.MaxValue)]
    [InlineData(5, 0xFFFF_0000u)]
    public void A_length_larger_than_the_blob_is_refused(int offset, uint claimed)
    {
        var blob = AuthorityBlob.Encode(Material());
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), claimed);

        Assert.Null(AuthorityBlob.TryDecode(blob));
    }

    /// <summary>
    /// The two halves are distinguishable after the round trip. A frame that
    /// swapped or overlapped them would still decode.
    /// </summary>
    [Fact]
    public void The_two_halves_do_not_bleed_into_each_other()
    {
        var original = new AuthorityMaterial(
            [.. Enumerable.Repeat((byte)0xAA, 64)],
            [.. Enumerable.Repeat((byte)0xBB, 32)]);

        var read = Present.Value(AuthorityBlob.TryDecode(AuthorityBlob.Encode(original)));

        Assert.All(read.RootCertificate, b => Assert.Equal(0xAA, b));
        Assert.All(read.Keys, b => Assert.Equal(0xBB, b));
        Assert.Equal(64, read.RootCertificate.Length);
        Assert.Equal(32, read.Keys.Length);
    }
}

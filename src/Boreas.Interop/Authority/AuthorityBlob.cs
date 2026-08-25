using System.Buffers.Binary;
using System.Collections.Immutable;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Authority;

/// <summary>
/// The certificate authority's two halves, framed as one byte string.
/// </summary>
/// <remarks>
/// <para>
/// <b>One blob rather than two files, and that is the whole design.</b>
/// api/lifecycle.md names two ways restoring an authority fails, and says the
/// second is the one worth understanding: the halves "live in different stores,
/// so one can be written without the other - an interrupted rotation, a
/// restored backup, two slots keyed differently". Nothing downstream can detect
/// it. Every parse succeeds, the session starts, and it mints leaves the
/// installed root cannot vouch for, so the user sees a certificate error on
/// every site and has nothing to act on.
/// </para>
/// <para>
/// Two halves in one frame, written to one path by one rename, cannot be half
/// written. The failure that needed understanding becomes a failure that has no
/// spelling.
/// </para>
/// <para>
/// The material is opaque and self-describing; nothing here looks inside
/// either half. The frame carries lengths because the key half has no
/// terminator and the certificate's DER length is not something this layer
/// should have to parse to find.
/// </para>
/// </remarks>
internal static class AuthorityBlob
{
    /// <summary>Four bytes that make a foreign file obviously foreign.</summary>
    private static ReadOnlySpan<byte> Magic => "BORA"u8;

    private const byte Version = 1;

    /// <summary>magic, version, then two little-endian lengths.</summary>
    private const int HeaderLength = 4 + 1 + 4 + 4;

    public static byte[] Encode(AuthorityMaterial material)
    {
        var blob = new byte[HeaderLength + material.RootCertificate.Length + material.Keys.Length];
        var span = blob.AsSpan();

        Magic.CopyTo(span);
        span[4] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(span[5..], (uint)material.RootCertificate.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[9..], (uint)material.Keys.Length);

        material.RootCertificate.CopyTo(span[HeaderLength..]);
        material.Keys.CopyTo(span[(HeaderLength + material.RootCertificate.Length)..]);

        return blob;
    }

    /// <summary>
    /// Reads a blob, or null for anything that is not exactly one.
    /// </summary>
    /// <remarks>
    /// Null covers every way this can be wrong - truncated, foreign, a version
    /// this build predates, lengths that do not add up - because they all mean
    /// the same thing to the caller: storage lost the material, so generate
    /// afresh and ask the user to trust the new root. Boreas will not silently
    /// generate a replacement, because a device whose store still trusts the
    /// old root would then intercept nothing while reporting itself healthy.
    /// </remarks>
    public static AuthorityMaterial? TryDecode(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderLength
            || !blob[..4].SequenceEqual(Magic)
            || blob[4] != Version)
        {
            return null;
        }

        var certificateLength = BinaryPrimitives.ReadUInt32LittleEndian(blob[5..]);
        var keysLength = BinaryPrimitives.ReadUInt32LittleEndian(blob[9..]);

        // Widened before adding, so two lengths that sum past uint cannot wrap
        // into a total the blob happens to match.
        if ((long)HeaderLength + certificateLength + keysLength != blob.Length)
        {
            return null;
        }

        // Both halves or neither: a zero-length half is not an authority, and
        // handing one back would be the BOREAS_CONFIG this file exists to make
        // unwritable.
        if (certificateLength == 0 || keysLength == 0)
        {
            return null;
        }

        var certificate = blob.Slice(HeaderLength, (int)certificateLength);
        var keys = blob.Slice(HeaderLength + (int)certificateLength, (int)keysLength);

        return new AuthorityMaterial([.. certificate], [.. keys]);
    }
}

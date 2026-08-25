using System.Numerics;

namespace Boreas.Interop.Wintun;

/// <summary>
/// A Wintun ring capacity that <c>WintunStartSession</c> will accept.
/// </summary>
/// <remarks>
/// <para>
/// <c>wintun.h</c> requires the capacity to lie between
/// <c>WINTUN_MIN_RING_CAPACITY</c> and <c>WINTUN_MAX_RING_CAPACITY</c>
/// inclusive <b>and to be a power of two</b>. A value that is not returns a
/// null session handle with no explanation attached, and the API listing in
/// api/windows.md gives the signature without either constraint, so the
/// obvious mistake - a round decimal number of megabytes - looks like Wintun
/// failing to start.
/// </para>
/// <para>
/// A refined type rather than a check at the call site: the constraint is
/// checked once, where the value is made, and every session afterwards holds a
/// number that could not have been built without passing it.
/// </para>
/// </remarks>
public readonly record struct RingCapacity
{
    /// <summary>From <c>WINTUN_MIN_RING_CAPACITY</c>, 128 KiB.</summary>
    public const uint Minimum = 0x20000;

    /// <summary>From <c>WINTUN_MAX_RING_CAPACITY</c>, 64 MiB.</summary>
    public const uint Maximum = 0x4000000;

    public static readonly string Requirement =
        $"The ring capacity must be a power of two between {Minimum} and {Maximum} bytes.";

    private RingCapacity(uint bytes) => Bytes = bytes;

    public uint Bytes { get; }

    /// <summary>
    /// Four mebibytes, which is what WireGuard's own Windows client uses.
    /// </summary>
    /// <remarks>
    /// The ring is a fixed reservation, not a high-water mark, so this is memory
    /// the tunnel holds for its whole life. It buys headroom for a burst that
    /// arrives while the reader is between calls; a desktop can afford it and
    /// the alternative is a counted drop at the driver, which nothing on this
    /// side of the boundary can see.
    /// </remarks>
    public static RingCapacity Default { get; } = new(0x400000);

    public static RingCapacity? TryCreate(uint bytes) =>
        bytes is >= Minimum and <= Maximum && BitOperations.IsPow2(bytes)
            ? new RingCapacity(bytes)
            : null;

    public override string ToString() => Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

namespace Boreas.Interop.Device;

/// <summary>
/// The negative values a device callback returns.
/// </summary>
/// <remarks>
/// The ABI says "a negative errno" and names no encoding, so these are the
/// ordinary Linux numbers the core's own error type is written against. They
/// are advisory to Boreas - a failing <c>recv</c> or <c>send</c> fails the
/// packet, not the tunnel - which is why an oversized packet is dropped here
/// rather than reported as one of these.
/// </remarks>
internal static class Errno
{
    /// <summary>Anything that went wrong and has no better name.</summary>
    public const nint Io = 5;

    /// <summary>The send ring is full. The packet did not go.</summary>
    public const nint NoBufferSpace = 105;
}

/// <summary>
/// One IP packet in, one IP packet out, and a way to release a blocked read.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the callback obligations can be proved without Windows. The
/// three rules in api/windows.md that actually cost an afternoon - never block
/// past the timeout, return zero rather than parking, never let an exception
/// escape - are properties of the <b>translation</b> from a packet source to
/// the C vtable, not of Wintun. Splitting them apart lets
/// <c>TunDeviceLaws</c> drive the real function pointers against a source it
/// controls, on whatever machine builds the repository, and leaves only the
/// Wintun ring itself waiting for a device.
/// </para>
/// <para>
/// The shape is the vtable's, deliberately. <see cref="Receive"/> returns a
/// count, zero, or a negative errno because that is what <c>recv</c> returns,
/// and a nicer managed signature here would only move the translation
/// somewhere it could not be tested.
/// </para>
/// </remarks>
public interface IPacketRing : IDisposable
{
    /// <summary>The MTU the interface is configured with. At least 1280.</summary>
    ushort Mtu { get; }

    /// <summary>
    /// Reads one IP packet, waiting no longer than <paramref name="timeout"/>.
    /// </summary>
    /// <returns>
    /// The byte count, <b>zero for "nothing yet, ask again"</b>, or a negative
    /// errno.
    /// </returns>
    /// <remarks>
    /// Zero on expiry is not a fallback, it is the design. An
    /// <c>[UnmanagedCallersOnly]</c> method runs in the CLR's cooperative GC
    /// mode, and a thread parked there prevents any garbage collection from
    /// completing <b>process-wide</b>. Returning zero puts the wait back in
    /// native code, in preemptive mode, where a collection can proceed.
    /// </remarks>
    int Receive(Span<byte> destination, TimeSpan timeout);

    /// <summary>
    /// Writes one IP packet, whole.
    /// </summary>
    /// <returns>Zero, or a negative errno.</returns>
    /// <remarks>
    /// All-or-nothing, because the unit is the packet: there is no correct
    /// handling of half an IP packet, since the remainder carries no header.
    /// </remarks>
    int Send(ReadOnlySpan<byte> packet);

    /// <summary>
    /// Makes an in-flight <see cref="Receive"/> return promptly.
    /// </summary>
    /// <remarks>
    /// Must be safe to call concurrently with a <see cref="Receive"/> that is
    /// already running, and safe to call more than once.
    /// </remarks>
    void Wake();
}

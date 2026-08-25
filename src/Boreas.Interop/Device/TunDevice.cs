using System.Runtime.InteropServices;
using Boreas.Interop.Native;

namespace Boreas.Interop.Device;

/// <summary>
/// The <see cref="BoreasDevice"/> vtable over an <see cref="IPacketRing"/>.
/// </summary>
/// <remarks>
/// <para>
/// The vtable <b>is</b> the object: the ring travels as the <c>void*</c>
/// context and <see cref="Release"/> is its only destructor. Boreas calls
/// release exactly once per vtable, on the success path and on every failure
/// path including a configuration it refuses before building anything, so a
/// context handed over is always accounted for and there is no path where the
/// ring outlives the tunnel or is freed twice.
/// </para>
/// <para>
/// That ownership is also what makes the required teardown order structural.
/// obligations.md says the Wintun session may only end after shutdown, and
/// release runs after every other callback has returned - including a
/// <c>recv</c> that was still in flight, which is refcounted rather than merely
/// sequenced because a blocking read cannot be cancelled. Putting the ring's
/// disposal inside release means the ordering cannot be got wrong by
/// forgetting it somewhere else.
/// </para>
/// <para>
/// Static <c>[UnmanagedCallersOnly]</c> methods and <c>&amp;Method</c>, never
/// <c>Marshal.GetFunctionPointerForDelegate</c>: the delegate route obliges the
/// caller to root the delegate for as long as native code might call it, and a
/// collected delegate is a call through freed memory. There is no heap object
/// here to collect.
/// </para>
/// </remarks>
internal static unsafe class TunDevice
{
    /// <summary>
    /// How long a <c>recv</c> may wait before returning "nothing yet".
    /// </summary>
    /// <remarks>
    /// api/windows.md's ceiling is "no callback blocks for more than ~100 ms",
    /// and the cost of being at the ceiling rather than under it is one extra
    /// managed transition per idle interval. A tunnel carrying traffic never
    /// reaches the wait at all, because the ring has a packet.
    /// </remarks>
    public static readonly TimeSpan ReceiveTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Builds the vtable and hands the ring over to it.
    /// </summary>
    /// <remarks>
    /// <b>The caller owns the result only until <c>boreas_tunnel_start</c>.</b>
    /// After that call - whatever it returns - Boreas has called release and
    /// the ring is disposed. Build this immediately before the call;
    /// <see cref="Abandon"/> is for the narrow window where something throws in
    /// between.
    /// </remarks>
    public static BoreasDevice For(IPacketRing ring) => new()
    {
        Context = GCHandle.ToIntPtr(GCHandle.Alloc(ring)),
        Recv = &Recv,
        Send = &Send,
        Close = &Close,
        Release = &Release,
        Mtu = ring.Mtu,
    };

    /// <summary>
    /// Runs the release a failed handover would otherwise have skipped.
    /// </summary>
    /// <remarks>
    /// Only for a vtable that was never passed to <c>boreas_tunnel_start</c>.
    /// Calling it on one that was is a double free, which is why nothing but
    /// the start path may call it.
    /// </remarks>
    public static void Abandon(BoreasDevice device) => ReleaseContext(device.Context);

    /// <summary>
    /// A native capacity as a length a span can hold.
    /// </summary>
    /// <remarks>
    /// Boreas sizes its slices from the MTU, so this never truncates in
    /// practice. It is here because the alternative to clamping a
    /// <c>size_t</c> is an unchecked cast that turns a large capacity into a
    /// negative length and throws inside a callback.
    /// </remarks>
    private static int Clamp(nuint capacity) => (int)Math.Min(capacity, (nuint)int.MaxValue);

    [UnmanagedCallersOnly]
    private static nint Recv(nint context, byte* buffer, nuint capacity)
    {
        try
        {
            var ring = (IPacketRing)GCHandle.FromIntPtr(context).Target!;
            return ring.Receive(new Span<byte>(buffer, Clamp(capacity)), ReceiveTimeout);
        }
        catch
        {
            // An unhandled managed exception crossing back into native code
            // crashes the host process. There is no caller to report to and no
            // channel to log on, so the contract's own failure value is the
            // whole of the handling available.
            return -Errno.Io;
        }
    }

    [UnmanagedCallersOnly]
    private static nint Send(nint context, byte* buffer, nuint length)
    {
        try
        {
            var ring = (IPacketRing)GCHandle.FromIntPtr(context).Target!;
            return ring.Send(new ReadOnlySpan<byte>(buffer, Clamp(length)));
        }
        catch
        {
            return -Errno.Io;
        }
    }

    /// <summary>
    /// Called before release, and possibly while a <c>recv</c> is blocked.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void Close(nint context)
    {
        try
        {
            ((IPacketRing)GCHandle.FromIntPtr(context).Target!).Wake();
        }
        catch
        {
            // Nothing to return and nothing to retry. A wake that failed leaves
            // recv to time out on its own, which is the slower path to the same
            // place.
        }
    }

    [UnmanagedCallersOnly]
    private static void Release(nint context) => ReleaseContext(context);

    /// <summary>
    /// The body, reachable from managed code.
    /// </summary>
    /// <remarks>
    /// An <c>[UnmanagedCallersOnly]</c> method cannot be called by name,
    /// and <see cref="Abandon"/> has to be able to. Splitting the body out
    /// is cheaper than routing a managed caller back through the function
    /// pointer, and it keeps the one destructor written once.
    /// </remarks>
    private static void ReleaseContext(nint context)
    {
        if (context == nint.Zero)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(context);

        try
        {
            (handle.Target as IDisposable)?.Dispose();
        }
        catch
        {
            // Swallowed for the same reason as above, and the free below still
            // runs: a ring that failed to close its session must not also leak
            // the handle pinning it.
        }
        finally
        {
            handle.Free();
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Boreas.Interop.Device;
using Microsoft.Win32.SafeHandles;

namespace Boreas.Interop.Wintun;

/// <summary>
/// An <see cref="IPacketRing"/> over a Wintun session.
/// </summary>
/// <remarks>
/// <para>
/// <b>The wait is on two handles, never one.</b> <c>WintunEndSession</c>'s
/// entire documented contract is "Ends Wintun session"; it says nothing about
/// signalling the read-wait event, and <c>WintunGetReadWaitEvent</c> promises
/// only that the event is signalled when data becomes available. Waiting on
/// the read-wait event alone would therefore be waiting for a packet that is
/// never coming, on an adapter that is going away. Wintun's own example waits
/// on the read-wait event and a quit event of its own, and this does the same.
/// </para>
/// <para>
/// <b>And the wait is bounded.</b> Even with the quit event, an infinite wait
/// inside a callback parks a thread in cooperative GC mode and stops collection
/// process-wide. The timeout is what puts the wait back in native code between
/// calls.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WintunRing : IPacketRing
{
    private const int ReadWaitIndex = 0;
    private const int QuitIndex = 1;

    private readonly WintunAdapterHandle _adapter;
    private readonly WintunSessionHandle _session;
    private readonly ManualResetEvent _quit = new(false);
    private readonly WaitHandle[] _waits;
    private long _oversizedDropped;
    private long _packetsIn;
    private long _packetsOut;
    private long _bytesIn;
    private long _bytesOut;

    private WintunRing(WintunAdapterHandle adapter, WintunSessionHandle session, ushort mtu)
    {
        _adapter = adapter;
        _session = session;
        Mtu = mtu;

        // Not owned: wintun.h says the session manages this handle and that
        // CloseHandle must not be called on it. A SafeWaitHandle that owned it
        // would close it when this object is collected, after the session that
        // owns it has already gone.
        //
        // The wrapper's type is cosmetic - whether the event auto-resets is a
        // property of the kernel object Wintun created, not of the class chosen
        // here - but the event it allocates on construction is real, so it is
        // closed rather than left to a finalizer.
        var readWait = new AutoResetEvent(false);
        var discarded = readWait.SafeWaitHandle;
        readWait.SafeWaitHandle = new SafeWaitHandle(Wintun.WintunGetReadWaitEvent(session), ownsHandle: false);
        discarded.Dispose();

        _waits = [readWait, _quit];
    }

    public ushort Mtu { get; }

    /// <summary>
    /// Packets discarded for being wider than the buffer Boreas offered.
    /// </summary>
    /// <remarks>
    /// This should stay at zero: Boreas sizes its slices from the same MTU the
    /// interface is configured with. A rising value means the interface MTU was
    /// not applied, which is the same misconfiguration
    /// <c>paths_reported</c> reports from the other side. Counted rather than
    /// silent, because a dropped packet with no record is the kind of fault
    /// that is diagnosed by guesswork.
    /// </remarks>
    public long OversizedDropped => Interlocked.Read(ref _oversizedDropped);

    /// <summary>Packets read from the adapter since the session started.</summary>
    /// <remarks>
    /// <para>
    /// The ABI reports no traffic counters: every field of <c>BoreasCounters</c>
    /// is a thing that went wrong or was refused, which is the right surface
    /// for a core and the wrong one for a status page that has to show a tunnel
    /// is doing something. The ring is where every packet actually crosses, so
    /// it is the one place these can be counted without inventing a second
    /// datapath to count them on.
    /// </para>
    /// <para>
    /// Interlocked rather than plain increments. The ABI serialises recv with
    /// itself and send with itself, but not to a fixed thread, so a plain
    /// increment would rely on the core's internal synchronisation publishing
    /// the write - which the ABI never promises. Two interlocked operations per
    /// packet per direction, against a packet rate that is bounded by the link:
    /// at a gigabit and 1420-byte packets that is under a hundred thousand a
    /// second, and each one is a handful of nanoseconds. Unmeasured, and stated
    /// as reasoning rather than as a benchmark.
    /// </para>
    /// </remarks>
    public long PacketsIn => Interlocked.Read(ref _packetsIn);

    /// <summary>Packets written to the adapter since the session started.</summary>
    public long PacketsOut => Interlocked.Read(ref _packetsOut);

    /// <summary>Bytes read from the adapter since the session started.</summary>
    public long BytesIn => Interlocked.Read(ref _bytesIn);

    /// <summary>Bytes written to the adapter since the session started.</summary>
    public long BytesOut => Interlocked.Read(ref _bytesOut);

    /// <summary>
    /// Creates the adapter and starts a session on it.
    /// </summary>
    /// <remarks>
    /// The caller hands the result straight to
    /// <c>TunDevice.For</c>; from then on the vtable owns it and its release
    /// callback is what disposes it.
    /// </remarks>
    public static WintunRing Create(string adapterName, string tunnelType, ushort mtu, RingCapacity capacity)
    {
        var adapter = new WintunAdapterHandle(Wintun.WintunCreateAdapter(adapterName, tunnelType, nint.Zero));

        if (adapter.IsInvalid)
        {
            adapter.Dispose();
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), $"Wintun could not create the adapter '{adapterName}'.");
        }

        var session = new WintunSessionHandle(Wintun.WintunStartSession(adapter, capacity.Bytes));

        if (session.IsInvalid)
        {
            // Ordered: the session never started, so the adapter is all there is
            // to give back, and giving it back is what lets a retry succeed.
            session.Dispose();
            adapter.Dispose();
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), $"Wintun could not start a session of {capacity} bytes.");
        }

        return new WintunRing(adapter, session, mtu);
    }

    public unsafe int Receive(Span<byte> destination, TimeSpan timeout)
    {
        var packet = Wintun.WintunReceivePacket(_session, out var size);

        if (packet != nint.Zero)
        {
            try
            {
                if (size > (uint)destination.Length)
                {
                    // Truncating would hand Boreas a header with no body, which
                    // is not a smaller packet but a corrupt one. Dropping costs
                    // this packet; "ask again" costs nothing and is counted.
                    Interlocked.Increment(ref _oversizedDropped);
                    return 0;
                }

                new ReadOnlySpan<byte>((void*)packet, (int)size).CopyTo(destination);

                Interlocked.Increment(ref _packetsIn);
                Interlocked.Add(ref _bytesIn, size);

                return (int)size;
            }
            finally
            {
                Wintun.WintunReleaseReceivePacket(_session, packet);
            }
        }

        var error = Marshal.GetLastWin32Error();

        if (error != Wintun.ErrorNoMoreItems)
        {
            // ERROR_HANDLE_EOF is the adapter terminating and anything else is a
            // corrupt ring. Neither is worth distinguishing to Boreas, which
            // fails the packet either way, but they are worth not confusing
            // with an empty ring - which is what makes SetLastError load-bearing.
            return -(int)Errno.Io;
        }

        return WaitHandle.WaitAny(_waits, timeout) == QuitIndex
            ? -(int)Errno.Io
            : 0;
    }

    public unsafe int Send(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is 0 or > Wintun.MaxIpPacketSize)
        {
            // There is no zero-length IP packet, and WintunAllocateSendPacket
            // requires an exact size no larger than WINTUN_MAX_IP_PACKET_SIZE.
            return -(int)Errno.Io;
        }

        var buffer = Wintun.WintunAllocateSendPacket(_session, (uint)packet.Length);

        if (buffer == nint.Zero)
        {
            return Marshal.GetLastWin32Error() == Wintun.ErrorBufferOverflow
                ? -(int)Errno.NoBufferSpace
                : -(int)Errno.Io;
        }

        packet.CopyTo(new Span<byte>((void*)buffer, packet.Length));
        Wintun.WintunSendPacket(_session, buffer);

        Interlocked.Increment(ref _packetsOut);
        Interlocked.Add(ref _bytesOut, packet.Length);

        return 0;
    }

    /// <summary>
    /// Signals the quit event, releasing a <see cref="Receive"/> that is
    /// blocked and every one after it.
    /// </summary>
    /// <remarks>
    /// Safe to call while a receive is running, which is the case it exists
    /// for, and safe to call more than once. The event stays set on purpose:
    /// once the device is closing, a later receive must not go back to waiting.
    /// </remarks>
    public void Wake() => _quit.Set();

    /// <summary>
    /// Ends the session, then closes the adapter, in that order.
    /// </summary>
    /// <remarks>
    /// Reached only through the vtable's release callback, which Boreas calls
    /// after every other callback has returned - including a receive that was
    /// still in flight when the tunnel stopped. That is what makes "end the
    /// Wintun session after shutdown, not before" a property of where this is
    /// called from rather than a step someone has to remember.
    /// </remarks>
    public void Dispose()
    {
        _quit.Set();
        _session.Dispose();
        _adapter.Dispose();
        _quit.Dispose();
    }
}

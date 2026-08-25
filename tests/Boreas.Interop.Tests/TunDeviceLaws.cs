using System.Diagnostics;
using Boreas.Interop.Device;

namespace Boreas.Interop.Tests;

/// <summary>
/// The device vtable's obligations, driven through the real function pointers.
/// </summary>
/// <remarks>
/// <para>
/// Every call below goes through <c>device.Recv</c> and friends rather than
/// through the ring, which is the whole point: an
/// <c>[UnmanagedCallersOnly]</c> method may not be called by name from managed
/// code, but calling it through the function pointer is exactly what Boreas
/// does, so these laws exercise the same path in the same calling convention.
/// </para>
/// <para>
/// What this cannot reach is Wintun. That split is deliberate and it is where
/// the value is: the three rules that cost an afternoon live in the
/// translation, and the translation is what runs here.
/// </para>
/// </remarks>
public sealed unsafe class TunDeviceLaws
{
    /// <summary>A ring whose every behaviour is dictated by the test.</summary>
    private sealed class FakeRing : IPacketRing
    {
        private readonly ManualResetEventSlim _woken = new(false);

        public ushort Mtu { get; init; } = 1420;

        public byte[]? Packet { get; set; }

        public bool Throws { get; set; }

        public int Disposals { get; private set; }

        public int Wakes { get; private set; }

        public List<byte[]> Sent { get; } = [];

        public int LastCapacityOffered { get; private set; } = -1;

        public int Receive(Span<byte> destination, TimeSpan timeout)
        {
            LastCapacityOffered = destination.Length;

            if (Throws)
            {
                throw new InvalidOperationException("the ring failed");
            }

            if (Packet is { } packet)
            {
                packet.CopyTo(destination);
                return packet.Length;
            }

            // Exactly what the Wintun ring does when it has nothing: wait, then
            // report "nothing yet" rather than staying in the callback.
            _woken.Wait(timeout);
            return 0;
        }

        public int Send(ReadOnlySpan<byte> packet)
        {
            if (Throws)
            {
                throw new InvalidOperationException("the ring failed");
            }

            Sent.Add(packet.ToArray());
            return 0;
        }

        public void Wake()
        {
            Wakes++;
            _woken.Set();
        }

        public void Dispose() => Disposals++;
    }

    private static nint Recv(Boreas.Interop.Native.BoreasDevice device, Span<byte> buffer)
    {
        fixed (byte* destination = buffer)
        {
            return device.Recv(device.Context, destination, (nuint)buffer.Length);
        }
    }

    /// <summary>
    /// The ceiling api/windows.md sets is "no callback blocks for more than
    /// ~100 ms", because a thread parked in cooperative GC mode stops
    /// collection process-wide. A later edit that raised this would be a
    /// process-wide pause nothing in a stack trace would point at.
    /// </summary>
    [Fact]
    public void The_receive_timeout_stays_under_the_documented_ceiling() =>
        Assert.True(
            TunDevice.ReceiveTimeout <= TimeSpan.FromMilliseconds(100),
            $"recv may block for {TunDevice.ReceiveTimeout.TotalMilliseconds} ms");

    /// <summary>
    /// An idle ring must produce zero - "nothing yet, ask again" - and must
    /// produce it in about the timeout, not eventually.
    /// </summary>
    [Fact]
    public void An_idle_receive_returns_zero_within_the_timeout()
    {
        var ring = new FakeRing();
        var device = TunDevice.For(ring);

        try
        {
            var buffer = new byte[2048];
            var clock = Stopwatch.StartNew();
            var result = Recv(device, buffer);
            clock.Stop();

            Assert.Equal(0, (int)result);
            Assert.True(
                clock.Elapsed < TunDevice.ReceiveTimeout * 10,
                $"an idle recv took {clock.ElapsedMilliseconds} ms");
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    /// <summary>
    /// Close is what releases a blocked read, and it is called <b>while</b> the
    /// read is running. A close that only set a flag the next call would see
    /// would still be correct here, and that is fine: what must not happen is
    /// the two waiting for each other.
    /// </summary>
    [Fact]
    public void Close_releases_a_receive_that_is_already_blocked()
    {
        var ring = new FakeRing();
        var device = TunDevice.For(ring);

        try
        {
            var entered = new ManualResetEventSlim(false);
            nint result = -1;

            var reader = new Thread(() =>
            {
                var buffer = new byte[2048];
                entered.Set();
                result = Recv(device, buffer);
            });

            reader.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            device.Close(device.Context);

            Assert.True(reader.Join(TimeSpan.FromSeconds(5)), "recv did not return after close");
            Assert.Equal(0, (int)result);
            Assert.Equal(1, ring.Wakes);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    /// <summary>
    /// An unhandled managed exception crossing back into native code crashes
    /// the host process, so every body is wrapped and every failure becomes the
    /// contract's own negative value.
    /// </summary>
    [Fact]
    public void An_exception_never_escapes_a_callback()
    {
        var ring = new FakeRing { Throws = true };
        var device = TunDevice.For(ring);

        try
        {
            var buffer = new byte[2048];

            Assert.Equal(-Errno.Io, Recv(device, buffer));

            fixed (byte* packet = buffer)
            {
                Assert.Equal(-Errno.Io, device.Send(device.Context, packet, (nuint)buffer.Length));
            }

            // Close has no return value at all, so "did not throw" is the whole
            // of what it can promise.
            device.Close(device.Context);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    [Fact]
    public void A_received_packet_arrives_whole_and_reports_its_length()
    {
        byte[] packet = [0x45, 0x00, 0x00, 0x28, 0xde, 0xad, 0xbe, 0xef];
        var ring = new FakeRing { Packet = packet };
        var device = TunDevice.For(ring);

        try
        {
            var buffer = new byte[2048];

            Assert.Equal(packet.Length, (int)Recv(device, buffer));
            Assert.Equal(packet, buffer[..packet.Length]);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    /// <summary>
    /// The capacity crosses as a <c>size_t</c> and arrives as a span length. A
    /// cast rather than a clamp would turn a large capacity into a negative
    /// length and throw inside the callback, which is the one place an
    /// exception is most expensive.
    /// </summary>
    [Fact]
    public void A_capacity_wider_than_a_span_is_clamped_rather_than_wrapped()
    {
        var ring = new FakeRing();
        var device = TunDevice.For(ring);

        try
        {
            // Never dereferenced: the fake reads only the length it is offered.
            var result = device.Recv(device.Context, null, nuint.MaxValue);

            Assert.Equal(0, (int)result);
            Assert.Equal(int.MaxValue, ring.LastCapacityOffered);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    [Fact]
    public void A_sent_packet_arrives_whole()
    {
        byte[] packet = [0x60, 0x00, 0x00, 0x00, 0x00, 0x08, 0x11, 0x40];
        var ring = new FakeRing();
        var device = TunDevice.For(ring);

        try
        {
            fixed (byte* buffer = packet)
            {
                Assert.Equal(0, (int)device.Send(device.Context, buffer, (nuint)packet.Length));
            }

            Assert.Equal(packet, Assert.Single(ring.Sent));
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    /// <summary>
    /// Release is the ring's only destructor, and Boreas calls it exactly once
    /// per vtable on every path. Disposing anywhere else as well would end the
    /// Wintun session twice, or end it before shutdown.
    /// </summary>
    [Fact]
    public void Release_disposes_the_ring_exactly_once()
    {
        var ring = new FakeRing();
        var device = TunDevice.For(ring);

        Assert.Equal(0, ring.Disposals);

        device.Release(device.Context);

        Assert.Equal(1, ring.Disposals);
    }

    /// <summary>
    /// A ring that throws on disposal must still have its handle freed: a
    /// session that failed to close must not also leak the handle pinning it.
    /// </summary>
    [Fact]
    public void A_ring_that_fails_to_dispose_still_releases_its_handle()
    {
        var device = TunDevice.For(new ThrowingRing());

        // The absence of an escaping exception is the assertion. A leaked
        // GCHandle is not observable from here, which is why the finally in
        // Release is the thing under test rather than something to detect.
        device.Release(device.Context);
    }

    private sealed class ThrowingRing : IPacketRing
    {
        public ushort Mtu => 1420;

        public int Receive(Span<byte> destination, TimeSpan timeout) => 0;

        public int Send(ReadOnlySpan<byte> packet) => 0;

        public void Wake() { }

        public void Dispose() => throw new InvalidOperationException("the session refused to close");
    }

    /// <summary>The vtable reports the ring's MTU, which start refuses below 1280.</summary>
    [Fact]
    public void The_vtable_carries_the_rings_mtu()
    {
        var device = TunDevice.For(new FakeRing { Mtu = 1400 });

        try
        {
            Assert.Equal(1400, device.Mtu);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }

    /// <summary>
    /// Close may be NULL only if recv never blocks indefinitely. This one does
    /// block, briefly, so all four callbacks are present.
    /// </summary>
    [Fact]
    public void Every_callback_is_present()
    {
        var device = TunDevice.For(new FakeRing());

        try
        {
            Assert.False(device.Recv is null);
            Assert.False(device.Send is null);
            Assert.False(device.Close is null);
            Assert.False(device.Release is null);
            Assert.NotEqual(nint.Zero, device.Context);
        }
        finally
        {
            TunDevice.Abandon(device);
        }
    }
}

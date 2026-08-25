using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Boreas.Interop.Bypass;
using Boreas.Interop.Device;
using Boreas.Interop.Native;

namespace Boreas.Interop.Tunnel;

/// <summary>The certificate authority's two halves, as read back from a tunnel.</summary>
/// <remarks>
/// The certificate is public and goes to the trust installer; the keys are
/// secret and go under DPAPI. They are opaque and self-describing, and nothing
/// here looks inside either.
/// </remarks>
public sealed record AuthorityMaterial(ImmutableArray<byte> RootCertificate, ImmutableArray<byte> Keys);

/// <summary>
/// A running tunnel: its handle, its reader, and the ordering its teardown owes.
/// </summary>
/// <remarks>
/// <para>
/// This is the imperative shell. Everything decidable was decided before it ran
/// - the configuration is a value that could not have been built wrong, and the
/// events it produces become a closed sum on the way out - so what is left here
/// is the part that is genuinely about effects: one handle, one thread, and one
/// order.
/// </para>
/// <para>
/// <b>The order is shutdown, join, free, and it is three calls for a reason.</b>
/// A thread blocked in <c>next_event</c> holds a borrow of the handle, and
/// freeing it from another thread at that moment is a use-after-free that no
/// internal locking can fix, because the reader is <i>inside</i> the call. So
/// shutdown signals, the reader observes BOREAS_STOPPED and returns, it is
/// joined, and only then is the handle unreferenced.
/// </para>
/// </remarks>
public sealed unsafe class NativeTunnel : IDisposable
{
    /// <summary>
    /// The name and rule buffers' capacity.
    /// </summary>
    /// <remarks>
    /// DNS caps a name at 255 bytes, so 256 holds the longest one there is plus
    /// its terminator, and is generous for a rule. Truncation is still reported
    /// rather than assumed away, because a rule has no such cap.
    /// </remarks>
    private const int TextCapacity = 256;

    private readonly BoreasTunnelHandle _handle;
    private readonly Thread _reader;
    private int _stopping;

    private NativeTunnel(BoreasTunnelHandle handle, Action<TunnelEvent> onEvent, Action<BoreasStatus>? onEnded)
    {
        _handle = handle;

        _reader = new Thread(() => ReadEvents(onEvent, onEnded))
        {
            Name = "boreas-events",

            // Foreground, as api/windows.md specifies. A healthy idle tunnel
            // emits nothing for hours, so this thread spends its life blocked;
            // a background thread would be torn down mid-P/Invoke at process
            // exit, which aborts native code at an arbitrary point. The cost is
            // that a process which never calls Stop cannot exit, and the answer
            // to that is that Stop is what Dispose does.
            IsBackground = false,
        };

        _reader.Start();
    }

    /// <summary>
    /// The status the reader ended on, once it has. Null while it is running.
    /// </summary>
    /// <remarks>
    /// BOREAS_STOPPED is the normal way the loop ends and means shutdown was
    /// called. Anything else ended it early and is worth surfacing.
    /// </remarks>
    public BoreasStatus? ReaderEndedWith { get; private set; }

    /// <summary>
    /// Builds and starts everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ABI check runs first, before anything else.</b> A stale library
    /// beside a newer header reads every field at the wrong offset and behaves
    /// inexplicably; there is no later moment at which noticing that is cheap.
    /// </para>
    /// <para>
    /// <b>The ring and the bypass are handed over unconditionally.</b> Whatever
    /// this returns, Boreas has called both release callbacks - on the success
    /// path and on every failure path, including a configuration it refuses
    /// before building anything - so the ring is disposed and a retry needs a
    /// fresh one. The only path that must undo the handover itself is the one
    /// where the call never happened.
    /// </para>
    /// <para>
    /// Blocks for as long as the first connection takes: a DNS lookup, a
    /// handshake. Call it off the UI thread.
    /// </para>
    /// </remarks>
    public static NativeTunnel Start(
        TunnelConfiguration configuration,
        IPacketRing ring,
        PhysicalInterfaceBypass bypass,
        Action<TunnelEvent> onEvent,
        Action<BoreasStatus>? onEnded = null)
    {
        RequireMatchingAbi();

        using var block = new Utf8Block();

        // Built before the vtables so that a failure here - which can only be a
        // defect, since the configuration is already a validated value - cannot
        // strand a context nobody will release.
        var config = NativeConfig.Lower(configuration, block);

        var device = TunDevice.For(ring);
        var bypassTable = BypassVtable.For(bypass);

        BoreasStatus status;
        BoreasTunnelHandle handle;

        try
        {
            status = Boreas.Interop.Native.Boreas
                .boreas_tunnel_start(&config, &device, &bypassTable, out handle)
                .Recognised;
        }
        catch
        {
            // The call did not happen - a missing boreas.dll is the realistic
            // case - so nothing released the contexts and this must.
            TunDevice.Abandon(device);
            BypassVtable.Abandon(bypassTable);
            throw;
        }

        if (status is not BoreasStatus.Ok)
        {
            // Deliberately no Abandon here: Boreas already ran both releases,
            // and running them again would be a double free.
            handle.Dispose();
            throw new BoreasException(status, "Starting the tunnel");
        }

        return new NativeTunnel(handle, onEvent, onEnded);
    }

    /// <summary>
    /// Compares the header's version against the library's.
    /// </summary>
    public static void RequireMatchingAbi()
    {
        var loaded = Boreas.Interop.Native.Boreas.boreas_abi_version();

        if (loaded != Boreas.Interop.Native.Boreas.CompiledAbiVersion)
        {
            throw new BoreasAbiMismatchException(Boreas.Interop.Native.Boreas.CompiledAbiVersion, loaded);
        }
    }

    /// <summary>
    /// Replaces the rules in force, without restarting or dropping a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A whole list set, never a delta. A rebuild compiles a fresh index and
    /// publishes it in one write, so every query is decided against exactly one
    /// version; applying edits incrementally would make "which rules did this
    /// query see" a question with no answer.
    /// </para>
    /// <para>
    /// <b>The same reload is reported twice</b> - once here and once on the
    /// event stream - and they describe one reload. Drive a UI from the stream,
    /// which is also where a reload triggered elsewhere arrives; the value
    /// returned here is for a caller that wants the answer synchronously.
    /// </para>
    /// <para>
    /// Safe to call while the reader is blocked, which is the case that
    /// matters, because that reader may be blocked for a very long time.
    /// </para>
    /// </remarks>
    public TunnelEvent.Reloaded Reload(IReadOnlyCollection<string> lists)
    {
        using var block = new Utf8Block();

        var items = block.AddArray(lists, out var count);

        BoreasEvent result;
        var status = Boreas.Interop.Native.Boreas
            .boreas_tunnel_reload(_handle, items, count, &result)
            .Recognised;

        return status is BoreasStatus.Ok
            ? new TunnelEvent.Reloaded(result.Allowed, result.BlockedRules, result.Inspected)
            : throw new BoreasException(status, "Reloading the rules");
    }

    /// <summary>
    /// Reads the certificate authority's material, or null when this tunnel
    /// does not intercept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two calls: once with both capacities zero to learn the lengths, then
    /// again to fill. The sizing call answers BOREAS_BUFFER_TOO_SMALL when
    /// there is material - that status is the whole point of it, and it is the
    /// one place a failed call still writes its out-parameters - and BOREAS_OK
    /// with both lengths zero when there is none. api/windows.md's sample
    /// discards the sizing call's status, which cannot distinguish those two
    /// from a genuine failure.
    /// </para>
    /// <para>
    /// Store and offer unconditionally, every launch: storing what was just
    /// restored is a no-op write, and offering a root the user already trusts
    /// shows no dialog, so there is no branch here to get wrong.
    /// </para>
    /// </remarks>
    public AuthorityMaterial? Authority()
    {
        nuint certificateLength = 0;
        nuint keysLength = 0;

        var sizing = Boreas.Interop.Native.Boreas
            .boreas_tunnel_authority(_handle, null, 0, &certificateLength, null, 0, &keysLength)
            .Recognised;

        if (sizing is BoreasStatus.Ok)
        {
            // Both lengths zero: this tunnel does not intercept. An answer, not
            // a failure.
            return null;
        }

        if (sizing is not BoreasStatus.BufferTooSmall)
        {
            throw new BoreasException(sizing, "Sizing the certificate authority");
        }

        var certificate = new byte[certificateLength];
        var keys = new byte[keysLength];

        BoreasStatus filling;

        fixed (byte* certificateBuffer = certificate, keysBuffer = keys)
        {
            filling = Boreas.Interop.Native.Boreas.boreas_tunnel_authority(
                _handle,
                certificateBuffer, certificateLength, &certificateLength,
                keysBuffer, keysLength, &keysLength).Recognised;
        }

        return filling is BoreasStatus.Ok
            ? new AuthorityMaterial([.. certificate], [.. keys])
            : throw new BoreasException(filling, "Reading the certificate authority");
    }

    /// <summary>
    /// Stops the tunnel: shutdown, join the reader, free the handle.
    /// </summary>
    /// <returns>
    /// What shutdown reported. BOREAS_IO here means shutdown itself hit an I/O
    /// failure; the teardown still completed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Idempotent, and idempotent by more than luck: <c>shutdown</c> is
    /// documented safe to call twice, and the interlock is what keeps the join
    /// and the free from happening twice.
    /// </para>
    /// <para>
    /// The status is returned rather than thrown, because the three steps must
    /// all run: throwing at the first would skip the join and leave a thread
    /// inside a call on a handle about to be freed, which is the exact
    /// use-after-free the three-step order exists to prevent.
    /// </para>
    /// <para>
    /// After this, the device's close and release callbacks have run - or will
    /// shortly, if a <c>recv</c> was in flight - and only then does the Wintun
    /// session end, because ending it is what the ring's disposal does and the
    /// ring is disposed by release.
    /// </para>
    /// </remarks>
    public BoreasStatus Stop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return BoreasStatus.Stopped;
        }

        var status = Boreas.Interop.Native.Boreas.boreas_tunnel_shutdown(_handle).Recognised;

        _reader.Join();
        _handle.Dispose();

        return status;
    }

    public void Dispose() => _ = Stop();

    private void ReadEvents(Action<TunnelEvent> onEvent, Action<BoreasStatus>? onEnded)
    {
        var name = stackalloc byte[TextCapacity];
        var rule = stackalloc byte[TextCapacity];

        while (true)
        {
            BoreasEvent raw;

            var status = Boreas.Interop.Native.Boreas.boreas_tunnel_next_event(
                _handle, &raw, name, TextCapacity, rule, TextCapacity).Recognised;

            if (status is not BoreasStatus.Ok)
            {
                // BOREAS_STOPPED is the normal way this loop ends: it is how a
                // reader learns another thread called shutdown.
                ReaderEndedWith = status;
                onEnded?.Invoke(status);
                return;
            }

            var translated = TunnelEvent.TryFrom(
                in raw,
                Marshal.PtrToStringUTF8((nint)name) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)rule) ?? string.Empty,
                TextCapacity,
                TextCapacity);

            // Null is an event kind this build predates. api/stability.md says
            // to ignore what cannot be interpreted; an event added later is one
            // that was not being missed before.
            if (translated is null)
            {
                continue;
            }

            try
            {
                onEvent(translated);
            }
            catch
            {
                // The observer is documented as not throwing. If it does, the
                // reader is the wrong place to die: losing it would strand the
                // tunnel with nothing draining its events, and events_lost is
                // the only trace that would leave.
            }
        }
    }
}

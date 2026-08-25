using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Boreas.Interop.Native;

namespace Boreas.Interop.Tests;

/// <summary>
/// The offsets and widths <c>ffi/include/boreas.h</c> pins, asserted from C#.
/// </summary>
/// <remarks>
/// <para>
/// The header asserts these from the C side so a toolchain whose flags would
/// move a field fails the host's build. Nothing does that for a C# host: a
/// wrong width here compiles, links, and reads a field from the middle of
/// another field, silently, under load. This class is the missing half.
/// </para>
/// <para>
/// Two views are checked at every offset, and the difference between them is
/// the point. The <b>runtime</b> view is what native code actually sees,
/// because these structs cross as pointers and nothing marshals them. The
/// <b>marshalling</b> view is what <c>Marshal.SizeOf</c> and
/// <c>Marshal.OffsetOf</c> report, and it is the one that moves when a
/// <c>[MarshalAs(UnmanagedType.U1)]</c> is dropped from a <c>bool</c>.
/// Asserting they agree is what turns that dropped attribute from a latent
/// trap into a red build.
/// </para>
/// </remarks>
public sealed class AbiLayout
{
    /// <summary>
    /// Where a field sits relative to the start of its struct, as the runtime
    /// lays it out.
    /// </summary>
    private static unsafe nint RuntimeOffset<TStruct, TField>(ref TStruct origin, ref TField member)
        where TStruct : unmanaged
        where TField : unmanaged =>
        Unsafe.ByteOffset(
            ref Unsafe.As<TStruct, byte>(ref origin),
            ref Unsafe.As<TField, byte>(ref member));

    /// <summary>
    /// Both views of one field's offset, so every assertion below states them
    /// together rather than trusting whichever was easier to read.
    /// </summary>
    private static unsafe void AssertField<TStruct, TField>(
        int expected, ref TStruct origin, ref TField member, string name)
        where TStruct : unmanaged
        where TField : unmanaged
    {
        Assert.Equal((nint)expected, RuntimeOffset(ref origin, ref member));
        Assert.Equal((nint)expected, Marshal.OffsetOf<TStruct>(name));
    }

    private static unsafe void AssertSize<T>(int expected)
        where T : unmanaged
    {
        Assert.Equal(expected, sizeof(T));
        Assert.Equal(expected, Marshal.SizeOf<T>());
    }

    /// <summary>
    /// The two shipped architectures are win-x64 and win-arm64, and every
    /// offset below assumes their pointer width. A 32-bit host would need a
    /// different table, so it is refused here rather than read wrong.
    /// </summary>
    /// <remarks>
    /// Mirrors the header's own
    /// <c>sizeof(size_t) == sizeof(void *)</c> and
    /// <c>sizeof(intptr_t) == sizeof(void *)</c>.
    /// </remarks>
    [Fact]
    public unsafe void The_abi_is_sixty_four_bit_and_size_t_is_pointer_width()
    {
        Assert.Equal(8, sizeof(void*));
        Assert.Equal(sizeof(void*), sizeof(nuint));
        Assert.Equal(sizeof(void*), sizeof(nint));
    }

    /// <summary>
    /// Mirrors the header's four enum-width assertions. A C enum's width is
    /// implementation-defined and <c>-fshort-enums</c> makes it one byte, which
    /// is the flag that moves <c>BoreasEvent.blocked</c> from four to one.
    /// </summary>
    [Fact]
    public unsafe void Every_enum_is_four_bytes()
    {
        Assert.Equal(4, sizeof(BoreasStatus));
        Assert.Equal(4, sizeof(BoreasEgress));
        Assert.Equal(4, sizeof(BoreasNat));
        Assert.Equal(4, sizeof(BoreasEventKind));
    }

    /// <summary>
    /// Mirrors <c>offsetof(BoreasDevice, context) == 0</c> and
    /// <c>offsetof(BoreasDevice, mtu) == 5 * sizeof(void *)</c>: the vtable a
    /// host fills in by hand, where a shifted field is a call through the wrong
    /// function pointer rather than a compile error.
    /// </summary>
    [Fact]
    public unsafe void The_device_vtable_matches_the_header()
    {
        var device = default(BoreasDevice);

        AssertField(0, ref device, ref device.Context, nameof(BoreasDevice.Context));
        Assert.Equal((nint)8, RuntimeOffset(ref device, ref Unsafe.AsRef<nint>(&device.Recv)));
        Assert.Equal((nint)16, RuntimeOffset(ref device, ref Unsafe.AsRef<nint>(&device.Send)));
        Assert.Equal((nint)24, RuntimeOffset(ref device, ref Unsafe.AsRef<nint>(&device.Close)));
        Assert.Equal((nint)32, RuntimeOffset(ref device, ref Unsafe.AsRef<nint>(&device.Release)));
        AssertField(5 * sizeof(void*), ref device, ref device.Mtu, nameof(BoreasDevice.Mtu));

        Assert.Equal(48, sizeof(BoreasDevice));
    }

    /// <summary>
    /// Mirrors <c>offsetof(BoreasBypass, context) == 0</c> and
    /// <c>sizeof(BoreasBypass) == 3 * sizeof(void *)</c>.
    /// </summary>
    [Fact]
    public unsafe void The_bypass_vtable_matches_the_header()
    {
        var bypass = default(BoreasBypass);

        AssertField(0, ref bypass, ref bypass.Context, nameof(BoreasBypass.Context));
        Assert.Equal((nint)8, RuntimeOffset(ref bypass, ref Unsafe.AsRef<nint>(&bypass.Protect)));
        Assert.Equal((nint)16, RuntimeOffset(ref bypass, ref Unsafe.AsRef<nint>(&bypass.Release)));

        Assert.Equal(3 * sizeof(void*), sizeof(BoreasBypass));
    }

    /// <summary>
    /// Mirrors <c>sizeof(BoreasCounters) == 6 * sizeof(uint64_t)</c>, and pins
    /// the order the six counters arrive in.
    /// </summary>
    [Fact]
    public unsafe void The_counters_match_the_header()
    {
        var counters = default(BoreasCounters);

        AssertField(0, ref counters, ref counters.DatagramsDropped, nameof(BoreasCounters.DatagramsDropped));
        AssertField(8, ref counters, ref counters.PacketsRejected, nameof(BoreasCounters.PacketsRejected));
        AssertField(16, ref counters, ref counters.QuicSteered, nameof(BoreasCounters.QuicSteered));
        AssertField(24, ref counters, ref counters.PathsReported, nameof(BoreasCounters.PathsReported));
        AssertField(32, ref counters, ref counters.EventsLost, nameof(BoreasCounters.EventsLost));
        AssertField(40, ref counters, ref counters.TasksPanicked, nameof(BoreasCounters.TasksPanicked));

        AssertSize<BoreasCounters>(6 * sizeof(ulong));
    }

    /// <summary>
    /// Mirrors <c>sizeof(BoreasCeilings) == 6 * sizeof(size_t)</c>. Written
    /// against <c>sizeof(nuint)</c> rather than a literal 48 so that declaring
    /// one of these <c>uint</c> - the second of the three type traps - fails
    /// here rather than truncating a ceiling on the way out.
    /// </summary>
    [Fact]
    public unsafe void The_ceilings_match_the_header()
    {
        var ceilings = default(BoreasCeilings);

        AssertField(0, ref ceilings, ref ceilings.BufferSlices, nameof(BoreasCeilings.BufferSlices));
        AssertField(8, ref ceilings, ref ceilings.DatagramsPerFlow, nameof(BoreasCeilings.DatagramsPerFlow));
        AssertField(16, ref ceilings, ref ceilings.TerminatedConnections, nameof(BoreasCeilings.TerminatedConnections));
        AssertField(24, ref ceilings, ref ceilings.Associations, nameof(BoreasCeilings.Associations));
        AssertField(32, ref ceilings, ref ceilings.InspectedAddresses, nameof(BoreasCeilings.InspectedAddresses));
        AssertField(40, ref ceilings, ref ceilings.PendingReassemblies, nameof(BoreasCeilings.PendingReassemblies));

        AssertSize<BoreasCeilings>(6 * sizeof(nuint));
    }

    /// <summary>
    /// Mirrors <c>offsetof(BoreasEvent, kind) == 0</c> and
    /// <c>offsetof(BoreasEvent, blocked) == 4</c>, then continues past what the
    /// header asserts to pin every remaining field.
    /// </summary>
    [Fact]
    public unsafe void The_event_matches_the_header()
    {
        var value = default(BoreasEvent);

        AssertField(0, ref value, ref value.Kind, nameof(BoreasEvent.Kind));
        AssertField(4, ref value, ref value.Blocked, nameof(BoreasEvent.Blocked));
        AssertField(8, ref value, ref value.NameLen, nameof(BoreasEvent.NameLen));
        AssertField(16, ref value, ref value.RuleLen, nameof(BoreasEvent.RuleLen));
        AssertField(24, ref value, ref value.Allowed, nameof(BoreasEvent.Allowed));
        AssertField(32, ref value, ref value.BlockedRules, nameof(BoreasEvent.BlockedRules));
        AssertField(40, ref value, ref value.Inspected, nameof(BoreasEvent.Inspected));
        AssertField(48, ref value, ref value.Counters, nameof(BoreasEvent.Counters));

        AssertSize<BoreasEvent>(96);
    }

    /// <summary>
    /// The three thirty-two-byte keys sit where <c>uint8_t[32]</c> does, and
    /// the flag that distinguishes an absent pre-shared key from thirty-two
    /// zero bytes sits after them.
    /// </summary>
    [Fact]
    public unsafe void The_wireguard_peer_matches_the_header()
    {
        var peer = default(BoreasWireGuard);

        Assert.Equal(32, sizeof(BoreasKey));

        AssertField(0, ref peer, ref peer.Endpoint, nameof(BoreasWireGuard.Endpoint));
        AssertField(8, ref peer, ref peer.PrivateKey, nameof(BoreasWireGuard.PrivateKey));
        AssertField(40, ref peer, ref peer.PeerPublicKey, nameof(BoreasWireGuard.PeerPublicKey));
        AssertField(72, ref peer, ref peer.PresharedKey, nameof(BoreasWireGuard.PresharedKey));
        AssertField(104, ref peer, ref peer.HasPresharedKey, nameof(BoreasWireGuard.HasPresharedKey));

        AssertSize<BoreasWireGuard>(112);
    }

    /// <summary>
    /// Mirrors <c>offsetof(BoreasConfig, egress) == 0</c>, and pins the other
    /// fourteen fields.
    /// </summary>
    /// <remarks>
    /// <b>The <c>Mtu</c> line is the bool trap's detector.</b> It is the only
    /// offset in the whole ABI that a dropped
    /// <c>[MarshalAs(UnmanagedType.U1)]</c> actually moves: everywhere else the
    /// three extra bytes vanish into padding the next field needed anyway.
    /// Widen <c>RewriteDocuments</c> to four bytes and <c>Mtu</c> goes from 202
    /// to 204, which is what this line refuses.
    /// </remarks>
    [Fact]
    public unsafe void The_config_matches_the_header()
    {
        var config = default(BoreasConfig);

        AssertField(0, ref config, ref config.Egress, nameof(BoreasConfig.Egress));
        AssertField(8, ref config, ref config.WireGuard, nameof(BoreasConfig.WireGuard));
        AssertField(120, ref config, ref config.NatBehavior, nameof(BoreasConfig.NatBehavior));
        AssertField(128, ref config, ref config.Resolver, nameof(BoreasConfig.Resolver));
        AssertField(136, ref config, ref config.Lists, nameof(BoreasConfig.Lists));
        AssertField(144, ref config, ref config.ListCount, nameof(BoreasConfig.ListCount));
        AssertField(152, ref config, ref config.InterceptHosts, nameof(BoreasConfig.InterceptHosts));
        AssertField(160, ref config, ref config.InterceptHostCount, nameof(BoreasConfig.InterceptHostCount));
        AssertField(168, ref config, ref config.RootCertificate, nameof(BoreasConfig.RootCertificate));
        AssertField(176, ref config, ref config.RootCertificateLen, nameof(BoreasConfig.RootCertificateLen));
        AssertField(184, ref config, ref config.AuthorityKeys, nameof(BoreasConfig.AuthorityKeys));
        AssertField(192, ref config, ref config.AuthorityKeysLen, nameof(BoreasConfig.AuthorityKeysLen));
        AssertField(200, ref config, ref config.RewriteDocuments, nameof(BoreasConfig.RewriteDocuments));
        AssertField(202, ref config, ref config.Mtu, nameof(BoreasConfig.Mtu));
        AssertField(208, ref config, ref config.Ceilings, nameof(BoreasConfig.Ceilings));

        AssertSize<BoreasConfig>(256);
    }

    /// <summary>
    /// An all-zero ceilings value is the defaults, so <c>default</c> has to be
    /// exactly that and not merely nearly.
    /// </summary>
    [Fact]
    public unsafe void A_default_ceilings_is_all_zero_bytes()
    {
        var ceilings = default(BoreasCeilings);
        var bytes = new ReadOnlySpan<byte>(&ceilings, sizeof(BoreasCeilings));

        Assert.True(bytes.IndexOfAnyExcept((byte)0) < 0);
    }

    /// <summary>
    /// A zeroed vtable is a vtable with no callbacks, not undefined: an absent
    /// callback is a null function pointer, and a null function pointer is
    /// all-zero bytes.
    /// </summary>
    [Fact]
    public unsafe void A_default_vtable_has_no_callbacks()
    {
        var device = default(BoreasDevice);
        var bypass = default(BoreasBypass);

        Assert.True(device.Recv is null);
        Assert.True(device.Send is null);
        Assert.True(device.Close is null);
        Assert.True(device.Release is null);
        Assert.True(bypass.Protect is null);
        Assert.True(bypass.Release is null);
    }
}

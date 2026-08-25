using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the lowering from a validated configuration to the wire struct.
/// </summary>
/// <remarks>
/// This is where a configuration a person can read becomes the bytes Boreas
/// reads, and it is checkable in full without the library: the pointers point
/// into an arena this test owns, so every string and every array can be read
/// back and compared with what went in.
/// </remarks>
public sealed unsafe class NativeConfigLaws
{
    private static string? ReadString(nint pointer) => Marshal.PtrToStringUTF8(pointer);

    private static string?[] ReadArray(nint items, nuint count) =>
        [.. Enumerable.Range(0, (int)count).Select(index => ReadString(((nint*)items)[index]))];

    private static byte[] ReadBytes(nint pointer, nuint length) =>
        pointer == nint.Zero ? [] : new ReadOnlySpan<byte>((void*)pointer, (int)length).ToArray();

    private static Hostname Host(string value) => Present.Value(Hostname.TryParse(value));

    private static HostPort Endpoint(string value) => Present.Value(HostPort.TryParse(value));

    private static Mtu Size(int value) => Present.Value(Mtu.TryCreate(value));

    private static Key32 Key(byte fill) =>
        Present.Value(Key32.TryFrom(Enumerable.Repeat(fill, Key32.Length).ToArray()));

    private static TunnelConfiguration Configuration(
        Egress? egress = null, Resolution? resolution = null, int mtu = 1420, Ceilings ceilings = default) =>
        new(egress ?? new Egress.Direct(NatBehavior.AddressAndPortDependent),
            resolution ?? Resolution.Passthrough.Instance,
            Size(mtu),
            ceilings);

    /// <summary>
    /// One value reaches both fields, which is what removes the second of the
    /// two silent mistakes. The device's copy is set from the same
    /// <see cref="Mtu"/> by the ring, so there is no second number to disagree.
    /// </summary>
    [Fact]
    public void The_mtu_crosses_as_the_one_value_it_was_written_as()
    {
        using var block = new Utf8Block();

        Assert.Equal(1420, NativeConfig.Lower(Configuration(mtu: 1420), block).Mtu);
        Assert.Equal(1280, NativeConfig.Lower(Configuration(mtu: 1280), block).Mtu);
    }

    /// <summary>
    /// Passthrough is a null resolver with no lists and no interception. Every
    /// one of those is the zero the struct starts at, and that has to stay true
    /// rather than be nearly true.
    /// </summary>
    [Fact]
    public void Passthrough_leaves_the_resolver_null_and_nothing_configured()
    {
        using var block = new Utf8Block();

        var config = NativeConfig.Lower(Configuration(resolution: Resolution.Passthrough.Instance), block);

        Assert.Equal(nint.Zero, config.Resolver);
        Assert.Equal(nint.Zero, config.Lists);
        Assert.Equal(0u, (uint)config.ListCount);
        Assert.Equal(nint.Zero, config.InterceptHosts);
        Assert.Equal(0u, (uint)config.InterceptHostCount);
        Assert.Equal(nint.Zero, config.RootCertificate);
        Assert.Equal(nint.Zero, config.AuthorityKeys);
        Assert.False(config.RewriteDocuments);
    }

    [Fact]
    public void A_local_resolver_crosses_with_its_lists_in_order()
    {
        using var block = new Utf8Block();
        ImmutableArray<string> lists = ["||ads.example^", "||track.example^\n||more.example^"];

        var config = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(Endpoint("9.9.9.9:53"), lists, null)), block);

        Assert.Equal("9.9.9.9:53", ReadString(config.Resolver));
        Assert.Equal(2u, (uint)config.ListCount);
        Assert.Equal(lists, ReadArray(config.Lists, config.ListCount));
    }

    /// <summary>
    /// A resolver with no lists is legal: it answers locally and blocks
    /// nothing. The reverse - lists with no resolver - has no spelling.
    /// </summary>
    [Fact]
    public void A_local_resolver_with_no_lists_is_a_null_array_and_a_zero_count()
    {
        using var block = new Utf8Block();

        var config = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(Endpoint("1.1.1.1:53"), [], null)), block);

        Assert.Equal("1.1.1.1:53", ReadString(config.Resolver));
        Assert.Equal(nint.Zero, config.Lists);
        Assert.Equal(0u, (uint)config.ListCount);
    }

    [Fact]
    public void Interception_crosses_with_its_host_allowlist()
    {
        using var block = new Utf8Block();

        var hosts = Present.Value(InterceptHosts.TryCreate([Host("news.example.com"), Host("shop.example.com")]));

        var config = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(
                Endpoint("1.1.1.1:53"), [], new Interception(hosts, Trust.Generate.Instance, RewriteDocuments: true))),
            block);

        Assert.Equal(2u, (uint)config.InterceptHostCount);
        Assert.Equal(["news.example.com", "shop.example.com"], ReadArray(config.InterceptHosts, config.InterceptHostCount));
        Assert.True(config.RewriteDocuments);
    }

    /// <summary>
    /// Generate is both halves null. Supplying one of the two is BOREAS_CONFIG,
    /// and the sum is why that has no spelling - but the lowering still has to
    /// produce the pair the sum promised.
    /// </summary>
    [Fact]
    public void Generating_an_authority_leaves_both_halves_null()
    {
        using var block = new Utf8Block();

        var config = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(
                Endpoint("1.1.1.1:53"), [],
                new Interception(
                    Present.Value(InterceptHosts.TryCreate([Host("news.example.com")])),
                    Trust.Generate.Instance,
                    RewriteDocuments: false))),
            block);

        Assert.Equal(nint.Zero, config.RootCertificate);
        Assert.Equal(0u, (uint)config.RootCertificateLen);
        Assert.Equal(nint.Zero, config.AuthorityKeys);
        Assert.Equal(0u, (uint)config.AuthorityKeysLen);
    }

    /// <summary>
    /// Restore is both halves, each with its own length. The lengths travel
    /// separately because the material is opaque bytes, not text, so there is
    /// no terminator to find one by.
    /// </summary>
    [Fact]
    public void Restoring_an_authority_crosses_both_halves_with_their_lengths()
    {
        using var block = new Utf8Block();

        var certificate = Enumerable.Range(0, 300).Select(static i => (byte)i).ToImmutableArray();
        var keys = Enumerable.Range(0, 121).Select(static i => (byte)(255 - i)).ToImmutableArray();

        var config = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(
                Endpoint("1.1.1.1:53"), [],
                new Interception(
                    Present.Value(InterceptHosts.TryCreate([Host("news.example.com")])),
                    new Trust.Restore(certificate, keys),
                    RewriteDocuments: false))),
            block);

        Assert.Equal(300u, (uint)config.RootCertificateLen);
        Assert.Equal(121u, (uint)config.AuthorityKeysLen);
        Assert.Equal(certificate, ReadBytes(config.RootCertificate, config.RootCertificateLen));
        Assert.Equal(keys, ReadBytes(config.AuthorityKeys, config.AuthorityKeysLen));

        // Bytes, not text: a terminator here would be content Boreas read.
        Assert.NotEqual(nint.Zero, config.RootCertificate);
        Assert.NotEqual(nint.Zero, config.AuthorityKeys);
    }

    [Theory]
    [InlineData(NatBehavior.EndpointIndependent, BoreasNat.EndpointIndependent)]
    [InlineData(NatBehavior.AddressDependent, BoreasNat.AddressDependent)]
    [InlineData(NatBehavior.AddressAndPortDependent, BoreasNat.AddressAndPortDependent)]
    public void A_direct_egress_carries_its_nat_behaviour(NatBehavior managed, BoreasNat native)
    {
        using var block = new Utf8Block();

        var config = NativeConfig.Lower(Configuration(egress: new Egress.Direct(managed)), block);

        Assert.Equal(BoreasEgress.Direct, config.Egress);
        Assert.Equal(native, config.NatBehavior);
    }

    [Fact]
    public void A_wireguard_egress_carries_its_endpoint_and_its_three_keys()
    {
        using var block = new Utf8Block();

        var peer = new WireGuardPeer(Endpoint("203.0.113.7:51820"), Key(0x11), Key(0x22), Key(0x33));

        var config = NativeConfig.Lower(Configuration(egress: new Egress.WireGuard(peer)), block);

        Assert.Equal(BoreasEgress.WireGuard, config.Egress);
        Assert.Equal("203.0.113.7:51820", ReadString(config.WireGuard.Endpoint));
        Assert.True(((ReadOnlySpan<byte>)config.WireGuard.PrivateKey).SequenceEqual(peer.PrivateKey.Value));
        Assert.True(((ReadOnlySpan<byte>)config.WireGuard.PeerPublicKey).SequenceEqual(peer.PeerPublicKey.Value));
        Assert.True(((ReadOnlySpan<byte>)config.WireGuard.PresharedKey).SequenceEqual(peer.PresharedKey!.Value));
        Assert.True(config.WireGuard.HasPresharedKey);
    }

    /// <summary>
    /// Thirty-two zero bytes is a key somebody may legitimately have
    /// configured, so "all zero" cannot mean "absent" and the flag is what
    /// distinguishes them.
    /// </summary>
    [Fact]
    public void An_absent_preshared_key_is_zero_bytes_with_the_flag_clear()
    {
        using var block = new Utf8Block();

        var peer = new WireGuardPeer(Endpoint("203.0.113.7:51820"), Key(0x11), Key(0x22), PresharedKey: null);

        var config = NativeConfig.Lower(Configuration(egress: new Egress.WireGuard(peer)), block);

        Assert.False(config.WireGuard.HasPresharedKey);
        Assert.True(((ReadOnlySpan<byte>)config.WireGuard.PresharedKey).IndexOfAnyExcept((byte)0) < 0);
    }

    /// <summary>
    /// A configured key of thirty-two zeroes is the same bytes as an absent
    /// one, and a different configuration. This is the pair the flag exists
    /// for, so it is asserted as a pair.
    /// </summary>
    [Fact]
    public void A_configured_zero_key_differs_from_an_absent_one_only_by_the_flag()
    {
        using var block = new Utf8Block();

        var zeroKey = Present.Value(Key32.TryFrom(new byte[Key32.Length]));
        var endpoint = Endpoint("203.0.113.7:51820");

        var configured = NativeConfig.Lower(
            Configuration(egress: new Egress.WireGuard(
                new WireGuardPeer(endpoint, Key(0x11), Key(0x22), zeroKey))), block);

        var absent = NativeConfig.Lower(
            Configuration(egress: new Egress.WireGuard(
                new WireGuardPeer(endpoint, Key(0x11), Key(0x22), null))), block);

        Assert.True(configured.WireGuard.HasPresharedKey);
        Assert.False(absent.WireGuard.HasPresharedKey);
        Assert.True(((ReadOnlySpan<byte>)configured.WireGuard.PresharedKey)
            .SequenceEqual((ReadOnlySpan<byte>)absent.WireGuard.PresharedKey));
    }

    /// <summary>
    /// Zero in any ceiling means "use the default for it", so the phone
    /// ceilings must cross as an all-zero struct rather than as the numbers
    /// they happen to stand for.
    /// </summary>
    [Fact]
    public void The_phone_ceilings_cross_as_zeroes_and_the_desktop_ones_do_not()
    {
        using var block = new Utf8Block();

        var phone = NativeConfig.Lower(Configuration(ceilings: Ceilings.Phone), block).Ceilings;
        var desktop = NativeConfig.Lower(Configuration(ceilings: Ceilings.Desktop), block).Ceilings;

        Assert.Equal(0u, (uint)phone.BufferSlices);
        Assert.Equal(0u, (uint)phone.TerminatedConnections);

        Assert.Equal(Ceilings.Desktop.BufferSlices, desktop.BufferSlices);
        Assert.Equal(Ceilings.Desktop.TerminatedConnections, desktop.TerminatedConnections);
        Assert.Equal(Ceilings.Desktop.PendingReassemblies, desktop.PendingReassemblies);
    }

    /// <summary>
    /// The arena owns every pointer the struct holds, and it is scoped to the
    /// start call because that is exactly how long Boreas borrows them for.
    /// Disposing it twice is what a <c>using</c> plus an explicit close does.
    /// </summary>
    [Fact]
    public void The_arena_releases_everything_the_struct_pointed_at()
    {
        var block = new Utf8Block();

        _ = NativeConfig.Lower(
            Configuration(resolution: new Resolution.Local(
                Endpoint("1.1.1.1:53"), ["||ads.example^"],
                new Interception(
                    Present.Value(InterceptHosts.TryCreate([Host("news.example.com")])),
                    new Trust.Restore([1, 2, 3], [4, 5, 6]),
                    RewriteDocuments: true))),
            block);

        block.Dispose();
        block.Dispose();
    }
}

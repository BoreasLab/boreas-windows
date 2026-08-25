using Boreas.Interop.Native;

namespace Boreas.Interop.Tunnel;

/// <summary>
/// Lowers a <see cref="TunnelConfiguration"/> onto the wire struct.
/// </summary>
/// <remarks>
/// <para>
/// Every pointer the result holds belongs to the <see cref="Utf8Block"/> passed
/// in, and every one of them is borrowed <b>for the duration of the start call
/// only</b> - Boreas copies what it needs before returning. Keeping the arena a
/// parameter rather than a field is what ties the struct's validity to a
/// <c>using</c> the caller can see.
/// </para>
/// <para>
/// Written as pattern switches rather than the <c>Match</c> the sums provide,
/// because the fields being filled in are on a <c>ref</c> local and C# cannot
/// capture one in a lambda. The exhaustiveness the closed hierarchy buys is
/// kept by the throwing default arms.
/// </para>
/// </remarks>
internal static unsafe class NativeConfig
{
    public static BoreasConfig Lower(TunnelConfiguration configuration, Utf8Block block)
    {
        var config = new BoreasConfig
        {
            // The same Mtu value reaches BoreasDevice.mtu through the ring, so
            // the two numbers the obligations warn about cannot differ.
            Mtu = configuration.Mtu.Value,
            Ceilings = configuration.Ceilings.ToNative(),
        };

        LowerEgress(configuration.Egress, block, ref config);
        LowerResolution(configuration.Resolution, block, ref config);

        return config;
    }

    private static void LowerEgress(Egress egress, Utf8Block block, ref BoreasConfig config)
    {
        switch (egress)
        {
            case Egress.Direct direct:
                config.Egress = BoreasEgress.Direct;
                config.NatBehavior = (BoreasNat)direct.NatBehavior;
                break;

            case Egress.WireGuard wireGuard:
                config.Egress = BoreasEgress.WireGuard;
                config.WireGuard = LowerPeer(wireGuard.Peer, block);
                break;

            default:
                throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(Egress)}: {egress}");
        }
    }

    private static BoreasWireGuard LowerPeer(WireGuardPeer peer, Utf8Block block)
    {
        var native = new BoreasWireGuard
        {
            Endpoint = (nint)block.Add(peer.Endpoint.ToString()),
            HasPresharedKey = peer.HasPresharedKey,
        };

        peer.PrivateKey.Value.CopyTo(native.PrivateKey);
        peer.PeerPublicKey.Value.CopyTo(native.PeerPublicKey);

        // Left as thirty-two zero bytes when absent, which the flag above is
        // what distinguishes from a key somebody configured as thirty-two
        // zeroes.
        peer.PresharedKey?.Value.CopyTo(native.PresharedKey);

        return native;
    }

    private static void LowerResolution(Resolution resolution, Utf8Block block, ref BoreasConfig config)
    {
        switch (resolution)
        {
            case Resolution.Passthrough:
                // Null resolver, no lists, no interception. Every field stays
                // at the zero the struct was built with.
                break;

            case Resolution.Local local:
                config.Resolver = (nint)block.Add(local.Upstream.ToString());
                config.Lists = (nint)block.AddArray([.. local.Lists], out var listCount);
                config.ListCount = listCount;

                if (local.Interception is { } interception)
                {
                    LowerInterception(interception, block, ref config);
                }

                break;

            default:
                throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(Resolution)}: {resolution}");
        }
    }

    private static void LowerInterception(Interception interception, Utf8Block block, ref BoreasConfig config)
    {
        config.InterceptHosts = (nint)block.AddArray(
            [.. interception.Hosts.Value.Select(static host => host.Value)], out var hostCount);
        config.InterceptHostCount = hostCount;
        config.RewriteDocuments = interception.RewriteDocuments;

        switch (interception.Trust)
        {
            case Trust.Generate:
                // Both halves stay null, which is what asks for a fresh
                // authority. Supplying one of the two is the combination the
                // sum makes unwritable.
                break;

            case Trust.Restore restore:
                config.RootCertificate = (nint)block.AddBytes(restore.RootCertificate.AsSpan());
                config.RootCertificateLen = (nuint)restore.RootCertificate.Length;
                config.AuthorityKeys = (nint)block.AddBytes(restore.Keys.AsSpan());
                config.AuthorityKeysLen = (nuint)restore.Keys.Length;
                break;

            default:
                throw new System.Diagnostics.UnreachableException(
                    $"Unhandled {nameof(Trust)}: {interception.Trust}");
        }
    }
}

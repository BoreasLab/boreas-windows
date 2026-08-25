using System.Runtime.Versioning;
using Boreas.Interop.Authority;
using Boreas.Interop.Bypass;
using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;
using Boreas.Interop.Wintun;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// Brings up a Wintun adapter and starts a Boreas tunnel on it.
/// </summary>
/// <remarks>
/// <para>
/// Everything privileged is here: creating an adapter, configuring an
/// interface, writing the current user's trust store. That is the whole reason
/// <see cref="ITunnelHost"/> exists - so the control surface in front of it can
/// be a state machine, and so this can be replaced by the process that actually
/// holds those privileges without the surface noticing.
/// </para>
/// <para>
/// <b>This requires an elevated process.</b> <c>WintunCreateAdapter</c> and
/// <c>netsh interface</c> both do. The failure is a clear Win32 error rather
/// than a mystery, which is the most this type can do about it: whether the
/// process holding it is the application or a service beside it is a packaging
/// decision, and not one this file makes.
/// </para>
/// <para>
/// <b>Unverified on a device.</b> Every step below follows a documented API and
/// none of it has been run against a Wintun driver, because there is no Windows
/// machine in this repository's build or test path. What can be checked without
/// one has been: the ABI layout, both vtables' obligations, the configuration
/// lowering, the mask arithmetic, and this file's type-checking.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WintunTunnelHost : ITunnelHost
{
    /// <summary>What Windows shows beside the adapter.</summary>
    private const string TunnelType = "Boreas";

    private readonly EnginePolicy _policy;
    private readonly PhysicalInterfaceBypass _bypass;
    private readonly AuthorityStore _authority;
    private readonly string _bypassInterfaceName;

    public WintunTunnelHost(
        EnginePolicy policy,
        PhysicalInterfaceBypass bypass,
        string bypassInterfaceName,
        AuthorityStore authority)
    {
        _policy = policy;
        _bypass = bypass;
        _bypassInterfaceName = bypassInterfaceName;
        _authority = authority;
    }

    public IRunningTunnel Start(ValidatedConfiguration configuration, Action<TunnelEvent> onEvent)
    {
        // The form's packet size is 1280..9000 and an MTU is 1280..65535, so
        // the narrower range sits inside the wider one and this cannot fail.
        // Stated rather than assumed, because the two ranges are declared in
        // different files and could stop agreeing.
        var mtu = Mtu.TryCreate(configuration.PacketSize.Value)
            ?? throw new InvalidOperationException(
                $"A packet size of {configuration.PacketSize} is not an MTU. {Mtu.Requirement}");

        var adapterName = configuration.Adapter.Value;

        var ring = WintunRing.Create(adapterName, TunnelType, mtu.Value, RingCapacity.Default);

        try
        {
            AdapterSetup.Apply(
                adapterName,
                configuration.Address.Address,
                configuration.Address.PrefixLength,
                mtu,
                configuration.Dns.Value);
        }
        catch
        {
            // The ring has not been handed to a vtable yet, so nothing else
            // will release it. After the handover this would be a double free,
            // which is why it is only here.
            ring.Dispose();
            throw;
        }

        var tunnel = NativeTunnel.Start(
            new TunnelConfiguration(SelectEgress(configuration.Egress), _policy.Resolution, mtu, _policy.Ceilings),
            ring,
            _bypass,
            onEvent);

        try
        {
            OfferAuthority(tunnel);
        }
        catch
        {
            // The tunnel is running and its reader is blocked in a call. The
            // three steps are the only correct way to take that apart, whatever
            // brought us here.
            tunnel.Dispose();
            throw;
        }

        return new RunningTunnel(tunnel, ring, _bypass, _bypassInterfaceName);
    }

    private Egress SelectEgress(EgressPolicy chosen) => chosen switch
    {
        EgressPolicy.Direct => new Egress.Direct(_policy.NatBehavior),

        EgressPolicy.WireGuard => _policy.Peer is { } peer
            ? new Egress.WireGuard(peer)
            : throw new InvalidOperationException(
                "This installation has no WireGuard peer configured, so Boreas cannot reach the "
                + "network that way. Choose a direct egress, or install a peer."),

        _ => throw Unreachable.Value(chosen),
    };

    /// <summary>
    /// Stores the authority and offers its root, unconditionally.
    /// </summary>
    /// <remarks>
    /// No branch here, deliberately. Storing what was just restored is a no-op
    /// write and offering a root the user already trusts shows no dialog, so a
    /// check would be a second way to be wrong about the same question.
    /// </remarks>
    private void OfferAuthority(NativeTunnel tunnel)
    {
        // Null means this tunnel does not intercept, which is an answer rather
        // than a failure and needs no certificate at all.
        if (tunnel.Authority() is not { } material)
        {
            return;
        }

        _authority.Save(material);
        RootCertificate.Offer(material.RootCertificate.AsSpan());
    }

    /// <summary>
    /// One running session: the tunnel, the ring its counters come from, and
    /// the bypass whose state the status reports.
    /// </summary>
    private sealed class RunningTunnel(
        NativeTunnel tunnel, WintunRing ring, PhysicalInterfaceBypass bypass, string bypassInterfaceName)
        : IRunningTunnel
    {
        /// <remarks>
        /// Read from the ring rather than kept here, because the ring is where
        /// the packets actually cross and a second copy would be a second thing
        /// to keep right.
        /// </remarks>
        public SessionCounters Counters => new(
            (ulong)ring.PacketsIn, (ulong)ring.PacketsOut, (ulong)ring.BytesIn, (ulong)ring.BytesOut);

        /// <remarks>
        /// Index zero is not an interface, and a bypass that cannot name one
        /// refuses every socket - which is the honest thing to report, because
        /// the alternative is a tunnel claiming protection it is not providing.
        /// </remarks>
        public EgressBypass Bypass => bypass.InterfaceIndex == 0
            ? new EgressBypass.Degraded(new TypedError(
                "host.bypass_unknown",
                "Boreas does not know which network adapter to send its own traffic through.",
                "Check that this device has a working network connection.",
                "The physical interface index is not set."))
            : new EgressBypass.Bound(bypassInterfaceName);

        public TunnelEvent.Reloaded Reload(IReadOnlyCollection<string> lists) => tunnel.Reload(lists);

        public BoreasStatus Stop() => tunnel.Stop();

        /// <remarks>
        /// The ring is not disposed here. It was handed to the device vtable,
        /// and Boreas calls that vtable's release exactly once - after every
        /// other callback has returned, which is what makes the Wintun session
        /// end after shutdown rather than before. Disposing it here as well
        /// would end the session twice.
        /// </remarks>
        public void Dispose() => tunnel.Dispose();
    }
}

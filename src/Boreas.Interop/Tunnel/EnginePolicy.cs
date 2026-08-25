namespace Boreas.Interop.Tunnel;

/// <summary>
/// The half of a tunnel's configuration the network form does not edit.
/// </summary>
/// <remarks>
/// <para>
/// The form owns the adapter: its name, its address, its MTU, its DNS servers,
/// how much of the routing table it claims, and which of the two egresses to
/// use. Everything else - which lists are in force, where names are answered,
/// what is intercepted, how much the tunnel may hold - has no control on it,
/// and inventing one would be inventing product.
/// </para>
/// <para>
/// So it arrives here instead, from whatever installed the product, and
/// <c>WintunTunnelHost</c> is where the two halves meet. Keeping them apart
/// makes the split explicit rather than leaving a form that appears to
/// configure a tunnel while half of it comes from somewhere the reader cannot
/// see.
/// </para>
/// <para>
/// <b>The resolver is cleartext DNS.</b> DoT, DoH and DoQ are on api/abi.md's
/// not-yet-exposed list, so one reached across an untrusted network is readable
/// by anything on the path. That is why the resolver is here rather than on the
/// form: a text box inviting a public resolver would be a UI that invites the
/// gap. A resolver on the local device or across a trusted link is the answer
/// until the ABI grows.
/// </para>
/// </remarks>
/// <param name="Peer">
/// Required when the form selects a WireGuard egress, and unused otherwise. The
/// form can only choose between the two egresses the ABI has; it cannot supply
/// a peer, because keys are not something to type into a settings page.
/// </param>
public sealed record EnginePolicy(
    Resolution Resolution,
    NatBehavior NatBehavior,
    WireGuardPeer? Peer,
    Ceilings Ceilings)
{
    /// <summary>
    /// Names answered locally against no lists, out by the host's own routes.
    /// </summary>
    /// <remarks>
    /// The conservative NAT behaviour, because Boreas cannot measure it and
    /// this one never claims more than is true. Desktop ceilings, because that
    /// is what this product runs on and the core's defaults are sized for a
    /// phone.
    /// </remarks>
    public static EnginePolicy Default { get; } = new(
        Resolution.Passthrough.Instance,
        NatBehavior.AddressAndPortDependent,
        Peer: null,
        Ceilings.Desktop);
}

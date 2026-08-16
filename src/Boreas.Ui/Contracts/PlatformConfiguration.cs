namespace Boreas.Ui.Contracts;

/// <summary>
/// The editable configuration as text: what a person typed, or what the
/// service reported.
/// </summary>
/// <remarks>
/// Strings keep this side of the boundary untrusted; only
/// <see cref="ValidatedConfiguration"/> crosses to the service. Installation
/// policy, including account, pipe authorization, and Wintun, is not editable.
/// </remarks>
public sealed record ConfigurationDraft(
    string AdapterName,
    string InterfaceAddress,
    string Mtu,
    string DnsServers,
    RouteMode Routes,
    EgressPolicy Egress);

/// <summary>How much of the routing table the adapter claims.</summary>
public enum RouteMode
{
    /// <summary>Everything goes through Boreas. The default route is claimed.</summary>
    Default,

    /// <summary>Only the configured destinations go through Boreas.</summary>
    Selected,
}

/// <summary>The core's egress choice. The host only has to make it reachable.</summary>
public enum EgressPolicy
{
    Direct,
    Relay,
}

/// <summary>
/// What the service did with a <c>configuration_changed</c> request.
/// </summary>
/// <remarks>
/// The contract allows only applied, restart-required, or rejected outcomes;
/// partial silent application is not represented.
/// </remarks>
public abstract record ConfigurationOutcome
{
    private ConfigurationOutcome() { }

    public sealed record Applied : ConfigurationOutcome;

    /// <summary>
    /// Accepted and stored; the running session uses it after restart.
    /// </summary>
    public sealed record RestartRequired : ConfigurationOutcome;

    /// <summary>
    /// Rejected at the trust boundary, with messages keyed to their fields.
    /// </summary>
    /// <remarks>
    /// The channel translates wire names once, so callers cannot receive an
    /// error keyed to a field this client does not know.
    /// </remarks>
    public sealed record Rejected(
        TypedError Error,
        IReadOnlyDictionary<ConfigField, string> FieldErrors) : ConfigurationOutcome;

    public TResult Match<TResult>(
        Func<Applied, TResult> applied,
        Func<RestartRequired, TResult> restartRequired,
        Func<Rejected, TResult> rejected) => this switch
        {
            Applied s => applied(s),
            RestartRequired s => restartRequired(s),
            Rejected s => rejected(s),
            _ => throw Unreachable.Value(this),
        };
}

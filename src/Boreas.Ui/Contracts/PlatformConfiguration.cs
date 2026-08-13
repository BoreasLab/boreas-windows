namespace Boreas.Ui.Contracts;

/// <summary>
/// The editable configuration as text: what a person typed, or what the
/// service reported.
/// </summary>
/// <remarks>
/// Deliberately all strings. This is the untrusted side of the boundary;
/// <see cref="ConfigurationParser.Parse"/> turns it into a
/// <see cref="ValidatedConfiguration"/>, and only that can be sent. The client
/// parsing early is a courtesy that puts the message next to the field one
/// round trip sooner. The service remains the authority.
///
/// Service account, packaging, pipe authorization policy and the Wintun binary
/// are installation policy and are deliberately not editable here.
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
/// The contract forbids partial silent application, so there is no "mostly
/// applied" case here. Either it took effect, or it needs a restart to take
/// effect, or it was rejected and nothing changed.
/// </remarks>
public abstract record ConfigurationOutcome
{
    private ConfigurationOutcome() { }

    public sealed record Applied : ConfigurationOutcome;

    /// <summary>
    /// Accepted and stored, but the running session keeps its old values until
    /// it is restarted. The user is told which, and restarts when they choose.
    /// </summary>
    public sealed record RestartRequired : ConfigurationOutcome;

    /// <summary>
    /// Rejected at the trust boundary. <paramref name="FieldErrors"/> maps a
    /// field to its message so the client can place each one beside its cause
    /// instead of showing one banner for the whole form.
    /// </summary>
    /// <remarks>
    /// Keyed by <see cref="ConfigField"/> rather than by a wire string. The
    /// channel implementation translates the wire name once, when it decodes
    /// the response, so nothing above it has to cope with a field name that
    /// names no field.
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

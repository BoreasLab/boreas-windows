namespace Boreas.Ui.Contracts;

/// <summary>
/// The subset of <c>PlatformConfig</c> and <c>EngineConfig</c> a user may edit.
/// </summary>
/// <remarks>
/// The client parses input, but it does not decide anything: this record is a
/// proposal that the service validates at the trust boundary and turns into an
/// immutable trusted value before it starts. Client-side validation exists to
/// give the user the error next to the field they typed it in, one round trip
/// earlier. It is never the authority.
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
    /// field name to its message so the client can place each one beside its
    /// cause instead of showing one banner for the whole form.
    /// </summary>
    public sealed record Rejected(
        TypedError Error,
        IReadOnlyDictionary<string, string> FieldErrors) : ConfigurationOutcome;

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

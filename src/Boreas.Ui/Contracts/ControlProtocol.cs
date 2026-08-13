namespace Boreas.Ui.Contracts;

/// <summary>
/// The facts about the control protocol that both ends of the pipe have to
/// agree on, stated once so they cannot be agreed on differently.
/// </summary>
/// <remarks>
/// Everything here was previously either a literal repeated in two files or a
/// decision left for whoever writes the pipe client to make on the spot. Both
/// are the same defect: a shared constant with no single owner. W2 reads this
/// type instead of choosing again.
/// </remarks>
public static class ControlProtocol
{
    /// <summary>
    /// The version this client speaks.
    /// </summary>
    /// <remarks>
    /// The service rejects an envelope whose version it does not know, so this
    /// is the number the pipe client sends and the number
    /// <see cref="ControlChannelState.VersionMismatch"/> reports as the
    /// client's. It is stated here rather than passed around because a client
    /// that could report a version other than its own would be reporting a
    /// number about nothing.
    /// </remarks>
    public const int Version = 1;

    /// <summary>
    /// How many control events the client keeps.
    /// </summary>
    /// <remarks>
    /// One number, two consumers who must agree: the channel trims its buffer
    /// to this, and the diagnostics list calls a full window
    /// <see cref="CollectionState{T}.Partial"/> so the user is told the record
    /// is bounded rather than shown a silently truncated list. Held separately,
    /// a channel that kept fewer would make "load older" unreachable and a
    /// channel that kept more would make it permanent.
    /// </remarks>
    public const int EventWindow = 200;

    /// <summary>
    /// The wire name of a configuration field.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigurationOutcome.Rejected"/> is keyed by
    /// <see cref="ConfigField"/> so that nothing above the channel can hold a
    /// field name that names no field. That obligation needs a translation to
    /// discharge it, and until now the translation did not exist, so the pipe
    /// client would have invented both the names and the mapping.
    ///
    /// snake_case follows the command names in docs/core-contract.md. The
    /// contract does not fix these four, so this client proposes them; the
    /// point is that one side proposes and the other reads, rather than both
    /// guessing.
    ///
    /// The receiver is not called <c>field</c>. Inside a property body that
    /// identifier is the C# 14 contextual keyword for the synthesized backing
    /// field, and an extension property whose receiver shadows it is rejected
    /// outright (CS9282), which reads as "properties are not allowed here"
    /// rather than as the naming collision it is.
    /// </remarks>
    extension(ConfigField target)
    {
        public string WireName => target switch
        {
            ConfigField.Adapter => "adapter_name",
            ConfigField.Address => "interface_address",
            ConfigField.Mtu => "mtu",
            ConfigField.Dns => "dns_servers",
            _ => throw Unreachable.Value(target),
        };
    }

    /// <summary>
    /// The field a wire name denotes, or null when the service named something
    /// this client has no field for.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw: a newer service naming a field this build
    /// does not have is a version skew, not a defect, and the right response is
    /// to show the summary without pinning it to a field. This is the parse
    /// half of the boundary, and it is the only way a wire string becomes a
    /// <see cref="ConfigField"/>.
    ///
    /// Two switches rather than one table read both ways, deliberately, and
    /// unlike the selector orders elsewhere in this codebase. There the array
    /// was the domain concept and positions were derived from it; here the
    /// names are data, a table lookup would be O(n) with a closure per call
    /// where a switch is O(1) both directions, and the round-trip law is what
    /// holds the two in agreement.
    /// </remarks>
    extension(ConfigField)
    {
        public static ConfigField? FromWireName(string? name) => name switch
        {
            "adapter_name" => ConfigField.Adapter,
            "interface_address" => ConfigField.Address,
            "mtu" => ConfigField.Mtu,
            "dns_servers" => ConfigField.Dns,
            _ => null,
        };
    }
}

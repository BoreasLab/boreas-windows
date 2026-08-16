namespace Boreas.Ui.Contracts;

/// <summary>
/// Shared facts both ends of the control pipe must agree on.
/// </summary>
/// <remarks>
/// W2 reads these values instead of duplicating or choosing them independently.
/// </remarks>
public static class ControlProtocol
{
    /// <summary>
    /// The version this client speaks.
    /// </summary>
    /// <remarks>
    /// This is sent by the pipe client and reported as the client's version;
    /// reading it here prevents a mismatch state from reporting another value.
    /// </remarks>
    public const int Version = 1;

    /// <summary>
    /// How many control events the client keeps.
    /// </summary>
    /// <remarks>
    /// The channel and diagnostics view share this bound; a full window is
    /// presented as <see cref="CollectionState{T}.Partial"/>.
    /// </remarks>
    public const int EventWindow = 200;

    /// <summary>
    /// The wire name of a configuration field.
    /// </summary>
    /// <remarks>
    /// The names follow the command names in docs/core-contract.md. The
    /// receiver avoids <c>field</c>, a C# 14 contextual keyword in properties.
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
    /// Unknown names are version skew, so return null and leave the message
    /// unpinned rather than throwing.
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

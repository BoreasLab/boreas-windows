using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Boreas.Ui.Contracts;

/// <summary>
/// The refined values a validated configuration is built from.
/// </summary>
/// <remarks>
/// Private constructors and <c>TryParse</c> keep validation at the boundary;
/// each value therefore proves that its own rule passed. Records avoid the
/// invalid default values a struct would permit, and each type owns its error
/// message so validation and guidance cannot drift.
/// </remarks>
public sealed record AdapterName
{
    public const string Requirement =
        "Give the adapter a name. It appears in Windows network settings.";

    private AdapterName(string value) => Value = value;

    public string Value { get; }

    public static AdapterName? TryParse(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : new AdapterName(raw.Trim());

    public override string ToString() => Value;
}

/// <summary>This device's address inside the tunnel, with its prefix length.</summary>
public sealed record TunnelAddress
{
    public const string Requirement =
        "Write the address as an IP address, a slash, and a prefix length, "
        + "for example 10.7.0.2/24 or fd00::2/64.";

    private TunnelAddress(IPAddress address, int prefixLength)
    {
        Address = address;
        PrefixLength = prefixLength;
    }

    public IPAddress Address { get; }

    public int PrefixLength { get; }

    /// <summary>The widest prefix this address family allows.</summary>
    public int MaximumPrefixLength =>
        Address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;

    public static TunnelAddress? TryParse(string? raw)
    {
        if (raw?.Trim() is not { Length: > 0 } text)
        {
            return null;
        }

        // The list pattern makes CIDR's exactly-two-part shape explicit.
        if (text.Split('/', StringSplitOptions.TrimEntries) is not [var host, var prefix]
            || !IPAddress.TryParse(host, out var address))
        {
            return null;
        }

        var maximum = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;

        return int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
               && length >= 0 && length <= maximum
            ? new TunnelAddress(address, length)
            : null;
    }

    public override string ToString() => $"{Address}/{PrefixLength}";
}

/// <summary>The largest packet the tunnel carries, in bytes.</summary>
public sealed record PacketSize
{
    /// <summary>The smallest MTU IPv6 permits on a link.</summary>
    public const int Minimum = 1280;

    /// <summary>Above this is outside what any supported physical path carries.</summary>
    public const int Maximum = 9000;

    /// <summary>
    /// Not a const: it interpolates the bounds above, so the sentence and the
    /// check can never disagree about what the range is.
    /// </summary>
    public static readonly string Requirement =
        $"The packet size must be a number from {Minimum} to {Maximum} bytes.";

    private PacketSize(int value) => Value = value;

    public int Value { get; }

    /// <summary>
    /// Digits, and nothing else.
    /// </summary>
    /// <remarks>
    /// Unlike DNS, MTU has one canonical spelling, so accepting separators
    /// would add ambiguity without accommodating real input.
    /// </remarks>
    public static PacketSize? TryParse(string? raw) =>
        int.TryParse(raw.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value is >= Minimum and <= Maximum
            ? new PacketSize(value)
            : null;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The DNS servers the tunnel installs. Empty is a real answer: keep whatever
/// Windows already has.
/// </summary>
public sealed record DnsServers
{
    public const string Requirement =
        "Enter IP addresses separated by commas or spaces, or leave this empty "
        + "to keep the DNS servers Windows already uses.";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private DnsServers(ImmutableArray<IPAddress> value) => Value = value;

    public static DnsServers Empty { get; } = new([]);

    public ImmutableArray<IPAddress> Value { get; }

    /// <summary>
    /// Accepts common separators because DNS lists have no single pasted
    /// spelling; unlike <see cref="PacketSize"/>, this variance is real.
    /// </summary>
    public static DnsServers? TryParse(string? raw)
    {
        var entries = (raw ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parsed = ImmutableArray.CreateBuilder<IPAddress>(entries.Length);

        foreach (var entry in entries)
        {
            if (!IPAddress.TryParse(entry, out var address))
            {
                return null;
            }

            parsed.Add(address);
        }

        return entries.Length == 0 ? Empty : new DnsServers(parsed.MoveToImmutable());
    }

    /// <summary>The one spelling the service is ever sent.</summary>
    public override string ToString() => string.Join(' ', Value);

    /// <summary>
    /// Structural, written out rather than synthesized.
    /// </summary>
    /// <remarks>
    /// <see cref="ImmutableArray{T}"/> compares its backing array by reference,
    /// so records need element equality to compare server lists by value.
    /// </remarks>
    public bool Equals(DnsServers? other) =>
        other is not null && Value.SequenceEqual(other.Value);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var address in Value)
        {
            hash.Add(address);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A configuration that has passed the boundary. Every field is a value that
/// could not have been constructed without being checked.
/// </summary>
public sealed record ValidatedConfiguration(
    AdapterName Adapter,
    TunnelAddress Address,
    PacketSize PacketSize,
    DnsServers Dns,
    RouteMode Routes,
    EgressPolicy Egress)
{
    /// <summary>Back to the editable text form, in canonical spelling.</summary>
    public ConfigurationDraft ToDraft() => new(
        AdapterName: Adapter.Value,
        InterfaceAddress: Address.ToString(),
        Mtu: PacketSize.ToString(),
        DnsServers: Dns.ToString(),
        Routes: Routes,
        Egress: Egress);
}

/// <summary>The result of parsing a draft, closed.</summary>
public abstract record ConfigurationParse
{
    private ConfigurationParse() { }

    public sealed record Valid(ValidatedConfiguration Configuration) : ConfigurationParse;

    public sealed record Invalid(IReadOnlyDictionary<ConfigField, string> Errors) : ConfigurationParse;

    public TResult Match<TResult>(
        Func<Valid, TResult> valid,
        Func<Invalid, TResult> invalid) => this switch
        {
            Valid v => valid(v),
            Invalid i => invalid(i),
            _ => throw Unreachable.Value(this),
        };
}

/// <summary>The editable fields, closed. Also the wire keys the service uses.</summary>
public enum ConfigField
{
    Adapter,
    Address,
    Mtu,
    Dns,
}

/// <summary>
/// The single boundary between typed text and a trusted configuration.
/// </summary>
public static class ConfigurationParser
{
    public static readonly ImmutableArray<ConfigField> AllFields =
        [ConfigField.Adapter, ConfigField.Address, ConfigField.Mtu, ConfigField.Dns];

    /// <summary>
    /// The message for one field, or null when that field parses. The view
    /// model uses this for per-field feedback, so a field can never report
    /// something the whole-form parse would disagree with.
    /// </summary>
    public static string? Validate(ConfigField field, ConfigurationDraft draft) => field switch
    {
        ConfigField.Adapter => AdapterName.TryParse(draft.AdapterName) is null
            ? AdapterName.Requirement
            : null,
        ConfigField.Address => TunnelAddress.TryParse(draft.InterfaceAddress) is null
            ? TunnelAddress.Requirement
            : null,
        ConfigField.Mtu => PacketSize.TryParse(draft.Mtu) is null
            ? PacketSize.Requirement
            : null,
        ConfigField.Dns => DnsServers.TryParse(draft.DnsServers) is null
            ? DnsServers.Requirement
            : null,
        _ => throw Unreachable.Value(field),
    };

    /// <summary>Parses the whole draft, in one pass over the four fields.</summary>
    public static ConfigurationParse Parse(ConfigurationDraft draft)
    {
        // Every field read once, before anything is decided. The short-circuit
        // conjunction this replaces stopped at the first failure and then had
        // to re-parse all four through Validate to report them, so a rejected
        // form parsed twice.
        var adapter = AdapterName.TryParse(draft.AdapterName);
        var address = TunnelAddress.TryParse(draft.InterfaceAddress);
        var packetSize = PacketSize.TryParse(draft.Mtu);
        var dns = DnsServers.TryParse(draft.DnsServers);

        if (adapter is not null && address is not null && packetSize is not null && dns is not null)
        {
            return new ConfigurationParse.Valid(
                new ValidatedConfiguration(adapter, address, packetSize, dns, draft.Routes, draft.Egress));
        }

        // Accumulated rather than fail-fast: a person fixing a form wants to
        // see all of it at once.
        var errors = new Dictionary<ConfigField, string>(AllFields.Length);

        if (adapter is null) { errors[ConfigField.Adapter] = AdapterName.Requirement; }
        if (address is null) { errors[ConfigField.Address] = TunnelAddress.Requirement; }
        if (packetSize is null) { errors[ConfigField.Mtu] = PacketSize.Requirement; }
        if (dns is null) { errors[ConfigField.Dns] = DnsServers.Requirement; }

        return new ConfigurationParse.Invalid(errors);
    }
}

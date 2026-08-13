using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Boreas.Ui.Contracts;

/// <summary>
/// The refined values a validated configuration is built from.
/// </summary>
/// <remarks>
/// Each type here has a private constructor and one <c>TryParse</c>. There is
/// no other way to make one, so holding an <see cref="AdapterName"/> is proof
/// that the text was non-empty, and holding a <see cref="PacketSize"/> is
/// proof that the number is one a link can actually carry. The check happens
/// once, at the boundary, and every function downstream is total.
///
/// Each type also owns the sentence shown when parsing fails. That keeps the
/// rule and its explanation in one place: previously the view model restated
/// both, and the two could drift.
///
/// These are records rather than structs on purpose. A struct is always
/// default-constructible, so <c>default(PacketSize)</c> would be a zero-byte
/// packet size that never passed a check. A record class cannot be forged.
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

        // One split, one parse each. Liberal about surrounding whitespace,
        // strict about the shape, because the shape is what carries meaning.
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

    public static PacketSize? TryParse(string? raw)
    {
        // Accepts "1,420", " 1420 " and "1420": a grouping separator is how
        // people write numbers, not a mistake for them to correct.
        var digits = new string((raw ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
               && value is >= Minimum and <= Maximum
            ? new PacketSize(value)
            : null;
    }

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

    public static DnsServers? TryParse(string? raw)
    {
        var entries = (raw ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (entries.Length == 0)
        {
            return Empty;
        }

        var parsed = ImmutableArray.CreateBuilder<IPAddress>(entries.Length);

        foreach (var entry in entries)
        {
            if (!IPAddress.TryParse(entry, out var address))
            {
                return null;
            }

            parsed.Add(address);
        }

        return new DnsServers(parsed.MoveToImmutable());
    }

    /// <summary>The one spelling the service is ever sent.</summary>
    public override string ToString() => string.Join(' ', Value);

    /// <summary>
    /// Structural, written out rather than synthesized.
    /// </summary>
    /// <remarks>
    /// <see cref="ImmutableArray{T}"/> is a struct wrapping an array, and its
    /// own equality compares that array by reference. A record containing one
    /// therefore inherits reference equality, so two configurations naming the
    /// same servers would compare unequal and every round-trip law over this
    /// type would be false. Comparing the elements is what the type means.
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

    /// <summary>
    /// Parses the whole draft. One pass over the four fields, collecting every
    /// message rather than stopping at the first, because a person fixing a
    /// form wants to see all of it at once.
    /// </summary>
    public static ConfigurationParse Parse(ConfigurationDraft draft)
    {
        if (AdapterName.TryParse(draft.AdapterName) is { } adapter
            && TunnelAddress.TryParse(draft.InterfaceAddress) is { } address
            && PacketSize.TryParse(draft.Mtu) is { } packetSize
            && DnsServers.TryParse(draft.DnsServers) is { } dns)
        {
            return new ConfigurationParse.Valid(
                new ValidatedConfiguration(adapter, address, packetSize, dns, draft.Routes, draft.Egress));
        }

        var errors = new Dictionary<ConfigField, string>(AllFields.Length);

        foreach (var field in AllFields)
        {
            if (Validate(field, draft) is { } message)
            {
                errors[field] = message;
            }
        }

        return new ConfigurationParse.Invalid(errors);
    }
}

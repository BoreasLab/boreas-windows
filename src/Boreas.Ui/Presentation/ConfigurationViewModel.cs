using System.Globalization;
using System.Net;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The network configuration form.
/// </summary>
/// <remarks>
/// Three rules shape it.
///
/// Parse, do not reject. An address pasted with surrounding whitespace, an MTU
/// typed as "1,420", DNS servers separated by commas or spaces or newlines:
/// all of these are what the user meant, and normalising them is the system's
/// job rather than theirs.
///
/// Validate at the right moment. On blur for a finished field, on submit for
/// the form, and once a field has errored, on every keystroke so the error
/// clears the moment it stops being true.
///
/// Never lose entry. A rejection from the service leaves every field exactly
/// as typed, with the service's message beside the field that caused it.
/// </remarks>
public sealed class ConfigurationViewModel : ObservableObject
{
    private static readonly ConfigField[] AllFields =
        [ConfigField.Adapter, ConfigField.Address, ConfigField.Mtu, ConfigField.Dns];

    private readonly IControlChannel _channel;
    private readonly Dictionary<ConfigField, string> _errors = [];
    private readonly HashSet<ConfigField> _touched = [];

    private string _adapterName = string.Empty;
    private string _interfaceAddress = string.Empty;
    private string _mtu = string.Empty;
    private string _dnsServers = string.Empty;
    private RouteMode _routes = RouteMode.Default;
    private EgressPolicy _egress = EgressPolicy.Direct;
    private ConfigurationOutcome? _outcome;
    private bool _isLoaded;

    public ConfigurationViewModel(IControlChannel channel)
    {
        _channel = channel;
        Apply = new AsyncCommand(ApplyAsync);
        Revert = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand Apply { get; }

    public AsyncCommand Revert { get; }

    public string AdapterName
    {
        get => _adapterName;
        set { if (Set(ref _adapterName, value)) { Revalidate(ConfigField.Adapter); } }
    }

    public string InterfaceAddress
    {
        get => _interfaceAddress;
        set { if (Set(ref _interfaceAddress, value)) { Revalidate(ConfigField.Address); } }
    }

    public string Mtu
    {
        get => _mtu;
        set { if (Set(ref _mtu, value)) { Revalidate(ConfigField.Mtu); } }
    }

    public string DnsServers
    {
        get => _dnsServers;
        set { if (Set(ref _dnsServers, value)) { Revalidate(ConfigField.Dns); } }
    }

    public RouteMode Routes
    {
        get => _routes;
        set
        {
            if (Set(ref _routes, value))
            {
                Raise(nameof(RouteIndex));
            }
        }
    }

    public EgressPolicy Egress
    {
        get => _egress;
        set
        {
            if (Set(ref _egress, value))
            {
                Raise(nameof(EgressIndex));
            }
        }
    }

    /// <summary>
    /// The radio group's index, mapped both ways over the closed enum so the
    /// order the buttons appear in is stated once rather than assumed.
    /// </summary>
    public int RouteIndex
    {
        get => Routes switch
        {
            RouteMode.Default => 0,
            RouteMode.Selected => 1,
            _ => throw Unreachable.Value(Routes),
        };
        set => Routes = value == 1 ? RouteMode.Selected : RouteMode.Default;
    }

    public int EgressIndex
    {
        get => Egress switch
        {
            EgressPolicy.Direct => 0,
            EgressPolicy.Relay => 1,
            _ => throw Unreachable.Value(Egress),
        };
        set => Egress = value == 1 ? EgressPolicy.Relay : EgressPolicy.Direct;
    }

    /// <summary>
    /// The result of the last apply, or null before one has been made. Shown
    /// where the user pressed the button, not as a corner toast.
    /// </summary>
    public ConfigurationOutcome? Outcome
    {
        get => _outcome;
        private set
        {
            if (Set(ref _outcome, value))
            {
                Raise(nameof(OutcomeMessage));
                Raise(nameof(OutcomeTone));
                Raise(nameof(HasOutcome));
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => Set(ref _isLoaded, value);
    }

    public string? AdapterError => _errors.GetValueOrDefault(ConfigField.Adapter);

    public string? AddressError => _errors.GetValueOrDefault(ConfigField.Address);

    public string? MtuError => _errors.GetValueOrDefault(ConfigField.Mtu);

    public string? DnsError => _errors.GetValueOrDefault(ConfigField.Dns);

    public bool HasAdapterError => AdapterError is not null;

    public bool HasAddressError => AddressError is not null;

    public bool HasMtuError => MtuError is not null;

    public bool HasDnsError => DnsError is not null;

    /// <summary>
    /// The result of the last apply, in the user's words, or null before one
    /// has been made.
    /// </summary>
    public string? OutcomeMessage => Outcome?.Match(
        applied: static _ => "Saved. The new settings are in effect.",
        restartRequired: static _ => "Saved. The tunnel keeps its current settings until you "
                                   + "stop and start it.",
        rejected: static r => r.Error.Summary + " " + r.Error.NextStep);

    public StatusTone OutcomeTone => Outcome?.Match(
        applied: static _ => StatusTone.Active,
        restartRequired: static _ => StatusTone.Caution,
        rejected: static _ => StatusTone.Fault) ?? StatusTone.Idle;

    public bool HasOutcome => Outcome is not null;

    /// <summary>
    /// The first field with an error, so focus can be moved there on submit.
    /// </summary>
    public ConfigField? FirstErrorField => AllFields
        .Cast<ConfigField?>()
        .FirstOrDefault(field => _errors.ContainsKey(field!.Value));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var draft = await _channel.ReadConfigurationAsync(cancellationToken);
        _adapterName = draft.AdapterName;
        _interfaceAddress = draft.InterfaceAddress;
        _mtu = draft.Mtu;
        _dnsServers = draft.DnsServers;
        _routes = draft.Routes;
        _egress = draft.Egress;
        _errors.Clear();
        _touched.Clear();
        Outcome = null;
        IsLoaded = true;
        RaiseAll();
    }

    /// <summary>Called when a field loses focus, which is when it is finished.</summary>
    public void MarkTouched(ConfigField field)
    {
        _touched.Add(field);
        Validate(field);
        RaiseMessages();
    }

    private void RaiseMessages()
    {
        Raise(nameof(AdapterError));
        Raise(nameof(AddressError));
        Raise(nameof(MtuError));
        Raise(nameof(DnsError));
        Raise(nameof(HasAdapterError));
        Raise(nameof(HasAddressError));
        Raise(nameof(HasMtuError));
        Raise(nameof(HasDnsError));
    }

    private void Revalidate(ConfigField field)
    {
        // Only speak up about a field the user has already finished once.
        // Telling someone their half-typed address is invalid is noise.
        if (_touched.Contains(field))
        {
            Validate(field);
            RaiseMessages();
        }
    }

    private void Validate(ConfigField field)
    {
        var message = field switch
        {
            ConfigField.Adapter => ValidateAdapter(_adapterName),
            ConfigField.Address => ValidateAddress(_interfaceAddress),
            ConfigField.Mtu => ValidateMtu(_mtu),
            ConfigField.Dns => ValidateDns(_dnsServers),
            _ => throw Unreachable.Value(field),
        };

        if (message is null)
        {
            _errors.Remove(field);
        }
        else
        {
            _errors[field] = message;
        }
    }

    private void ValidateAll()
    {
        foreach (var field in AllFields)
        {
            _touched.Add(field);
            Validate(field);
        }

        RaiseMessages();
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        ValidateAll();
        if (_errors.Count > 0)
        {
            Outcome = null;
            return;
        }

        var outcome = await _channel.ApplyConfigurationAsync(
            new ConfigurationDraft(
                AdapterName: _adapterName.Trim(),
                InterfaceAddress: _interfaceAddress.Trim(),
                Mtu: NormaliseMtu(_mtu),
                DnsServers: NormaliseDns(_dnsServers),
                Routes: _routes,
                Egress: _egress),
            cancellationToken);

        // The service is the authority on validity. Its field messages replace
        // whatever this side thought, and every typed value stays put.
        outcome.Match<object?>(
            applied: _ => null,
            restartRequired: _ => null,
            rejected: r =>
            {
                foreach (var (name, message) in r.FieldErrors)
                {
                    // Wire names the service does not share with this client
                    // are ignored rather than guessed at.
                    if (!Enum.TryParse<ConfigField>(name, ignoreCase: true, out var field))
                    {
                        continue;
                    }

                    _touched.Add(field);
                    _errors[field] = message;
                }

                return null;
            });

        Outcome = outcome;
        RaiseMessages();
    }

    private static string? ValidateAdapter(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Give the adapter a name. It appears in Windows network settings."
            : null;

    private static string? ValidateAddress(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "Enter the address this device takes inside the tunnel, with its prefix "
                 + "length, for example 10.7.0.2/24.";
        }

        var parts = trimmed.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return "Write the address as an IP address, a slash, and a prefix length, "
                 + "for example 10.7.0.2/24 or fd00::2/64.";
        }

        var maximumPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
               && prefix >= 0 && prefix <= maximumPrefix
            ? null
            : $"The prefix length after the slash must be a number from 0 to {maximumPrefix}.";
    }

    private static string? ValidateMtu(string value)
    {
        var normalised = NormaliseMtu(value);
        if (normalised.Length == 0)
        {
            return "Enter the maximum packet size for the tunnel, in bytes.";
        }

        // 1280 is the smallest MTU IPv6 permits on a link; above 9000 is
        // outside what any supported physical path carries.
        return int.TryParse(normalised, NumberStyles.None, CultureInfo.InvariantCulture, out var mtu)
               && mtu is >= 1280 and <= 9000
            ? null
            : "The packet size must be a number from 1280 to 9000 bytes.";
    }

    private static string? ValidateDns(string value)
    {
        var normalised = NormaliseDns(value);
        if (normalised.Length == 0)
        {
            return null; // Optional: the tunnel can leave DNS as Windows has it.
        }

        var bad = normalised
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(entry => !IPAddress.TryParse(entry, out _));

        return bad is null
            ? null
            : $"“{bad}” is not an IP address. Separate servers with commas or spaces.";
    }

    /// <summary>Accepts "1,420", " 1420 " and "1420".</summary>
    private static string NormaliseMtu(string value) =>
        new(value.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Accepts commas, semicolons, spaces and newlines as separators.</summary>
    private static string NormaliseDns(string value) => string.Join(
        ' ',
        value.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void RaiseAll()
    {
        Raise(nameof(AdapterName));
        Raise(nameof(InterfaceAddress));
        Raise(nameof(Mtu));
        Raise(nameof(DnsServers));
        Raise(nameof(Routes));
        Raise(nameof(Egress));
        Raise(nameof(RouteIndex));
        Raise(nameof(EgressIndex));
        RaiseMessages();
    }
}

/// <summary>
/// The editable fields, closed. The names double as the wire keys the service
/// uses in <see cref="ConfigurationOutcome.Rejected.FieldErrors"/>.
/// </summary>
public enum ConfigField
{
    Adapter,
    Address,
    Mtu,
    Dns,
}

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
    private readonly IControlChannel _channel;
    private readonly Dictionary<ConfigField, string> _errors = [];
    private readonly HashSet<ConfigField> _touched = [];

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
        get;
        set { if (Set(ref field, value)) { Revalidate(ConfigField.Adapter); } }
    } = string.Empty;

    public string InterfaceAddress
    {
        get;
        set { if (Set(ref field, value)) { Revalidate(ConfigField.Address); } }
    } = string.Empty;

    public string Mtu
    {
        get;
        set { if (Set(ref field, value)) { Revalidate(ConfigField.Mtu); } }
    } = string.Empty;

    public string DnsServers
    {
        get;
        set { if (Set(ref field, value)) { Revalidate(ConfigField.Dns); } }
    } = string.Empty;

    public RouteMode Routes
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(RouteIndex));
            }
        }
    } = RouteMode.Default;

    public EgressPolicy Egress
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(EgressIndex));
            }
        }
    } = EgressPolicy.Direct;

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
        get;
        private set
        {
            if (Set(ref field, value))
            {
                Raise(nameof(OutcomeMessage));
                Raise(nameof(OutcomeTone));
                Raise(nameof(HasOutcome));
            }
        }
    }

    public bool IsLoaded
    {
        get;
        private set => Set(ref field, value);
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
    /// <remarks>
    /// The lambda parameter is deliberately not called <c>field</c>. Inside a
    /// property accessor that identifier is now the contextual keyword for the
    /// synthesized backing field, and shadowing it here would either fail to
    /// compile or silently bind to the wrong thing.
    /// </remarks>
    public ConfigField? FirstErrorField => ConfigurationParser.AllFields
        .Cast<ConfigField?>()
        .FirstOrDefault(candidate => _errors.ContainsKey(candidate!.Value));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var draft = await _channel.ReadConfigurationAsync(cancellationToken);

        // Cleared before the assignments, not after: every setter revalidates,
        // and a field nobody has touched yet must not acquire a message just
        // because the service supplied its value.
        _errors.Clear();
        _touched.Clear();

        AdapterName = draft.AdapterName;
        InterfaceAddress = draft.InterfaceAddress;
        Mtu = draft.Mtu;
        DnsServers = draft.DnsServers;
        Routes = draft.Routes;
        Egress = draft.Egress;

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

    /// <summary>The form as text, ready for the parser.</summary>
    private ConfigurationDraft CurrentDraft => new(
        AdapterName: AdapterName,
        InterfaceAddress: InterfaceAddress,
        Mtu: Mtu,
        DnsServers: DnsServers,
        Routes: Routes,
        Egress: Egress);

    private void Validate(ConfigField field)
    {
        // One source of truth. The rule and its sentence live with the refined
        // type, so a field can never report something the whole-form parse
        // would disagree with.
        var message = ConfigurationParser.Validate(field, CurrentDraft);

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
        foreach (var field in ConfigurationParser.AllFields)
        {
            _touched.Add(field);
            Validate(field);
        }

        RaiseMessages();
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Parsed once. Either this yields a value the service can be handed,
        // or it yields the messages, and there is no third case and no way to
        // send the unparsed text by mistake.
        var parsed = ConfigurationParser.Parse(CurrentDraft);

        if (parsed is ConfigurationParse.Invalid invalid)
        {
            foreach (var (field, message) in invalid.Errors)
            {
                _touched.Add(field);
                _errors[field] = message;
            }

            Outcome = null;
            RaiseMessages();
            return;
        }

        var configuration = ((ConfigurationParse.Valid)parsed).Configuration;
        var outcome = await _channel.ApplyConfigurationAsync(configuration, cancellationToken);

        // The service is the authority on validity. Its field messages replace
        // whatever this side thought, and every typed value stays put.
        outcome.Match<object?>(
            applied: _ => null,
            restartRequired: _ => null,
            rejected: r =>
            {
                foreach (var (field, message) in r.FieldErrors)
                {
                    _touched.Add(field);
                    _errors[field] = message;
                }

                return null;
            });

        Outcome = outcome;
        RaiseMessages();
    }

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


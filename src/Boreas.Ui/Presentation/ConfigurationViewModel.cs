using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The network configuration form.
/// </summary>
/// <remarks>
/// Accept real pasted variance, but keep single-spelling fields strict. Validate
/// on blur, then on each edit after an error; service rejection preserves text
/// and maps messages to their fields.
/// </remarks>
public sealed class ConfigurationViewModel : ObservableObject
{
    private readonly IControlChannel _channel;

    /// <summary>
    /// One state per field, positioned by <see cref="ConfigurationParser.AllFields"/>.
    /// </summary>
    /// <remarks>
    /// The state combines touched status and validation result, so an untouched
    /// field cannot carry a message. The dense field enum indexes the array
    /// directly; <see cref="PresentationLaws"/> guards that ordering.
    /// </remarks>
    private readonly FieldState[] _fields = new FieldState[ConfigurationParser.AllFields.Length];

    public ConfigurationViewModel(IControlChannel channel)
    {
        _channel = channel;
        Forget();
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

    /// <summary>The route values in selector order.</summary>
    public static readonly RouteMode[] RouteOrder = [RouteMode.Default, RouteMode.Selected];

    public static readonly EgressPolicy[] EgressOrder = [EgressPolicy.Direct, EgressPolicy.WireGuard];

    public int RouteIndex
    {
        get => Array.IndexOf(RouteOrder, Routes);
        set => Routes = RouteOrder[Math.Clamp(value, 0, RouteOrder.Length - 1)];
    }

    public int EgressIndex
    {
        get => Array.IndexOf(EgressOrder, Egress);
        set => Egress = EgressOrder[Math.Clamp(value, 0, EgressOrder.Length - 1)];
    }

    /// <summary>
    /// Last apply result, shown beside the form.
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

    public string? AdapterError => MessageFor(ConfigField.Adapter);

    public string? AddressError => MessageFor(ConfigField.Address);

    public string? MtuError => MessageFor(ConfigField.Mtu);

    public string? DnsError => MessageFor(ConfigField.Dns);

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
    /// First invalid field in form order, for submit focus.
    /// </summary>
    /// <remarks>
    /// The state array and <see cref="ConfigurationParser.AllFields"/> share
    /// form order, so the first rejected position identifies its field.
    /// </remarks>
    public ConfigField? FirstErrorField =>
        Array.FindIndex(_fields, static state => state is FieldState.Rejected) switch
        {
            < 0 => null,
            var position => ConfigurationParser.AllFields[position],
        };

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var draft = await _channel.ReadConfigurationAsync(cancellationToken);

        // Clear before assignments so loaded values are not marked touched by
        // setter revalidation.
        Forget();

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

    /// <summary>Marks a field finished when it loses focus.</summary>
    public void MarkTouched(ConfigField field)
    {
        Finish(field);
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
        // Keep untouched fields quiet while they are being typed.
        if (_fields[Position(field)] is not FieldState.Untouched)
        {
            Finish(field);
            RaiseMessages();
        }
    }

    private ConfigurationDraft CurrentDraft => new(
        AdapterName: AdapterName,
        InterfaceAddress: InterfaceAddress,
        Mtu: Mtu,
        DnsServers: DnsServers,
        Routes: Routes,
        Egress: Egress);

    /// <summary>
    /// Records the current validation result for a finished field.
    /// </summary>
    /// <remarks>
    /// Validation rules and messages remain owned by the refined types.
    /// </remarks>
    private void Finish(ConfigField field) =>
        _fields[Position(field)] = ConfigurationParser.Validate(field, CurrentDraft) is { } message
            ? new FieldState.Rejected(message)
            : FieldState.Accepted.Instance;

    /// <summary>
    /// Places parser or service messages on their fields.
    /// </summary>
    /// <remarks>
    /// A rejection state also records that the field was finished.
    /// </remarks>
    private void Reject(IReadOnlyDictionary<ConfigField, string> errors)
    {
        foreach (var (field, message) in errors)
        {
            _fields[Position(field)] = new FieldState.Rejected(message);
        }
    }

    /// <summary>Resets every field to untouched.</summary>
    private void Forget() => Array.Fill(_fields, FieldState.Untouched.Instance);

    private string? MessageFor(ConfigField field) =>
        _fields[Position(field)] is FieldState.Rejected rejected ? rejected.Message : null;

    /// <summary>
    /// Returns the array index for a field.
    /// </summary>
    /// <remarks>
    /// Direct indexing depends on dense enum values; the law test guards the
    /// ordering, and this check keeps an out-of-domain value explicit.
    /// </remarks>
    private static int Position(ConfigField field) =>
        (uint)field < (uint)ConfigurationParser.AllFields.Length
            ? (int)field
            : throw Unreachable.Value(field);

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Parse once so only validated values reach the service.
        var parsed = ConfigurationParser.Parse(CurrentDraft);

        if (parsed is ConfigurationParse.Invalid invalid)
        {
            Reject(invalid.Errors);

            Outcome = null;
            RaiseMessages();
            return;
        }

        var configuration = ((ConfigurationParse.Valid)parsed).Configuration;
        var outcome = await _channel.ApplyConfigurationAsync(configuration, cancellationToken);

        // Service messages replace local validation results; typed values stay.
        outcome.Match<object?>(
            applied: static _ => null,
            restartRequired: static _ => null,
            rejected: r =>
            {
                Reject(r.FieldErrors);
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

    /// <summary>
    /// Everything the form can have to say about one field, closed.
    /// </summary>
    /// <remarks>
    /// Three states distinguish untouched, valid, and invalid fields; editing
    /// state stays private to the view model rather than the pipe contract.
    /// </remarks>
    private abstract record FieldState
    {
        private FieldState() { }

        /// <summary>The user has not finished with it, so the form says nothing.</summary>
        public sealed record Untouched : FieldState
        {
            /// <summary>Shared because the state carries no data.</summary>
            public static readonly Untouched Instance = new();
        }

        /// <summary>Finished and valid.</summary>
        public sealed record Accepted : FieldState
        {
            public static readonly Accepted Instance = new();
        }

        /// <summary>
        /// Finished, and what it holds does not parse, carrying the one
        /// sentence that says why.
        /// </summary>
        public sealed record Rejected(string Message) : FieldState;
    }
}


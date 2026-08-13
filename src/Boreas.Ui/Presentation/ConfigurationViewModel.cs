using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The network configuration form.
/// </summary>
/// <remarks>
/// Three rules shape it.
///
/// Parse, do not reject, where the variance is real. An address pasted with
/// surrounding whitespace and DNS servers separated by commas or spaces or
/// newlines are what the user meant, and normalising them is the system's job
/// rather than theirs. Where a field has one spelling, the format is narrowed
/// instead: a parser written to absorb variance nobody produces is complexity
/// bought with nothing.
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

    /// <summary>
    /// One state per field, positioned by <see cref="ConfigurationParser.AllFields"/>.
    /// </summary>
    /// <remarks>
    /// This was two collections: a dictionary of messages and a set of
    /// finished fields. Nothing held them in agreement, so a message on a
    /// field the user had never finished was representable and would have
    /// shown as an error under a box nobody had left yet. It cannot be
    /// written now, because carrying a message and being finished are the
    /// same fact in <see cref="FieldState.Rejected"/> rather than two facts
    /// in two places.
    ///
    /// An array over the four fields rather than a hash structure. The domain
    /// is closed and dense, so the field's own value is already a perfect hash
    /// of itself: <see cref="Position"/> indexes with it directly, which is
    /// O(1) with no hashing, no bucket, and no allocation, where the dictionary
    /// paid for a hash to rediscover a number it had been handed. Every field
    /// also always has a state, so "absent from the dictionary" stops being a
    /// third way to say untouched.
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

    /// <summary>The order the radio buttons appear in, stated once each.</summary>
    /// <remarks>
    /// Read in both directions, so the mapping out and the mapping back cannot
    /// disagree. <c>PresentationLaws</c> asserts each array names every value
    /// of its enum, which is what rules out the -1
    /// <see cref="Array.IndexOf{T}(T[], T)"/> would otherwise return, and what
    /// the discarded switch arms were only pretending to do.
    /// </remarks>
    public static readonly RouteMode[] RouteOrder = [RouteMode.Default, RouteMode.Selected];

    public static readonly EgressPolicy[] EgressOrder = [EgressPolicy.Direct, EgressPolicy.Relay];

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
    /// The first field with an error, so focus can be moved there on submit.
    /// </summary>
    /// <remarks>
    /// The states array is positioned by <see cref="ConfigurationParser.AllFields"/>,
    /// so one array read backwards turns a position into the field that holds
    /// it, exactly as the selector orders elsewhere in this file do. That is
    /// what removes the <c>Cast&lt;ConfigField?&gt;</c> this used to need: the
    /// cast existed only so <c>FirstOrDefault</c> would answer null instead of
    /// <see cref="ConfigField.Adapter"/> when nothing matched, and it boxed
    /// every field and allocated three iterators to say so.
    ///
    /// A scan, and the right one: "first in form order" is a question about
    /// the order, so no index answers it faster than reading the order. O(4)
    /// over four contiguous references, no allocation, on a submit.
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

        // Cleared before the assignments, not after: every setter revalidates,
        // and a field nobody has touched yet must not acquire a message just
        // because the service supplied its value.
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

    /// <summary>Called when a field loses focus, which is when it is finished.</summary>
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
        // Only speak up about a field the user has already finished once.
        // Telling someone their half-typed address is invalid is noise.
        if (_fields[Position(field)] is not FieldState.Untouched)
        {
            Finish(field);
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

    /// <summary>
    /// Records a field as finished and stores the verdict on what it now holds.
    /// </summary>
    /// <remarks>
    /// One source of truth. The rule and its sentence live with the refined
    /// type, so a field can never report something the whole-form parse would
    /// disagree with.
    /// </remarks>
    private void Finish(ConfigField field) =>
        _fields[Position(field)] = ConfigurationParser.Validate(field, CurrentDraft) is { } message
            ? new FieldState.Rejected(message)
            : FieldState.Accepted.Instance;

    /// <summary>
    /// Places service or parser messages on their fields.
    /// </summary>
    /// <remarks>
    /// No second collection to mark the same fields finished: a message is only
    /// carried by <see cref="FieldState.Rejected"/>, and a rejected field is a
    /// finished one.
    /// </remarks>
    private void Reject(IReadOnlyDictionary<ConfigField, string> errors)
    {
        foreach (var (field, message) in errors)
        {
            _fields[Position(field)] = new FieldState.Rejected(message);
        }
    }

    /// <summary>Every field back to "the user has not finished with it".</summary>
    private void Forget() => Array.Fill(_fields, FieldState.Untouched.Instance);

    private string? MessageFor(ConfigField field) =>
        _fields[Position(field)] is FieldState.Rejected rejected ? rejected.Message : null;

    /// <summary>
    /// Where a field's state lives: the field's own value, used directly as
    /// the array index.
    /// </summary>
    /// <remarks>
    /// O(1) and branch-light, which is the point of an array over the
    /// dictionary this replaced: a dense enum is already a perfect hash of
    /// itself, so paying for a hash, or scanning
    /// <see cref="ConfigurationParser.AllFields"/> for the value, would be
    /// buying back what the type already gives away.
    ///
    /// It rests on one law: the field values are the dense positions
    /// <see cref="ConfigurationParser.AllFields"/> names, in that order.
    /// <c>PresentationLaws</c> asserts it, because a hand-assigned enum value
    /// would otherwise put one field's message in another field's slot, or
    /// past the end of the array. The bounds test keeps a value from outside
    /// the domain loud in the codebase's own vocabulary rather than as a raw
    /// index fault.
    /// </remarks>
    private static int Position(ConfigField field) =>
        (uint)field < (uint)ConfigurationParser.AllFields.Length
            ? (int)field
            : throw Unreachable.Value(field);

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Parsed once. Either this yields a value the service can be handed,
        // or it yields the messages, and there is no third case and no way to
        // send the unparsed text by mistake.
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

        // The service is the authority on validity. Its field messages replace
        // whatever this side thought, and every typed value stays put.
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
    /// Three states, not a flag beside a nullable string. "Finished and it
    /// parses" and "not finished yet" both show no message and are not the
    /// same thing: the first revalidates on every keystroke, the second stays
    /// silent until the user leaves the box. A boolean pair would have made
    /// them indistinguishable in one direction and contradictory in the other.
    ///
    /// Private to the view model because it is editing state and not a
    /// contract: nothing below this class, and nothing on the pipe, has an
    /// opinion about whether a text box has been left yet.
    /// </remarks>
    private abstract record FieldState
    {
        private FieldState() { }

        /// <summary>The user has not finished with it, so the form says nothing.</summary>
        public sealed record Untouched : FieldState
        {
            /// <summary>
            /// Shared. The state carries nothing, so one value serves every
            /// field and every reset rather than allocating a record per
            /// keystroke to say the same nothing.
            /// </summary>
            public static readonly Untouched Instance = new();
        }

        /// <summary>Finished, and what it holds parses.</summary>
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


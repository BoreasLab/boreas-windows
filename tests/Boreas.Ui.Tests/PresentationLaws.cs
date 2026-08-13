using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws of <see cref="StatusPresentation.For"/>, the function that decides what
/// this application tells a person about their network.
/// </summary>
/// <remarks>
/// The domain is a product of two closed sums, and it is small enough to
/// enumerate rather than sample: five channel states times seven service
/// states, which is 35 pairs. Every law below is checked against the whole
/// domain, so these are proofs over the finite model rather than spot checks.
/// </remarks>
public sealed class PresentationLaws
{
    private static readonly TypedError AnyError = new(
        Code: "test.error",
        Summary: "Something failed.",
        NextStep: "Try it again.");

    private static readonly SessionIdentity AnySession = new("session-under-test");

    /// <summary>One representative of each channel variant.</summary>
    public static IReadOnlyList<ControlChannelState> Channels { get; } =
    [
        new ControlChannelState.Connecting(),
        new ControlChannelState.Connected(ProtocolVersion: 1),
        new ControlChannelState.Unavailable(AnyError),
        new ControlChannelState.Unauthorized(),
        new ControlChannelState.VersionMismatch(ServiceVersion: ControlProtocol.Version + 1),
    ];

    /// <summary>
    /// One representative of each service variant, with Running split by
    /// bypass because the two carry different claims, and Failed split by
    /// recoverability because it changes the offered action.
    /// </summary>
    public static IReadOnlyList<ServiceState> Services { get; } =
    [
        new ServiceState.Stopped(),
        new ServiceState.Starting(),
        Running(new EgressBypass.Bound("Ethernet")),
        Running(new EgressBypass.Degraded(AnyError)),
        new ServiceState.Stopping(AnySession),
        new ServiceState.Failed(ControlOperation.Start, AnyError, Recoverable: true),
        new ServiceState.Failed(ControlOperation.Start, AnyError, Recoverable: false),
    ];

    public static TheoryData<int, int> Domain()
    {
        var data = new TheoryData<int, int>();

        for (var channel = 0; channel < Channels.Count; channel++)
        {
            for (var service = 0; service < Services.Count; service++)
            {
                data.Add(channel, service);
            }
        }

        return data;
    }

    private static ServiceState Running(EgressBypass bypass) =>
        new ServiceState.Running(
            AnySession,
            new SessionStatus(
                AdapterName: "Boreas",
                InterfaceAddress: "10.0.0.2/24",
                Mtu: 1420,
                RunningSince: DateTimeOffset.UnixEpoch,
                Counters: default,
                Bypass: bypass));

    /// <summary>
    /// Totality. Every pair produces something a person can read: no blank
    /// headline, no blank sentence, no state that renders as an empty band.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void Every_state_pair_produces_readable_text(int channel, int service)
    {
        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        Assert.False(string.IsNullOrWhiteSpace(presentation.Headline));
        Assert.False(string.IsNullOrWhiteSpace(presentation.Detail));
        Assert.False(string.IsNullOrWhiteSpace(presentation.Glyph));
        Assert.False(string.IsNullOrWhiteSpace(presentation.GlyphLabel));
    }

    /// <summary>
    /// No information by colour alone. The announcement a screen reader hears
    /// has to carry everything the tone conveys, so it must contain both the
    /// headline and the sentence.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void The_announcement_carries_everything_the_tone_does(int channel, int service)
    {
        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        Assert.Contains(presentation.Headline, presentation.Announcement, StringComparison.Ordinal);
        Assert.Contains(presentation.Detail, presentation.Announcement, StringComparison.Ordinal);
    }

    /// <summary>
    /// The safety law. While the channel is not connected the client knows
    /// nothing about the tunnel, so it must not claim protection, must not
    /// claim the tunnel is off, and must not offer to stop something it cannot
    /// see. Breaking this would mean telling someone their traffic is
    /// protected on the strength of a stale reading.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void A_disconnected_channel_makes_no_claim_about_the_tunnel(int channel, int service)
    {
        if (Channels[channel] is ControlChannelState.Connected)
        {
            return;
        }

        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        Assert.DoesNotContain(presentation.Headline, TunnelClaims);
        Assert.NotEqual(PrimaryAction.Stop, presentation.Action);
        Assert.NotEqual(PrimaryAction.Start, presentation.Action);
    }

    private static readonly string[] TunnelClaims = ["Protected", "Off", "Running", "Starting", "Stopping"];

    /// <summary>
    /// The protection law, in both directions. "Protected" appears exactly
    /// when the channel is connected, the session is running, and the bypass
    /// is bound. A running session whose upstream socket may have re-entered
    /// the tunnel is not protected and must not say so.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void Protected_means_connected_running_and_bound(int channel, int service)
    {
        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        var isProtected = Channels[channel] is ControlChannelState.Connected
            && Services[service] is ServiceState.Running { Status.Bypass: EgressBypass.Bound };

        Assert.Equal(isProtected, presentation.Headline == "Protected");
        Assert.Equal(isProtected, presentation.Tone == StatusTone.Active);
    }

    /// <summary>
    /// Action legality. A command is offered only in a state where the service
    /// would accept it, so no button produces a request the service serialises
    /// and then rejects.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void Every_offered_action_is_legal_in_its_state(int channel, int service)
    {
        var presentation = StatusPresentation.For(Channels[channel], Services[service]);
        var connected = Channels[channel] is ControlChannelState.Connected;

        switch (presentation.Action)
        {
            case PrimaryAction.Start:
                Assert.True(connected && Services[service] is ServiceState.Stopped);
                break;
            case PrimaryAction.Stop:
                Assert.True(connected && Services[service] is ServiceState.Running);
                break;
            case PrimaryAction.Retry:
                Assert.True(connected && Services[service] is ServiceState.Failed { Recoverable: true });
                break;
            case PrimaryAction.Reconnect:
                Assert.True(Channels[channel] is ControlChannelState.Unavailable);
                break;
            case PrimaryAction.None:
                break;
        }
    }

    /// <summary>
    /// A transitional state shows progress and offers nothing, because there
    /// is nothing useful to press while the service is mid-transition.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void Transitional_states_show_progress_and_offer_nothing(int channel, int service)
    {
        if (Channels[channel] is not ControlChannelState.Connected)
        {
            return;
        }

        if (Services[service] is not (ServiceState.Starting or ServiceState.Stopping))
        {
            return;
        }

        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        Assert.True(presentation.ShowProgress);
        Assert.Equal(PrimaryAction.None, presentation.Action);
    }

    /// <summary>
    /// An unrecoverable failure offers no retry. A button that cannot work
    /// teaches people to press buttons that do not work.
    /// </summary>
    [Fact]
    public void An_unrecoverable_failure_offers_nothing_to_press()
    {
        var presentation = StatusPresentation.For(
            new ControlChannelState.Connected(1),
            new ServiceState.Failed(ControlOperation.Start, AnyError, Recoverable: false));

        Assert.Equal(PrimaryAction.None, presentation.Action);
    }

    /// <summary>The label is present exactly when there is an action.</summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void A_label_exists_exactly_when_an_action_does(int channel, int service)
    {
        var model = new StatusViewModel(new StubChannel(Channels[channel], Services[service]));

        Assert.Equal(model.HasPrimaryAction, model.PrimaryLabel.Length > 0);
        Assert.Equal(model.HasPrimaryAction, model.Primary.CanExecute(null));
    }

    /// <summary>
    /// Session facts appear only for a running session on a live channel.
    /// Showing an adapter and a byte count next to "No service" would present
    /// a stale reading as current.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void Session_facts_appear_only_for_a_running_session(int channel, int service)
    {
        var model = new StatusViewModel(new StubChannel(Channels[channel], Services[service]));

        var expected = Channels[channel] is ControlChannelState.Connected
            && Services[service] is ServiceState.Running;

        Assert.Equal(expected, model.HasFacts);
        Assert.Equal(expected, model.Facts.Identity.Count > 0);
    }

    /// <summary>
    /// The bypass warning appears exactly when the bypass is degraded, and
    /// never merely because the channel dropped.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void The_bypass_warning_tracks_the_bypass(int channel, int service)
    {
        var model = new StatusViewModel(new StubChannel(Channels[channel], Services[service]));

        var degraded = Services[service] is ServiceState.Running { Status.Bypass: EgressBypass.Degraded };

        Assert.Equal(degraded, model.BypassDegradation is not null);
    }

    /// <summary>
    /// The chip is always populated, and a banner appears only where there is
    /// something to say about the channel.
    /// </summary>
    [Theory]
    [MemberData(nameof(Domain))]
    public void The_channel_chip_is_always_readable(int channel, int service)
    {
        _ = service;
        var presentation = ChannelPresentation.For(Channels[channel]);

        Assert.False(string.IsNullOrWhiteSpace(presentation.ChipLabel));

        var expectsBanner = Channels[channel] is not
            (ControlChannelState.Connected or ControlChannelState.Connecting);

        Assert.Equal(expectsBanner, presentation.Banner is not null);
    }

    /// <summary>
    /// A banner offers an action only where retrying could change the answer.
    /// Authorization and version mismatch will not change until someone
    /// installs or configures something, so neither offers a button.
    /// </summary>
    [Fact]
    public void Only_a_retryable_channel_failure_offers_an_action()
    {
        Assert.NotNull(ChannelPresentation.For(new ControlChannelState.Unavailable(AnyError)).Banner!.ActionLabel);
        Assert.Null(ChannelPresentation.For(new ControlChannelState.Unauthorized()).Banner!.ActionLabel);
        Assert.Null(ChannelPresentation.For(new ControlChannelState.VersionMismatch(ControlProtocol.Version + 1)).Banner!.ActionLabel);
    }

    /// <summary>
    /// Every selector array names every value of its enum.
    /// </summary>
    /// <remarks>
    /// The property that makes those selectors total. Each maps its enum to a
    /// control index with one array read forwards and backwards, which removes
    /// any chance of the two directions disagreeing but leaves one hole:
    /// <see cref="Array.IndexOf{T}(T[], T)"/> answers -1 for a value the array
    /// omits, and a control asked to select -1 shows nothing selected. C# has
    /// no way to require an array to cover an enum, so it is required here. Add
    /// a route mode or an event kind without adding its segment and this fails,
    /// which is the notification the compiler cannot give.
    /// </remarks>
    [Fact]
    public void Every_selector_covers_its_closed_set()
    {
        Assert.Equal(Enum.GetValues<RouteMode>(), ConfigurationViewModel.RouteOrder);
        Assert.Equal(Enum.GetValues<EgressPolicy>(), ConfigurationViewModel.EgressOrder);

        // Not only a selector order. The network form keeps one state per field
        // in an array positioned by this, so a field the array omits would have
        // nowhere to record its message and would throw when the user left it.
        Assert.Equal(Enum.GetValues<ConfigField>(), ConfigurationParser.AllFields.ToArray());

        // And the array is indexed by the field's own value, which is what
        // makes that lookup O(1) rather than a scan. Give a member a
        // hand-assigned value and it lands in another field's slot, or past
        // the end of the array; this is where that stops.
        foreach (var (position, field) in ConfigurationParser.AllFields.Index())
        {
            Assert.Equal(position, (int)field);
        }

        // Null leads the filter segments: "everything" is a choice, so the
        // array is one longer than the enum it covers.
        Assert.Equal(
            Enum.GetValues<ControlEventKind>().Cast<ControlEventKind?>(),
            DiagnosticsViewModel.FilterOrder.Skip(1));
        Assert.Null(DiagnosticsViewModel.FilterOrder[0]);
    }

    /// <summary>
    /// Wire names round-trip, and every field has one.
    /// </summary>
    /// <remarks>
    /// The two directions are separate switches, so this is what keeps them
    /// agreeing. It also fixes the names: changing one without the other, or
    /// adding a field and forgetting its name, fails here rather than at the
    /// pipe, where the symptom would be a service message that silently
    /// attaches to no field.
    /// </remarks>
    [Fact]
    public void Every_configuration_field_round_trips_through_its_wire_name()
    {
        foreach (var expected in Enum.GetValues<ConfigField>())
        {
            Assert.Equal(expected, ConfigField.FromWireName(expected.WireName));
        }

        // Distinct names, so the round trip above is a bijection and not a
        // collapse onto one field.
        Assert.Equal(
            Enum.GetValues<ConfigField>().Length,
            Enum.GetValues<ConfigField>().Select(f => f.WireName).Distinct().Count());
    }

    /// <summary>
    /// A name from a newer service is version skew, not a defect: it parses to
    /// nothing, so the message is shown without being pinned to a field.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("adapterName")]
    [InlineData("something_added_later")]
    public void An_unknown_wire_name_denotes_no_field(string name) =>
        Assert.Null(ConfigField.FromWireName(name));

    [Fact]
    public void An_unknown_wire_name_is_not_invented_from_null() =>
        Assert.Null(ConfigField.FromWireName(null));

    /// <summary>
    /// A version mismatch reports this client's actual version. It used to be
    /// a constructor parameter, so the state could carry a number that was
    /// nobody's.
    /// </summary>
    [Fact]
    public void A_version_mismatch_reports_the_version_this_client_speaks()
    {
        var mismatch = new ControlChannelState.VersionMismatch(ServiceVersion: 99);

        Assert.Equal(ControlProtocol.Version, mismatch.ClientVersion);
        Assert.Equal(99, mismatch.ServiceVersion);

        // And the text the user reads says the same thing, rather than
        // rendering a version from somewhere else.
        var presentation = StatusPresentation.For(mismatch, new ServiceState.Stopped());
        Assert.Contains(
            ControlProtocol.Version.ToString(),
            presentation.Detail,
            StringComparison.Ordinal);
    }
}

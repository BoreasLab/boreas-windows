using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws of <see cref="StatusPresentation.For"/>, the function that decides what
/// this application tells a person about their network.
/// </summary>
/// <remarks>
/// The finite domain is fully enumerated: five channel states by seven service
/// states, or 35 pairs.
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
    /// Service representatives split running bypass and failure recoverability
    /// because each changes the presentation.
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

    [Theory]
    [MemberData(nameof(Domain))]
    public void The_announcement_carries_everything_the_tone_does(int channel, int service)
    {
        var presentation = StatusPresentation.For(Channels[channel], Services[service]);

        Assert.Contains(presentation.Headline, presentation.Announcement, StringComparison.Ordinal);
        Assert.Contains(presentation.Detail, presentation.Announcement, StringComparison.Ordinal);
    }

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

    [Fact]
    public void An_unrecoverable_failure_offers_nothing_to_press()
    {
        var presentation = StatusPresentation.For(
            new ControlChannelState.Connected(1),
            new ServiceState.Failed(ControlOperation.Start, AnyError, Recoverable: false));

        Assert.Equal(PrimaryAction.None, presentation.Action);
    }

    [Theory]
    [MemberData(nameof(Domain))]
    public void A_label_exists_exactly_when_an_action_does(int channel, int service)
    {
        var model = new StatusViewModel(new StubChannel(Channels[channel], Services[service]));

        Assert.Equal(model.HasPrimaryAction, model.PrimaryLabel.Length > 0);
        Assert.Equal(model.HasPrimaryAction, model.Primary.CanExecute(null));
    }

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

    [Theory]
    [MemberData(nameof(Domain))]
    public void The_bypass_warning_tracks_the_bypass(int channel, int service)
    {
        var model = new StatusViewModel(new StubChannel(Channels[channel], Services[service]));

        var degraded = Services[service] is ServiceState.Running { Status.Bypass: EgressBypass.Degraded };

        Assert.Equal(degraded, model.BypassDegradation is not null);
    }

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

    [Fact]
    public void Only_a_retryable_channel_failure_offers_an_action()
    {
        Assert.NotNull(ChannelPresentation.For(new ControlChannelState.Unavailable(AnyError)).Banner!.ActionLabel);
        Assert.Null(ChannelPresentation.For(new ControlChannelState.Unauthorized()).Banner!.ActionLabel);
        Assert.Null(ChannelPresentation.For(new ControlChannelState.VersionMismatch(ControlProtocol.Version + 1)).Banner!.ActionLabel);
    }

    /// <remarks>
    /// C# cannot require enum coverage in arrays; this catches omissions and
    /// protects direct index assumptions.
    /// </remarks>
    [Fact]
    public void Every_selector_covers_its_closed_set()
    {
        Assert.Equal(Enum.GetValues<RouteMode>(), ConfigurationViewModel.RouteOrder);
        Assert.Equal(Enum.GetValues<EgressPolicy>(), ConfigurationViewModel.EgressOrder);

        // The network form also indexes one state per field.
        Assert.Equal(Enum.GetValues<ConfigField>(), ConfigurationParser.AllFields.ToArray());

        // Dense enum values are required for direct indexing.
        foreach (var (position, field) in ConfigurationParser.AllFields.Index())
        {
            Assert.Equal(position, (int)field);
        }

        // Null is the leading "everything" filter choice.
        Assert.Equal(
            Enum.GetValues<ControlEventKind>().Cast<ControlEventKind?>(),
            DiagnosticsViewModel.FilterOrder.Skip(1));
        Assert.Null(DiagnosticsViewModel.FilterOrder[0]);
    }

    /// <remarks>
    /// Separate switches must remain inverse; this fails before a pipe response
    /// can become an unpinned field error.
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

    [Theory]
    [InlineData("")]
    [InlineData("adapterName")]
    [InlineData("something_added_later")]
    public void An_unknown_wire_name_denotes_no_field(string name) =>
        Assert.Null(ConfigField.FromWireName(name));

    [Fact]
    public void An_unknown_wire_name_is_not_invented_from_null() =>
        Assert.Null(ConfigField.FromWireName(null));

    [Fact]
    public void A_version_mismatch_reports_the_version_this_client_speaks()
    {
        var mismatch = new ControlChannelState.VersionMismatch(ServiceVersion: 99);

        Assert.Equal(ControlProtocol.Version, mismatch.ClientVersion);
        Assert.Equal(99, mismatch.ServiceVersion);

        // Presentation uses the same client version.
        var presentation = StatusPresentation.For(mismatch, new ServiceState.Stopped());
        Assert.Contains(
            ControlProtocol.Version.ToString(),
            presentation.Detail,
            StringComparison.Ordinal);
    }
}

using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws of the network form: what it accepts, when it complains, and what it
/// does with the service's answer.
/// </summary>
public sealed class ConfigurationLaws
{
    private static async Task<ConfigurationViewModel> LoadedAsync(RecordingChannel channel)
    {
        var model = new ConfigurationViewModel(channel);
        await model.LoadAsync();
        return model;
    }

    /// <summary>
    /// Be liberal in what you accept. Every one of these is unambiguously the
    /// same number, and rejecting any of them would be the system making its
    /// formatting preference the user's problem.
    /// </summary>
    [Theory]
    [InlineData("1420")]
    [InlineData(" 1420 ")]
    [InlineData("1,420")]
    [InlineData("1 420")]
    public async Task An_unambiguous_packet_size_is_accepted_however_it_is_written(string typed)
    {
        var channel = new RecordingChannel(new ConfigurationOutcome.Applied());
        var model = await LoadedAsync(channel);

        model.Mtu = typed;
        model.MarkTouched(ConfigField.Mtu);

        Assert.Null(model.MtuError);

        await model.Apply.ExecuteAsync(CancellationToken.None);

        // The service receives a parsed number, not a spelling of one.
        Assert.Equal(1420, channel.LastApplied!.PacketSize.Value);
    }

    /// <summary>
    /// Separators are the user's choice and the system's problem. All of these
    /// name the same two servers.
    /// </summary>
    [Theory]
    [InlineData("10.7.0.1, 10.7.0.2")]
    [InlineData("10.7.0.1;10.7.0.2")]
    [InlineData("10.7.0.1 10.7.0.2")]
    [InlineData("10.7.0.1\n10.7.0.2")]
    public async Task Dns_servers_are_accepted_with_any_ordinary_separator(string typed)
    {
        var channel = new RecordingChannel(new ConfigurationOutcome.Applied());
        var model = await LoadedAsync(channel);

        model.DnsServers = typed;
        model.MarkTouched(ConfigField.Dns);

        Assert.Null(model.DnsError);

        await model.Apply.ExecuteAsync(CancellationToken.None);

        Assert.Equal(DnsServers.TryParse("10.7.0.1 10.7.0.2"), channel.LastApplied!.Dns);
    }

    /// <summary>
    /// Parsing is idempotent through the canonical rendering: parse, render,
    /// parse again, and you land on the same value. This is the law that lets
    /// the form round-trip a configuration through the service and back
    /// without drifting, and it is stated once over the parser rather than
    /// re-tested per field through the view model.
    /// </summary>
    [Theory]
    [InlineData("Boreas", "10.7.0.2/24", "1,420", "10.7.0.1, 10.7.0.2")]
    [InlineData("  Boreas  ", "  fd00::2/64  ", " 9000 ", "")]
    [InlineData("Tunnel", "10.0.0.1/8", "1280", "1.1.1.1;8.8.8.8")]
    public void Parsing_is_idempotent_through_the_canonical_rendering(
        string adapter, string address, string mtu, string dns)
    {
        var draft = new ConfigurationDraft(adapter, address, mtu, dns, RouteMode.Default, EgressPolicy.Direct);

        var once = Assert.IsType<ConfigurationParse.Valid>(ConfigurationParser.Parse(draft)).Configuration;
        var twice = Assert.IsType<ConfigurationParse.Valid>(
            ConfigurationParser.Parse(once.ToDraft())).Configuration;

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// A refined value cannot be built from text that fails its rule, so the
    /// only way to hold one is to have passed the check.
    /// </summary>
    [Fact]
    public void A_refined_value_rejects_text_that_fails_its_rule()
    {
        Assert.Null(AdapterName.TryParse("   "));
        Assert.Null(TunnelAddress.TryParse("10.7.0.2"));
        Assert.Null(PacketSize.TryParse("1279"));
        Assert.Null(PacketSize.TryParse("9001"));
        Assert.Null(DnsServers.TryParse("not-an-address"));

        Assert.NotNull(AdapterName.TryParse(" Boreas "));
        Assert.NotNull(TunnelAddress.TryParse("fd00::2/64"));
        Assert.NotNull(PacketSize.TryParse("1,420"));
        Assert.Equal(DnsServers.Empty, DnsServers.TryParse("   "));
    }

    /// <summary>
    /// Nothing is said about a field the user has not finished. Telling
    /// somebody their half-typed address is invalid is noise they learn to
    /// ignore, which costs the message that matters.
    /// </summary>
    [Fact]
    public async Task An_untouched_field_stays_silent_while_it_is_being_typed()
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.InterfaceAddress = "10.";
        Assert.Null(model.AddressError);

        model.InterfaceAddress = "10.7.0";
        Assert.Null(model.AddressError);
    }

    /// <summary>
    /// Once a field has errored it revalidates live, so the message clears the
    /// moment it stops being true rather than at the next submit.
    /// </summary>
    [Fact]
    public async Task An_errored_field_clears_as_soon_as_it_becomes_valid()
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.InterfaceAddress = "not an address";
        model.MarkTouched(ConfigField.Address);
        Assert.NotNull(model.AddressError);

        model.InterfaceAddress = "10.7.0.2/24";
        Assert.Null(model.AddressError);
    }

    [Theory]
    [InlineData("10.7.0.2/24")]
    [InlineData("  10.7.0.2/24  ")]
    [InlineData("fd00::2/64")]
    public async Task A_well_formed_address_is_accepted(string typed)
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.InterfaceAddress = typed;
        model.MarkTouched(ConfigField.Address);

        Assert.Null(model.AddressError);
    }

    [Theory]
    [InlineData("10.7.0.2")]          // no prefix length
    [InlineData("10.7.0.2/")]         // empty prefix length
    [InlineData("10.7.0.2/33")]       // beyond the IPv4 maximum
    [InlineData("fd00::2/129")]       // beyond the IPv6 maximum
    [InlineData("not an address/24")]
    public async Task A_malformed_address_is_rejected_with_a_message(string typed)
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.InterfaceAddress = typed;
        model.MarkTouched(ConfigField.Address);

        Assert.False(string.IsNullOrWhiteSpace(model.AddressError));
    }

    /// <summary>
    /// An invalid form is never sent. The service would reject it, and the
    /// round trip costs the user a wait to learn what the client already knew.
    /// </summary>
    [Fact]
    public async Task An_invalid_form_is_not_sent()
    {
        var channel = new RecordingChannel(new ConfigurationOutcome.Applied());
        var model = await LoadedAsync(channel);

        model.InterfaceAddress = "nonsense";
        await model.Apply.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, channel.ApplyCount);
        Assert.NotNull(model.AddressError);
        Assert.NotNull(model.FirstErrorField);
    }

    /// <summary>
    /// A rejection keeps every typed value and places the service's message
    /// beside the field that caused it. Losing a filled-in form to a rejection
    /// is the most damaging thing a form can do.
    /// </summary>
    [Fact]
    public async Task A_rejection_preserves_entry_and_places_each_message_at_its_field()
    {
        var rejection = new ConfigurationOutcome.Rejected(
            new TypedError("cfg.rejected", "The adapter name is in use.", "Choose another name."),
            new Dictionary<ConfigField, string>
            {
                [ConfigField.Adapter] = "Another adapter already uses this name.",
            });

        var model = await LoadedAsync(new RecordingChannel(rejection));

        model.AdapterName = "Boreas";
        model.InterfaceAddress = "10.7.0.9/24";
        await model.Apply.ExecuteAsync(CancellationToken.None);

        Assert.Equal("Boreas", model.AdapterName);
        Assert.Equal("10.7.0.9/24", model.InterfaceAddress);
        Assert.Equal("Another adapter already uses this name.", model.AdapterError);
        Assert.Equal(ConfigField.Adapter, model.FirstErrorField);
    }

    /// <summary>
    /// A rejection that names no field leaves every field message clear. The
    /// banner still reports it; nothing is pinned to an arbitrary input.
    /// </summary>
    [Fact]
    public async Task A_rejection_naming_no_field_leaves_the_fields_alone()
    {
        var rejection = new ConfigurationOutcome.Rejected(
            new TypedError("cfg.rejected", "Rejected.", "Fix it."),
            new Dictionary<ConfigField, string>());

        var model = await LoadedAsync(new RecordingChannel(rejection));
        await model.Apply.ExecuteAsync(CancellationToken.None);

        Assert.Null(model.AdapterError);
        Assert.Null(model.AddressError);
        Assert.Null(model.MtuError);
        Assert.Null(model.DnsError);
        Assert.Null(model.FirstErrorField);
        Assert.NotNull(model.OutcomeMessage);
    }

    /// <summary>
    /// Each outcome says something different, because they mean different
    /// things: in effect now, saved but waiting, or not saved at all.
    /// </summary>
    [Fact]
    public async Task Each_outcome_produces_its_own_message_and_tone()
    {
        var applied = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));
        await applied.Apply.ExecuteAsync(CancellationToken.None);

        var waiting = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.RestartRequired()));
        await waiting.Apply.ExecuteAsync(CancellationToken.None);

        Assert.Equal(StatusTone.Active, applied.OutcomeTone);
        Assert.Equal(StatusTone.Caution, waiting.OutcomeTone);
        Assert.NotEqual(applied.OutcomeMessage, waiting.OutcomeMessage);
        Assert.All(
            new[] { applied.OutcomeMessage, waiting.OutcomeMessage },
            message => Assert.False(string.IsNullOrWhiteSpace(message)));
    }

    /// <summary>Discarding restores exactly what the service reported.</summary>
    [Fact]
    public async Task Discarding_restores_the_service_values_and_clears_every_message()
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.AdapterName = "";
        model.MarkTouched(ConfigField.Adapter);
        Assert.NotNull(model.AdapterError);

        await model.Revert.ExecuteAsync(CancellationToken.None);

        Assert.Equal("Boreas", model.AdapterName);
        Assert.Null(model.AdapterError);
        Assert.Null(model.OutcomeMessage);
    }

    /// <summary>The radio index maps both ways over the closed enum.</summary>
    [Theory]
    [InlineData(RouteMode.Default, 0)]
    [InlineData(RouteMode.Selected, 1)]
    public async Task Route_mode_round_trips_through_its_index(RouteMode mode, int index)
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.Routes = mode;
        Assert.Equal(index, model.RouteIndex);

        model.RouteIndex = index;
        Assert.Equal(mode, model.Routes);
    }

    /// <summary>Empty DNS is a real answer: keep what Windows already has.</summary>
    [Fact]
    public async Task Leaving_dns_empty_is_valid()
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.DnsServers = "   ";
        model.MarkTouched(ConfigField.Dns);

        Assert.Null(model.DnsError);
    }

    [Theory]
    [InlineData("1279")]
    [InlineData("9001")]
    [InlineData("")]
    [InlineData("abc")]
    public async Task A_packet_size_outside_the_carriable_range_is_rejected(string typed)
    {
        var model = await LoadedAsync(new RecordingChannel(new ConfigurationOutcome.Applied()));

        model.Mtu = typed;
        model.MarkTouched(ConfigField.Mtu);

        Assert.False(string.IsNullOrWhiteSpace(model.MtuError));
    }
}

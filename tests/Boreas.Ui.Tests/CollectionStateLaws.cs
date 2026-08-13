using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws of the six-state container and of the diagnostics list built on it.
/// </summary>
public sealed class CollectionStateLaws
{
    private static readonly ControlEvent Sample =
        new(DateTimeOffset.UnixEpoch, ControlEventKind.Transition, "Session running.");

    public static TheoryData<CollectionState<int>> AllStates() =>
    [
        new CollectionState<int>.Loading(),
        new CollectionState<int>.Failed(new TypedError("e", "s", "n"), static () => { }),
        new CollectionState<int>.Empty(),
        new CollectionState<int>.Filtered(static () => { }),
        new CollectionState<int>.Partial([1, 2], static () => { }),
        new CollectionState<int>.Ready([1, 2, 3]),
    ];

    /// <summary>
    /// Match is a total eliminator: every state selects exactly one arm, and
    /// no state falls through to a default that would render a blank region.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_state_selects_exactly_one_arm(CollectionState<int> state)
    {
        var arm = state.Match(
            loading: static _ => nameof(CollectionState<int>.Loading),
            failed: static _ => nameof(CollectionState<int>.Failed),
            empty: static _ => nameof(CollectionState<int>.Empty),
            filtered: static _ => nameof(CollectionState<int>.Filtered),
            partial: static _ => nameof(CollectionState<int>.Partial),
            ready: static _ => nameof(CollectionState<int>.Ready));

        Assert.Equal(state.GetType().Name, arm);
    }

    /// <summary>
    /// Items exist only where the state says they do. A loading or failed
    /// container that quietly returns rows would render a list under a
    /// spinner.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllStates))]
    public void Items_appear_only_in_the_two_states_that_carry_them(CollectionState<int> state)
    {
        var carries = state is CollectionState<int>.Partial or CollectionState<int>.Ready;

        Assert.Equal(carries, state.ItemsOrEmpty.Count > 0);
    }

    /// <summary>
    /// Nothing recorded yet and nothing matching the filter are different
    /// states. Collapsing them is the most common container bug, and it costs
    /// the user the one action that would help: clearing the filter.
    /// </summary>
    [Fact]
    public void Nothing_recorded_and_nothing_matching_are_distinguished()
    {
        var empty = new DiagnosticsViewModel(new StubChannel(
            new ControlChannelState.Connected(1), new ServiceState.Stopped()));

        var filtered = new DiagnosticsViewModel(new StubChannel(
            new ControlChannelState.Connected(1), new ServiceState.Stopped())
        {
            Events = [Sample],
        })
        {
            Filter = ControlEventKind.Failure,
        };

        Settle(empty);
        Settle(filtered);

        Assert.IsType<CollectionState<EventRow>.Empty>(empty.Events);
        Assert.IsType<CollectionState<EventRow>.Filtered>(filtered.Events);
    }

    [Fact]
    public void A_matching_filter_yields_only_its_own_kind()
    {
        var channel = new StubChannel(new ControlChannelState.Connected(1), new ServiceState.Stopped())
        {
            Events =
            [
                Sample,
                new ControlEvent(DateTimeOffset.UnixEpoch, ControlEventKind.Failure, "Start failed.",
                    new TypedError("start.failed", "Start failed.", "Check the adapter.")),
            ],
        };

        var model = new DiagnosticsViewModel(channel) { Filter = ControlEventKind.Failure };
        Settle(model);

        var rows = model.Events.ItemsOrEmpty;

        Assert.Single(rows);
        Assert.Equal("Failure", rows[0].Kind);
        Assert.True(rows[0].HasNextStep);
    }

    /// <summary>
    /// A row without a failure carries no remedy text, so the template never
    /// renders an empty red line under an ordinary transition.
    /// </summary>
    [Fact]
    public void An_ordinary_event_carries_no_remedy_text()
    {
        var row = EventRow.From(Sample);

        Assert.False(row.HasNextStep);
        Assert.Equal(string.Empty, row.NextStep);
        Assert.Empty(row.SupportLines);
    }

    /// <summary>
    /// The support text contains every row on screen and the technical detail
    /// each one carries, because its whole purpose is to save someone
    /// transcribing the screen by hand.
    /// </summary>
    [Fact]
    public void The_support_text_contains_every_row_and_its_detail()
    {
        var channel = new StubChannel(new ControlChannelState.Connected(1), new ServiceState.Stopped())
        {
            Events =
            [
                new ControlEvent(DateTimeOffset.UnixEpoch, ControlEventKind.Failure, "Start failed.",
                    new TypedError("start.failed", "Start failed.", "Check the adapter.", "adapter busy")),
            ],
        };

        var model = new DiagnosticsViewModel(channel);
        Settle(model);

        var text = model.ComposeSupportText();

        Assert.Contains("Start failed.", text, StringComparison.Ordinal);
        Assert.Contains("start.failed", text, StringComparison.Ordinal);
        Assert.Contains("Check the adapter.", text, StringComparison.Ordinal);
        Assert.Contains("adapter busy", text, StringComparison.Ordinal);
    }

    /// <summary>The filter index maps both ways over the closed kind set.</summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(1, ControlEventKind.Transition)]
    [InlineData(2, ControlEventKind.Command)]
    [InlineData(3, ControlEventKind.Channel)]
    [InlineData(4, ControlEventKind.Failure)]
    public void The_filter_index_round_trips(int index, ControlEventKind? kind)
    {
        var model = new DiagnosticsViewModel(new StubChannel(
            new ControlChannelState.Connected(1), new ServiceState.Stopped()))
        {
            FilterIndex = index,
        };

        Assert.Equal(kind, model.Filter);
        Assert.Equal(index, model.FilterIndex);
    }

    /// <summary>
    /// Event identity is time-ordered, so sorting by id agrees with sorting by
    /// time. A version 4 identifier would order arbitrarily.
    /// </summary>
    [Fact]
    public void Event_identity_is_a_version_7_uuid()
    {
        var id = new ControlEvent(DateTimeOffset.UnixEpoch, ControlEventKind.Channel, "x").Id;

        // RFC 9562 puts the version in the high nibble of the 7th byte.
        Assert.Equal(7, (id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    /// <summary>
    /// Drives the model past its initial loading state without a UI thread.
    /// </summary>
    private static void Settle(DiagnosticsViewModel model) =>
        model.Reload.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
}

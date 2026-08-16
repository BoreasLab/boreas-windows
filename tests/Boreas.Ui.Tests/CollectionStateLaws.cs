using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws for the six-state container and diagnostics list.
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
    /// Every state selects exactly one Match arm.
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
    /// Only Partial and Ready carry items.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllStates))]
    public void Items_appear_only_in_the_two_states_that_carry_them(CollectionState<int> state)
    {
        var carries = state is CollectionState<int>.Partial or CollectionState<int>.Ready;

        Assert.Equal(carries, state.ItemsOrEmpty.Count > 0);
    }

    /// <summary>
    /// Empty and filtered states stay distinct; only filtered offers ClearFilter.
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
    /// Ordinary events carry no remedy text.
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
    /// Support copy includes every row and its technical detail.
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

    /// <summary>The filter index round-trips over the closed kind set.</summary>
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
    /// Version 7 IDs sort with event time; version 4 IDs would not.
    /// </summary>
    [Fact]
    public void Event_identity_is_a_version_7_uuid()
    {
        var id = new ControlEvent(DateTimeOffset.UnixEpoch, ControlEventKind.Channel, "x").Id;

        // RFC 9562 stores the version in the high nibble of byte 7.
        Assert.Equal(7, (id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    /// <summary>Completes initial loading without a UI thread.</summary>
    private static void Settle(DiagnosticsViewModel model) =>
        model.Reload.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
}

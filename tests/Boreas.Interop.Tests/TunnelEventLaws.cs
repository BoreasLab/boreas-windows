using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;

namespace Boreas.Interop.Tests;

/// <summary>
/// Laws for the read from a flat product to a closed sum.
/// </summary>
/// <remarks>
/// <c>BoreasEvent</c> carries every arm's fields side by side and only the ones
/// the tag names carry meaning. The risk this class exists for is a reader that
/// uses a field the tag did not name and finds a plausible zero there.
/// </remarks>
public sealed class TunnelEventLaws
{
    /// <summary>
    /// A struct with every field non-zero, so a translation that reads the
    /// wrong one produces a wrong answer rather than a zero that looks right.
    /// </summary>
    private static BoreasEvent Populated(BoreasEventKind kind) => new()
    {
        Kind = kind,
        Blocked = true,
        NameLen = 11,
        RuleLen = 13,
        Allowed = 17,
        BlockedRules = 19,
        Inspected = 23,
        Counters = new BoreasCounters
        {
            DatagramsDropped = 1,
            PacketsRejected = 2,
            QuicSteered = 3,
            PathsReported = 4,
            EventsLost = 5,
            TasksPanicked = 6,
        },
    };

    private static TunnelEvent? Read(BoreasEvent raw, string name = "ads.example", string rule = "||ads.example^") =>
        TunnelEvent.TryFrom(in raw, name, rule, 256, 256);

    [Fact]
    public void A_resolved_event_carries_its_name_rule_and_verdict()
    {
        var read = Assert.IsType<TunnelEvent.Resolved>(Read(Populated(BoreasEventKind.Resolved)));

        Assert.Equal("ads.example", read.Name);
        Assert.Equal("||ads.example^", read.Rule);
        Assert.True(read.Blocked);
        Assert.False(read.Truncated);
    }

    /// <summary>
    /// <c>rule_len == 0</c> means no rule decided it, which is a different
    /// answer from a rule whose text was empty.
    /// </summary>
    [Fact]
    public void A_resolution_no_rule_decided_carries_a_null_rule()
    {
        var raw = Populated(BoreasEventKind.Resolved);
        raw.RuleLen = 0;

        Assert.Null(Assert.IsType<TunnelEvent.Resolved>(Read(raw)).Rule);
    }

    /// <summary>
    /// The lengths are the <b>full</b> lengths before truncation, so larger
    /// than the capacity offered means the text did not all fit. A reader that
    /// compared against the string it got back would never see it.
    /// </summary>
    [Theory]
    [InlineData(257u, 13u)]
    [InlineData(11u, 257u)]
    [InlineData(4096u, 4096u)]
    public void Text_longer_than_the_buffer_is_reported_as_truncated(uint nameLength, uint ruleLength)
    {
        var raw = Populated(BoreasEventKind.Resolved);
        raw.NameLen = nameLength;
        raw.RuleLen = ruleLength;

        Assert.True(Assert.IsType<TunnelEvent.Resolved>(Read(raw)).Truncated);
    }

    /// <summary>Exactly the capacity fits: the comparison is strict.</summary>
    [Fact]
    public void Text_that_exactly_fills_the_buffer_is_not_truncated()
    {
        var raw = Populated(BoreasEventKind.Resolved);
        raw.NameLen = 256;
        raw.RuleLen = 256;

        Assert.False(Assert.IsType<TunnelEvent.Resolved>(Read(raw)).Truncated);
    }

    [Fact]
    public void A_reloaded_event_carries_the_three_counts_the_tag_names()
    {
        var read = Assert.IsType<TunnelEvent.Reloaded>(Read(Populated(BoreasEventKind.Reloaded)));

        Assert.Equal(17u, read.Allowed);
        Assert.Equal(19u, read.BlockedRules);
        Assert.Equal(23u, read.Inspected);
    }

    [Fact]
    public void A_counted_event_carries_the_six_counters_in_order()
    {
        var read = Assert.IsType<TunnelEvent.Counted>(Read(Populated(BoreasEventKind.Counted)));

        Assert.Equal(new TunnelCounters(1, 2, 3, 4, 5, 6), read.Counters);
        Assert.False(read.Counters.IsQuiet);
    }

    /// <summary>
    /// api/stability.md reserves adding an event kind and says to ignore what
    /// cannot be interpreted. Null is how the reader skips it; throwing would
    /// end the loop and strand the tunnel with nothing draining its events.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(-1)]
    public void An_event_kind_this_build_predates_is_skipped(int kind)
    {
        var raw = Populated((BoreasEventKind)kind);

        Assert.Null(Read(raw));
    }

    /// <summary>
    /// A tunnel working normally reports zeroes, so "nothing went wrong" has to
    /// be recognisable without knowing what any individual counter means.
    /// </summary>
    [Fact]
    public void An_interval_with_nothing_in_it_is_quiet() =>
        Assert.True(default(TunnelCounters).IsQuiet);
}

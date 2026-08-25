using Boreas.Build;

namespace Boreas.Build.Tests;

/// <summary>
/// The laws the release scheme rests on, each stated as the thing that goes
/// wrong without it.
/// </summary>
/// <remarks>
/// These laws catch reversed builds, numeric commits sorted below their
/// siblings, and versions that climb with commit volume.
/// </remarks>
public sealed class ReleaseLaws
{
    /// <summary>2026-08-25 11:30:00 UTC, the instant every rendering is pinned to.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 8, 25, 11, 30, 0, TimeSpan.Zero);

    private static ReleaseVersion Version(string text) =>
        Assert.NotNull(ReleaseVersion.TryParseTriple(text));

    private static CommitSha Sha(string hex = "1a2b3c4d5e6f7890abcdef1234567890abcdef12") =>
        Assert.NotNull(CommitSha.TryParse(hex));

    private static Publish Pre(string[] tags, DateTimeOffset at) =>
        Release.Resolve(TriggerEvent.Push.Instance, tags, BuildStamp.At(at), Sha());

    private static Publish Cut(string version) =>
        Release.Resolve(
            new TriggerEvent.Release(Version(version)), [], BuildStamp.At(Noon), Sha());

    // ---------------------------------------------------------------- shape

    /// <summary>
    /// The shape, exactly. A change here is a change every consumer sees, so it
    /// is written out rather than assembled from the parts it is made of.
    /// </summary>
    [Fact]
    public void A_pre_release_names_its_time_and_its_commit() =>
        Assert.Equal("v0.4.3-dev.2026-08-25.11-30-00.g1a2b3c4", Pre(["v0.4.2"], Noon).Tag);

    [Fact]
    public void A_release_is_the_triple_and_nothing_else() =>
        Assert.Equal("v0.4.2", Cut("0.4.2").Tag);

    // ------------------------------------------------------- law 1: ordering

    /// <summary>
    /// Major, then minor, then patch, each compared numerically. Every pair
    /// below is one a lexical comparison gets wrong: 10 sorts below 9 as text
    /// and above it as a number.
    /// </summary>
    [Theory]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("1.0.0", "10.0.0")]
    [InlineData("0.0.9", "0.0.10")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("0.4.2", "0.4.3")]
    [InlineData("0.1.0", "0.2.0")]
    public void Versions_compare_numerically_field_by_field(string lower, string higher)
    {
        Assert.True(Version(lower) < Version(higher));
        Assert.True(Version(higher) > Version(lower));
        Assert.NotEqual(Version(lower), Version(higher));
    }

    /// <summary>
    /// The pairs above are chosen so that at least one of them is one a
    /// text comparison would reverse. Stated separately so the law is visibly
    /// stronger than string order rather than accidentally agreeing with it.
    /// </summary>
    [Fact]
    public void Numeric_order_beats_lexical_order()
    {
        Assert.True(string.CompareOrdinal("0.9.0", "0.10.0") > 0, "the premise");
        Assert.True(Version("0.9.0") < Version("0.10.0"));
    }

    /// <summary>
    /// The three tag kinds in the order SemVer puts them: a release, the
    /// pre-release heading for the next patch, and that patch's release.
    /// </summary>
    [Fact]
    public void The_three_tag_kinds_sort_in_the_order_semver_gives_them()
    {
        var before = Cut("0.4.2");
        var middle = Pre(["v0.4.2"], Noon);
        var after = Cut("0.4.3");

        // A pre-release is numbered for the patch that has not happened yet.
        Assert.Equal(Version("0.4.3"), middle.Version);
        Assert.Equal(after.Version, middle.Version);

        Assert.True(before < middle);
        Assert.True(middle < after);

        Assert.True(middle.IsPrerelease);
        Assert.False(after.IsPrerelease);
        Assert.False(before.IsPrerelease);
    }

    /// <summary>Origin is the identity of the fold, and it is the struct's zero.</summary>
    [Fact]
    public void The_origin_is_below_every_version_and_is_the_default()
    {
        Assert.Equal(ReleaseVersion.Origin, default(ReleaseVersion));
        Assert.Equal("0.0.0", ReleaseVersion.Origin.ToString());
        Assert.True(ReleaseVersion.Origin < Version("0.0.1"));
    }

    // ---------------------------------------------------- law 2: the stamp

    /// <summary>
    /// What the zero padding buys. Unpadded, <c>9-30-00</c> sorts above
    /// <c>11-30-00</c> and two builds an hour apart come back reversed - and
    /// SemVer compares this identifier as ASCII, so text order is the order that
    /// matters.
    /// </summary>
    [Fact]
    public void Later_builds_sort_later_as_text()
    {
        var morning = Pre([], Noon.AddHours(-2)).Tag;
        var noon = Pre([], Noon).Tag;

        Assert.Contains("09-30-00", morning, StringComparison.Ordinal);
        Assert.Contains("11-30-00", noon, StringComparison.Ordinal);
        Assert.True(string.CompareOrdinal(morning, noon) < 0, $"{morning} should sort below {noon}");
    }

    /// <summary>
    /// The rendering is fixed width, whatever the instant. A month, day, hour,
    /// minute or second below ten is where an unpadded implementation differs.
    /// </summary>
    [Theory]
    [InlineData(2026, 1, 2, 3, 4, 5, "2026-01-02.03-04-05")]
    [InlineData(2026, 12, 31, 23, 59, 59, "2026-12-31.23-59-59")]
    [InlineData(2026, 8, 25, 0, 0, 0, "2026-08-25.00-00-00")]
    [InlineData(999, 1, 1, 1, 1, 1, "0999-01-01.01-01-01")]
    public void A_stamp_is_always_the_same_width(
        int year, int month, int day, int hour, int minute, int second, string expected)
    {
        var stamp = BuildStamp.At(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

        Assert.Equal(expected, stamp.ToString());
        Assert.Equal(19, stamp.ToString().Length);
    }

    /// <summary>
    /// An instant handed over in another offset renders as the UTC wall clock it
    /// compares as. Rendering local time would make two builds' order depend on
    /// which runner produced them.
    /// </summary>
    [Fact]
    public void A_stamp_renders_the_utc_instant_whatever_offset_it_arrived_in()
    {
        var utc = BuildStamp.At(new DateTimeOffset(2026, 8, 25, 11, 30, 0, TimeSpan.Zero));
        var elsewhere = BuildStamp.At(new DateTimeOffset(2026, 8, 25, 20, 30, 0, TimeSpan.FromHours(9)));

        Assert.Equal(utc, elsewhere);
        Assert.Equal("2026-08-25.11-30-00", elsewhere.ToString());
    }

    /// <summary>
    /// Text order on the rendering agrees with chronological order on the
    /// instant, over a stride that crosses every field boundary.
    /// </summary>
    [Fact]
    public void Text_order_on_a_stamp_is_chronological_order()
    {
        var instants = Enumerable.Range(0, 400)
            .Select(step => BuildStamp.At(Noon.AddMinutes(-step * 137)))
            .ToArray();

        foreach (var (later, earlier) in instants.Zip(instants.Skip(1)))
        {
            Assert.True(earlier < later);
            Assert.True(
                string.CompareOrdinal(earlier.ToString(), later.ToString()) < 0,
                $"{earlier} should sort below {later}");
        }
    }

    // ------------------------------------------------------ law 3: the commit

    /// <summary>
    /// A commit that abbreviates to seven digits would be a <b>numeric</b>
    /// SemVer identifier, which is compared numerically and ranks below every
    /// alphanumeric one. The prefix is what stops that being possible.
    /// </summary>
    [Fact]
    public void A_commit_is_never_an_all_digit_identifier()
    {
        var digits = Sha("0012345678901234567890123456789012345678");

        Assert.Equal("g0012345", digits.ToString());
        Assert.False(digits.ToString().All(char.IsAsciiDigit));
    }

    [Theory]
    [InlineData("1a2b3c4d5e6f", "g1a2b3c4")]
    [InlineData("ABCDEF1234567890", "gabcdef1")]
    [InlineData("0000000", "g0000000")]
    public void A_commit_keeps_seven_lower_case_digits(string full, string expected) =>
        Assert.Equal(expected, Sha(full).ToString());

    [Theory]
    [InlineData("1a2b3c")]
    [InlineData("1a2b3c4z")]
    [InlineData("not-a-sha")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_a_commit_is_refused(string? text) =>
        Assert.Null(CommitSha.TryParse(text));

    // ------------------------------------------------- law 4: the base version

    /// <summary>
    /// The next pre-release heads for the patch above the newest release. The
    /// tag set is the only source, so there is no second version to disagree.
    /// </summary>
    [Fact]
    public void The_base_version_is_the_patch_above_the_newest_release()
    {
        Assert.Equal(Version("0.4.3"), Pre(["v0.4.2"], Noon).Version);
        Assert.Equal(Version("0.4.3"), Pre(["v0.1.0", "v0.4.2", "v0.2.9"], Noon).Version);

        // Commutative: the order tags arrive in is git's business, not this.
        Assert.Equal(Version("0.4.3"), Pre(["v0.4.2", "v0.1.0"], Noon).Version);
    }

    /// <summary>
    /// A repository that has never shipped heads for 0.0.1, which is
    /// <see cref="ReleaseVersion.Origin"/>'s successor. The anchor tag v0.0.0
    /// means the case is unusual here, but the fold must remain total without
    /// any release tags.
    /// </summary>
    [Fact]
    public void A_repository_that_has_never_shipped_heads_for_the_first_patch()
    {
        Assert.Equal(Version("0.0.1"), Pre([], Noon).Version);
        Assert.Equal(Version("0.0.1"), Pre(["v0.0.0"], Noon).Version);
    }

    /// <summary>
    /// Pre-release tags do not raise the base version. Counting them would make
    /// the version track commit volume rather than release intent.
    /// </summary>
    [Fact]
    public void A_pre_release_tag_never_raises_the_base_version()
    {
        Assert.Equal(
            Version("0.0.1"),
            Pre(["v0.9.9-dev.2026-01-01.00-00-00.gabc1234"], Noon).Version);

        Assert.Equal(
            Version("0.0.1"),
            Pre(["0.9.0", "release-0.9.0", "v0.9", "v0.9.0.1", "v0.09.0", "", "v"], Noon).Version);
    }

    /// <summary>
    /// A minor is cut by tagging that minor.
    /// </summary>
    [Fact]
    public void The_way_to_cut_a_minor_is_to_tag_one()
    {
        Assert.Equal(Version("0.2.0"), Cut("0.2.0").Version);
        Assert.Equal(Version("0.2.1"), Pre(["v0.1.9", "v0.2.0"], Noon).Version);
    }

    // ------------------------------------------ the algebra is total

    /// <summary>
    /// Resolve cannot fail: the untrusted string becomes a value at the parser
    /// boundary, before this fold receives it.
    /// </summary>
    [Fact]
    public void Resolve_is_total_over_every_tag_set()
    {
        string[][] sets =
        [
            [],
            ["v0.0.0"],
            ["rubbish", "", "v", "v0.9.9-dev.2026-01-01.00-00-00.gabc1234"],
            ["v0.1.0", "v10.20.30"],
        ];

        foreach (var tags in sets)
        {
            Assert.NotNull(Release.Resolve(TriggerEvent.Push.Instance, tags, BuildStamp.At(Noon), Sha()));
        }
    }

    /// <summary>
    /// The refusal that remains lives at the parser: a ref that is not a strict
    /// triple never becomes a release event.
    /// </summary>
    /// <remarks>
    /// <b>SemVer forbids a leading zero in a numeric identifier</b>, so v0.04.2
    /// is a refusal rather than a second spelling of v0.4.2 - the two would
    /// otherwise sort apart in anything comparing the text.
    /// </remarks>
    [Theory]
    [InlineData("0.4.2")]
    [InlineData("v0.4")]
    [InlineData("v0.4.2.1")]
    [InlineData("v0.04.2")]
    [InlineData("v00.4.2")]
    [InlineData("v0.4.02")]
    [InlineData("v+0.4.2")]
    [InlineData("v0.4.2 ")]
    [InlineData("release-0.4.2")]
    [InlineData("v0.4.2-dev.2026-08-24.11-30-00.gabc1234")]
    [InlineData("")]
    [InlineData("v")]
    [InlineData(null)]
    public void A_ref_that_is_not_a_release_tag_never_becomes_one(string? tag) =>
        Assert.Null(ReleaseVersion.TryParseTag(tag));

    [Theory]
    [InlineData("v0.0.0")]
    [InlineData("v1.2.3")]
    [InlineData("v10.20.30")]
    [InlineData("v0.1.0")]
    public void A_release_tag_round_trips_through_its_parser(string text) =>
        Assert.Equal(text, $"v{Assert.NotNull(ReleaseVersion.TryParseTag(text))}");
}

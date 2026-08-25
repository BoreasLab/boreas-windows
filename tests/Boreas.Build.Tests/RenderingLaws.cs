using Boreas.Build;

namespace Boreas.Build.Tests;

/// <summary>
/// The law that connects the tag scheme to the numbers Windows carries.
/// </summary>
/// <remarks>
/// <b>FileVersion is order-preserving from the tag order.</b> For publishes
/// <c>a &lt; b</c> by SemVer precedence, the rendered version of <c>a</c> is
/// below the rendered version of <c>b</c>, field by field. Stated as a property
/// over a simulated history rather than on hand-picked points, because the case
/// that breaks it is the one nobody thought to pick: the release that must sort
/// above however many pre-releases happened to precede it.
/// </remarks>
public sealed class RenderingLaws
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 25, 11, 30, 0, TimeSpan.Zero);

    private static ReleaseVersion Version(string text) =>
        Assert.NotNull(ReleaseVersion.TryParseTriple(text));

    /// <summary>A distinct commit per step, so no two tags collide.</summary>
    private static CommitSha Sha(int step) =>
        Assert.NotNull(CommitSha.TryParse(step.ToString("x8", System.Globalization.CultureInfo.InvariantCulture)));

    private static WindowsVersion File(Publish publish, int offset) =>
        Assert.NotNull(Rendering.TryFileVersion(publish, offset));

    /// <summary>
    /// A repository's life, in the order it happens: pushes to main, then a
    /// human cutting a tag, then more pushes.
    /// </summary>
    /// <remarks>
    /// The offset resets at each release tag, because that is what
    /// <c>git describe</c> reports: commits since the newest release, not
    /// commits ever. A history that forgot the reset would test a monotonic
    /// counter rather than the one the pipeline actually passes.
    /// </remarks>
    private static List<(Publish Publish, int Offset)> Simulate(int cycles, int pushesPerCycle, bool minors)
    {
        var tags = new List<string> { "v0.0.0" };
        var timeline = new List<(Publish, int)>();
        var stamp = Noon;
        var offset = 0;
        var step = 0;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            for (var push = 0; push < pushesPerCycle; push++)
            {
                offset++;
                stamp = stamp.AddMinutes(17);
                timeline.Add((
                    Release.Resolve(TriggerEvent.Push.Instance, tags, BuildStamp.At(stamp), Sha(++step)),
                    offset));
            }

            // The human act. Either the patch the pre-releases were heading for,
            // or a minor - which is said by tagging one and in no other way.
            var heading = Release.BaseVersion(tags);
            var cut = minors && cycle % 2 == 1
                ? Version($"{heading.Major}.{heading.Minor + 1}.0")
                : heading;

            offset++;
            stamp = stamp.AddMinutes(17);
            timeline.Add((
                Release.Resolve(new TriggerEvent.Release(cut), tags, BuildStamp.At(stamp), Sha(++step)),
                offset));

            tags.Add($"v{cut}");

            // describe now answers from this tag.
            offset = 0;
        }

        return timeline;
    }

    [Theory]
    [InlineData(6, 1, false)]
    [InlineData(6, 5, false)]
    [InlineData(8, 3, true)]
    [InlineData(3, 40, true)]
    // One release after another with no pushes between them.
    [InlineData(10, 0, true)]
    public void A_file_version_is_order_preserving_from_the_tag_order(int cycles, int pushes, bool minors)
    {
        var history = Simulate(cycles, pushes, minors);

        Assert.NotEmpty(history);

        // The history is chronological, so precedence must agree with it. If it
        // does not, the law below would be vacuously true over the wrong order.
        foreach (var (earlier, later) in history.Zip(history.Skip(1)))
        {
            Assert.True(
                earlier.Publish < later.Publish,
                $"{earlier.Publish.Tag} should precede {later.Publish.Tag}");
        }

        // THE LAW, over every pair and not only adjacent ones.
        foreach (var (left, leftIndex) in history.Select((entry, index) => (entry, index)))
        {
            foreach (var right in history.Skip(leftIndex + 1))
            {
                Assert.True(
                    File(left.Publish, left.Offset) < File(right.Publish, right.Offset),
                    $"{left.Publish.Tag} -> {File(left.Publish, left.Offset)} should sort below "
                    + $"{right.Publish.Tag} -> {File(right.Publish, right.Offset)}");
            }
        }
    }

    /// <summary>
    /// A release takes the field's maximum, which is what puts it above every
    /// pre-release sharing its triple however many there were.
    /// </summary>
    [Fact]
    public void A_release_takes_the_top_of_the_revision_field()
    {
        var release = Release.Resolve(
            new TriggerEvent.Release(Version("0.4.2")), [], BuildStamp.At(Noon), Sha(1));

        Assert.Equal(WindowsVersion.MaxField, File(release, 0).Revision);
        Assert.Equal("0.4.2.65535", File(release, 0).ToString());

        // A release ignores the offset, which is the distance to its tag.
        Assert.Equal(File(release, 0), File(release, 9_999));
    }

    /// <summary>
    /// A pre-release carries the commit count, and is refused rather than
    /// wrapped when it will not fit. A truncated revision would sort below its
    /// own predecessor.
    /// </summary>
    [Fact]
    public void A_pre_release_that_outgrows_the_field_is_refused()
    {
        var pre = Release.Resolve(TriggerEvent.Push.Instance, ["v0.4.2"], BuildStamp.At(Noon), Sha(1));

        Assert.Equal(0, File(pre, 0).Revision);
        Assert.Equal(WindowsVersion.MaxField - 1, File(pre, WindowsVersion.MaxField - 1).Revision);

        // The maximum belongs to releases alone, so a pre-release may not tie
        // with one.
        Assert.Null(Rendering.TryFileVersion(pre, WindowsVersion.MaxField));
        Assert.Null(Rendering.TryFileVersion(pre, WindowsVersion.MaxField + 1));
    }

    /// <summary>
    /// The assembly version is a binding identity, not a build stamp, so it is
    /// stable within a minor.
    /// </summary>
    [Fact]
    public void An_assembly_version_moves_only_with_the_minor()
    {
        var first = Release.Resolve(TriggerEvent.Push.Instance, ["v0.4.2"], BuildStamp.At(Noon), Sha(1));
        var later = Release.Resolve(TriggerEvent.Push.Instance, ["v0.4.9"], BuildStamp.At(Noon), Sha(2));
        var minor = Release.Resolve(new TriggerEvent.Release(Version("0.5.0")), [], BuildStamp.At(Noon), Sha(3));

        Assert.Equal("0.4.0.0", Rendering.AssemblyVersion(first).ToString());
        Assert.Equal(Rendering.AssemblyVersion(first), Rendering.AssemblyVersion(later));
        Assert.Equal("0.5.0.0", Rendering.AssemblyVersion(minor).ToString());
    }

    /// <summary>
    /// The informational version is the tag verbatim, which is the only
    /// rendering the build can be recovered from exactly. The four-part fields
    /// cannot carry a pre-release identifier at all.
    /// </summary>
    [Fact]
    public void The_informational_version_is_the_tag_without_its_v()
    {
        var pre = Release.Resolve(TriggerEvent.Push.Instance, ["v0.4.2"], BuildStamp.At(Noon), Sha(0x1a2b3c4));

        Assert.Equal(pre.Tag[1..], Rendering.InformationalVersion(pre));
        Assert.Equal($"v{Rendering.InformationalVersion(pre)}", pre.Tag);
        Assert.Contains("-dev.", Rendering.InformationalVersion(pre), StringComparison.Ordinal);
    }

    /// <summary>Every field is a WORD, and out-of-range is a refusal.</summary>
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(65535, 65535, 65535, 65535)]
    public void A_windows_version_accepts_every_word(int a, int b, int c, int d) =>
        Assert.NotNull(WindowsVersion.TryCreate(a, b, c, d));

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(65536, 0, 0, 0)]
    [InlineData(0, 0, 0, 65536)]
    public void A_windows_version_refuses_anything_wider(int a, int b, int c, int d) =>
        Assert.Null(WindowsVersion.TryCreate(a, b, c, d));

    // ------------------------------------------------- the other ceiling

    /// <summary>
    /// The mirror of the theory above, and <b>the pair of opposite verdicts on
    /// 65535 is the assertion worth having</b>: a VERSIONINFO WORD takes it and
    /// an assembly identity does not.
    /// </summary>
    /// <remarks>
    /// <c>-p:AssemblyVersion=65535.0.0.0</c> is <b>CS7034</b>, in that position
    /// and in every other; 65534 builds in all four. An identity must be able to
    /// say "this component is unspecified" - what a partial reference like
    /// <c>Foo, Version=1.0</c> is built from - and with no presence bit beside
    /// the four metadata USHORTs, 0xFFFF is spent as that sentinel. A label
    /// names nothing and so reserves nothing.
    /// </remarks>
    [Fact]
    public void An_assembly_identity_stops_one_short_of_a_word()
    {
        Assert.NotNull(AssemblyIdentityVersion.TryCreate(AssemblyIdentityVersion.MaxField, 0));
        Assert.NotNull(AssemblyIdentityVersion.TryCreate(0, AssemblyIdentityVersion.MaxField));

        // CS7034's bound, from either side.
        Assert.Null(AssemblyIdentityVersion.TryCreate(65535, 0));
        Assert.Null(AssemblyIdentityVersion.TryCreate(0, 65535));

        // The same number, two verdicts, because the two fields are not the
        // same kind of thing.
        Assert.NotNull(WindowsVersion.TryCreate(65535, 65535, 65535, 65535));
        Assert.Equal(WindowsVersion.MaxField - 1, AssemblyIdentityVersion.MaxField);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(65536, 0)]
    public void An_assembly_identity_refuses_anything_wider(int major, int minor) =>
        Assert.Null(AssemblyIdentityVersion.TryCreate(major, minor));

    /// <summary>
    /// Build and revision are the rendering, not state. There is no way to set
    /// them, which is what a two-field type buys over asserting they are zero.
    /// </summary>
    [Fact]
    public void An_assembly_identity_renders_its_trailing_zeroes() =>
        Assert.Equal("1.2.0.0", Assert.NotNull(AssemblyIdentityVersion.TryCreate(1, 2)).ToString());

    /// <summary>
    /// The release sentinel is one past what an assembly identity accepts, which
    /// is the whole reason the two are different types.
    /// </summary>
    [Fact]
    public void The_release_sentinel_is_not_a_value_an_identity_could_hold()
    {
        Assert.Equal(WindowsVersion.MaxField, Rendering.ReleaseRevision);
        Assert.True(Rendering.ReleaseRevision > AssemblyIdentityVersion.MaxField);
        Assert.Null(AssemblyIdentityVersion.TryCreate(Rendering.ReleaseRevision, 0));
    }
}

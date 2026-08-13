using System.Text.RegularExpressions;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws the visual system has to satisfy, checked against the XAML rather than
/// against a comment claiming it.
/// </summary>
/// <remarks>
/// The corpus is built once for the whole class. Each law is then a pure
/// predicate over it.
/// </remarks>
public sealed class DesignSystemLaws
{
    private static readonly DesignCorpus Corpus = DesignCorpus.Load();

    /// <summary>Themes that define literal colours, so contrast is computable.</summary>
    private static readonly Theme[] LiteralThemes = [Theme.Light, Theme.Dark];

    /// <summary>
    /// Text pairings and the ratio each needs. Normal text needs 4.5:1;
    /// anything at or above the large-text threshold needs 3:1. Nothing in
    /// this table is exempt, so every entry carries the stricter figure unless
    /// the role is only ever set at a display size.
    /// </summary>
    public static TheoryData<Theme, string, string, double> TextPairings()
    {
        var data = new TheoryData<Theme, string, string, double>();

        foreach (var theme in LiteralThemes)
        {
            foreach (var surface in new[] { "SurfaceCanvas", "SurfaceCard" })
            {
                data.Add(theme, "TextPrimary", surface, 4.5);
                data.Add(theme, "TextBody", surface, 4.5);
                data.Add(theme, "TextSecondary", surface, 4.5);
                data.Add(theme, "AccentText", surface, 4.5);
                data.Add(theme, "DangerText", surface, 4.5);
            }

            data.Add(theme, "AccentOn", "AccentFill", 4.5);
            data.Add(theme, "DangerOn", "DangerFill", 4.5);
            data.Add(theme, "BandInk", "SurfaceBand", 4.5);
            data.Add(theme, "BandInkSoft", "SurfaceBand", 4.5);
            data.Add(theme, "BandInk", "SurfaceBandRaised", 4.5);
        }

        return data;
    }

    /// <summary>
    /// Marks that identify a control or its state. These carry no text, so the
    /// non-text figure applies rather than the reading one.
    ///
    /// The status tones are here, not in the text table, because a tone is
    /// only ever a ring, a glyph or a dot. Every place one appears, the state
    /// is also written beside it in words set in a text role.
    /// </summary>
    public static TheoryData<Theme, string, string> NonTextPairings()
    {
        var data = new TheoryData<Theme, string, string>();

        foreach (var theme in LiteralThemes)
        {
            data.Add(theme, "ControlBorder", "SurfaceCanvas");
            data.Add(theme, "ControlBorder", "SurfaceCard");
            data.Add(theme, "FocusRing", "SurfaceCanvas");
            data.Add(theme, "FocusRing", "SurfaceCard");
            data.Add(theme, "FocusVisualPrimaryBrush", "SurfaceCanvas");

            // The band is dark in both themes, so its tones are measured
            // against it in both themes.
            foreach (var tone in BandTones)
            {
                data.Add(theme, tone, "SurfaceBand");
            }

            // The chip and the banner icon sit on the cream surfaces.
            foreach (var tone in CanvasTones)
            {
                data.Add(theme, tone, "SurfaceCanvas");
                data.Add(theme, tone, "SurfaceCard");
            }
        }

        return data;
    }

    private static readonly string[] CanvasTones =
        ["StatusIdle", "StatusActive", "StatusCaution", "StatusFault"];

    private static readonly string[] BandTones =
        ["BandToneIdle", "BandToneActive", "BandToneCaution", "BandToneFault"];

    [Theory]
    [MemberData(nameof(TextPairings))]
    public void Text_meets_its_contrast_threshold(
        Theme theme, string foreground, string background, double threshold)
    {
        var ratio = Ratio(theme, foreground, background);

        Assert.True(
            ratio >= threshold,
            $"{theme}: {foreground} on {background} is {ratio:F2}:1, needs {threshold:F1}:1");
    }

    [Theory]
    [MemberData(nameof(NonTextPairings))]
    public void Control_marks_meet_the_non_text_threshold(Theme theme, string mark, string background)
    {
        var ratio = Ratio(theme, mark, background);

        Assert.True(ratio >= 3.0, $"{theme}: {mark} on {background} is {ratio:F2}:1, needs 3:1");
    }

    /// <summary>
    /// Danger must not look like the accent, or a destructive action stops
    /// reading as destructive. Contrast cannot express this: two colours of
    /// equal luminance have a ratio of 1 whether they match or clash. This is
    /// perceptual distance, and 15 sits far above the roughly 2.3 that is one
    /// just-noticeable difference.
    /// </summary>
    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Danger_and_every_status_tone_are_distinguishable_from_the_accent(Theme theme)
    {
        var accent = Require(theme, "AccentFill");

        foreach (var role in CanvasTones.Concat(BandTones).Concat(["DangerFill", "DangerText"]))
        {
            var distance = Colour.Distance(accent, Require(theme, role));

            Assert.True(
                distance >= 15.0,
                $"{theme}: {role} is only {distance:F1} from the accent, needs 15");
        }
    }

    /// <summary>
    /// A role missing from one theme falls back to whatever the platform
    /// supplies there, which is how a theme ends up half designed.
    /// </summary>
    [Fact]
    public void Every_theme_defines_the_same_roles()
    {
        var light = Corpus.ThemeEntries[Theme.Light].Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var theme in Enum.GetValues<Theme>())
        {
            var actual = Corpus.ThemeEntries[theme].Keys.ToHashSet(StringComparer.Ordinal);

            Assert.True(
                light.SetEquals(actual),
                $"{theme} differs from Light. Missing: [{string.Join(", ", light.Except(actual).Order())}]. "
                + $"Extra: [{string.Join(", ", actual.Except(light).Order())}]");
        }
    }

    /// <summary>
    /// Under forced colours the user's chosen pair wins outright, so no role
    /// may carry a colour of its own there.
    /// </summary>
    [Fact]
    public void High_contrast_defers_every_role_to_the_system()
    {
        var literals = Corpus.ThemeEntries[Theme.HighContrast]
            .Where(entry => Colour.TryParse(entry.Value) is not null)
            .Select(entry => $"{entry.Key}={entry.Value}")
            .Order();

        Assert.Empty(literals);
    }

    /// <summary>
    /// A key that resolves nowhere is a blank region or a crash on Windows.
    /// Nothing in a Linux compile catches it, so it is caught here.
    /// </summary>
    [Fact]
    public void Every_resource_reference_resolves()
    {
        var unresolved = Corpus.UnresolvedReferences()
            .Select(entry => $"{entry.File} -> {entry.Key}")
            .Order();

        Assert.Empty(unresolved);
    }

    /// <summary>
    /// One definition per colour. A literal outside the token file is a value
    /// that will not follow a theme change and will not be found by anyone
    /// looking for it.
    /// </summary>
    [Fact]
    public void Only_the_token_file_names_a_colour()
    {
        var offenders = Corpus.Files
            .Where(static file => file.RelativePath != "Design/Tokens.xaml")
            .SelectMany(static file => file.LiteralColours.Select(colour => $"{file.RelativePath}: {colour}"))
            .Order();

        Assert.Empty(offenders);
    }

    private static readonly double[] SpacingScale = [0, 4, 8, 12, 16, 24, 32, 48];

    private static readonly double[] RadiusScale = [0, 4, 6, 8, 12, 16, 999];

    private static readonly Regex SpacingAttribute = new(
        @"\b(?<name>Spacing|Padding|Margin|RowSpacing|ColumnSpacing|MinRowSpacing|MinColumnSpacing)=""(?<value>[0-9][0-9,. ]*)""",
        RegexOptions.Compiled);

    private static readonly Regex RadiusAttribute =
        new(@"\bCornerRadius=""(?<value>[0-9][0-9,. ]*)""", RegexOptions.Compiled);

    [Fact]
    public void Every_spacing_and_radius_value_comes_from_its_scale()
    {
        var offenders = Corpus.Files
            .SelectMany(file => OffScale(file, SpacingAttribute, SpacingScale, "spacing")
                .Concat(OffScale(file, RadiusAttribute, RadiusScale, "radius")))
            .Order();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> OffScale(
        XamlFile file, Regex attribute, double[] scale, string label) =>
        from match in attribute.Matches(file.Text).Cast<Match>()
        from component in match.Groups["value"].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
        let parsed = double.Parse(component, System.Globalization.CultureInfo.InvariantCulture)
        where !scale.Contains(parsed)
        select $"{file.RelativePath}: {label} {parsed} is not on the scale";

    private static double Ratio(Theme theme, string foreground, string background) =>
        Colour.Contrast(Require(theme, foreground), Require(theme, background));

    private static Colour Require(Theme theme, string role) =>
        Corpus.Resolve(theme, role)
        ?? throw new InvalidOperationException($"{theme} defines no literal colour for {role}");
}

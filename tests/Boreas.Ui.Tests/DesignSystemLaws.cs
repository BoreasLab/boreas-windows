using System.Text.RegularExpressions;

namespace Boreas.Ui.Tests;

/// <summary>
/// Visual-system laws checked against XAML.
/// </summary>
/// <remarks>
/// The corpus is built once; each law is a pure predicate over it.
/// </remarks>
public sealed class DesignSystemLaws
{
    private static readonly DesignCorpus Corpus = DesignCorpus.Load();

    /// <summary>Themes that define literal colours, so contrast is computable.</summary>
    private static readonly Theme[] LiteralThemes = [Theme.Light, Theme.Dark];

    /// <summary>
    /// Text pairings and their required contrast ratios.
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
    /// Non-text control marks and status tones with their contrast backgrounds.
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

            // The band is dark in both themes.
            foreach (var tone in BandTones)
            {
                data.Add(theme, tone, "SurfaceBand");
            }

            // Chip and banner icons sit on canvas surfaces.
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
    /// Perceptual distance keeps danger and status tones distinct from accent.
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
    /// Every theme defines the same roles.
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
    /// High contrast defers every colour role to the system.
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
    /// Every resource reference resolves; Linux compilation cannot catch missing
    /// Windows resource keys.
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
    /// Colour literals belong only in the token file.
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

using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// A release triple.
/// </summary>
/// <remarks>
/// <para>
/// <b>Field order is the precedence law.</b> SemVer 2.0.0 compares major, then
/// minor, then patch, each numerically, and <see cref="CompareTo"/> delegates to
/// a tuple whose element order is that order. What could disagree with the spec
/// has nowhere to live: there is no hand-written ladder of comparisons to get
/// out of step, and adding a field to the tuple in the wrong place is a
/// compile-time shape change rather than a silent reordering.
/// </para>
/// <para>
/// There is no public numeric constructor. Every value comes from
/// <see cref="TryParseTriple"/>, <see cref="TryParseTag"/>,
/// <see cref="Origin"/>, or <see cref="Successor"/>, so a triple in hand has
/// already passed the grammar. <c>default</c> is <see cref="Origin"/>, which is
/// the identity of <c>max</c> and therefore the right thing for a struct's zero
/// to mean.
/// </para>
/// </remarks>
public readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>
    /// The identity of <c>max</c> over releases: a repository that has never
    /// shipped.
    /// </summary>
    public static ReleaseVersion Origin => default;

    /// <summary>
    /// The next patch: what a build published between releases works toward.
    /// </summary>
    /// <remarks>
    /// Checked, because the alternative to throwing on a patch of
    /// <see cref="int.MaxValue"/> is wrapping to a version below every release
    /// that came before it.
    /// </remarks>
    public ReleaseVersion Successor => new(Major, Minor, checked(Patch + 1));

    /// <summary>
    /// Strictly <c>v</c> and a triple. Everything else - a pre-release tag
    /// included - is not a release and does not participate in the fold that
    /// picks the base version.
    /// </summary>
    public static ReleaseVersion? TryParseTag(string? tag) =>
        tag is ['v', .. var rest] ? TryParseTriple(rest) : null;

    /// <summary>
    /// Three numeric identifiers.
    /// </summary>
    /// <remarks>
    /// SemVer forbids a leading zero in a numeric identifier, so <c>01</c> is a
    /// refusal rather than a 1. <see cref="int.TryParse"/> alone would accept
    /// it, and would also accept a leading <c>+</c> and surrounding space, so
    /// the stricter grammar is checked before it runs.
    /// </remarks>
    public static ReleaseVersion? TryParseTriple(string? text)
    {
        if (text?.Split('.') is not [var major, var minor, var patch])
        {
            return null;
        }

        return (NumericIdentifier(major), NumericIdentifier(minor), NumericIdentifier(patch)) is
            ({ } m, { } n, { } p)
            ? new ReleaseVersion(m, n, p)
            : null;
    }

    private static int? NumericIdentifier(string text) =>
        text.Length > 0
        && text.All(char.IsAsciiDigit)
        && (text.Length == 1 || text[0] != '0')
        && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// SemVer precedence, delegated to the tuple so field order is the only
    /// place the ordering is stated.
    /// </summary>
    public int CompareTo(ReleaseVersion other) =>
        (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}

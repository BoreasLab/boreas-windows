using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// A UTC instant rendered <c>yyyy-mm-dd.hh-mm-ss</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed width and zero-padded, and that is the monotonicity law.</b> SemVer
/// compares a hyphen-bearing identifier as ASCII, not as a number, so lexical
/// order on the rendering has to equal chronological order on the instant.
/// Unpadded, <c>9-30-00</c> sorts above <c>11-30-00</c> and two builds an hour
/// apart come back reversed.
/// </para>
/// <para>
/// The padding is written as a format specifier per field rather than through
/// one composite format string, so the thing the ordering depends on is visible
/// at the point it is applied. Invariant culture because the repository does not
/// set <c>InvariantGlobalization</c>, and a tag is not a thing that renders
/// differently for a reader in another locale.
/// </para>
/// </remarks>
public readonly record struct BuildStamp : IComparable<BuildStamp>
{
    private BuildStamp(DateTimeOffset instant) => Instant = instant;

    /// <summary>Always UTC: the constructor converts, so there is no other case.</summary>
    public DateTimeOffset Instant { get; }

    /// <summary>
    /// Normalises to UTC, so an offset that arrived from somewhere else cannot
    /// render as a different wall clock than it compares as.
    /// </summary>
    public static BuildStamp At(DateTimeOffset instant) => new(instant.ToUniversalTime());

    public static BuildStamp Now() => At(DateTimeOffset.UtcNow);

    public int CompareTo(BuildStamp other) => Instant.CompareTo(other.Instant);

    public static bool operator <(BuildStamp left, BuildStamp right) => left.CompareTo(right) < 0;

    public static bool operator >(BuildStamp left, BuildStamp right) => left.CompareTo(right) > 0;

    public static bool operator <=(BuildStamp left, BuildStamp right) => left.CompareTo(right) <= 0;

    public static bool operator >=(BuildStamp left, BuildStamp right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var utc = Instant;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{utc.Year:D4}-{utc.Month:D2}-{utc.Day:D2}.{utc.Hour:D2}-{utc.Minute:D2}-{utc.Second:D2}");
    }
}

/// <summary>
/// Seven hex digits of the commit, rendered with a leading <c>g</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The prefix is not decoration.</b> SemVer compares an identifier of only
/// digits <i>numerically</i> and ranks it below every alphanumeric one, so a
/// commit that abbreviates to <c>0012345</c> would sort beneath its siblings.
/// The <c>g</c> makes the identifier alphanumeric always, and it is what
/// <c>git describe</c> already writes, so the tag matches what the tool a reader
/// reaches for produces.
/// </para>
/// <para>
/// Lower-cased on the way in. Git writes lower-case, and two spellings of one
/// commit would be two tags naming the same build.
/// </para>
/// </remarks>
public readonly record struct CommitSha
{
    /// <summary>What <c>git describe</c> abbreviates to by default.</summary>
    public const int Digits = 7;

    private readonly string? _abbreviated;

    private CommitSha(string abbreviated) => _abbreviated = abbreviated;

    /// <summary>
    /// A full or abbreviated hex object name, of which the first seven digits
    /// are kept. Anything that is not entirely hex is not a commit.
    /// </summary>
    public static CommitSha? TryParse(string? full) =>
        full is { Length: >= Digits } text && text.All(char.IsAsciiHexDigit)
            ? new CommitSha(text[..Digits].ToLowerInvariant())
            : null;

    public override string ToString() => $"g{_abbreviated ?? new string('0', Digits)}";
}

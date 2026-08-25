using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// What <c>git describe</c> says about where HEAD sits relative to the last
/// release.
/// </summary>
/// <remarks>
/// The command is
/// <c>git describe --tags --match 'v*.*.*' --exclude '*-*'</c>. The exclusion is
/// load-bearing: every push to main creates a pre-release tag, and those match
/// <c>v*.*.*</c> too, so without it <c>describe</c> answers with last night's
/// nightly and the offset becomes "commits since the last build" rather than
/// "commits since the last release". A release triple contains no hyphen, so
/// excluding every tag that does keeps exactly the releases.
/// </remarks>
public readonly record struct Described(ReleaseVersion Base, int Offset, CommitSha? Sha)
{
    /// <summary>
    /// Reads <c>v0.1.0</c> or <c>v0.1.0-7-g1a2b3c4</c>.
    /// </summary>
    /// <remarks>
    /// Split from the right, because the tag on the left is the only part whose
    /// shape is already known: it is a release triple, so it contains no hyphen,
    /// and the two fields describe appends do.
    /// </remarks>
    public static Described? TryParse(string? text)
    {
        if (text?.Trim() is not { Length: > 0 } described)
        {
            return null;
        }

        // The exact-tag case: HEAD is the release.
        if (ReleaseVersion.TryParseTag(described) is { } exact)
        {
            return new Described(exact, 0, null);
        }

        var lastHyphen = described.LastIndexOf('-');

        if (lastHyphen < 0)
        {
            return null;
        }

        var previousHyphen = described.LastIndexOf('-', lastHyphen - 1);

        if (previousHyphen < 0)
        {
            return null;
        }

        var sha = described[(lastHyphen + 1)..];

        return ReleaseVersion.TryParseTag(described[..previousHyphen]) is { } tag
               && int.TryParse(
                   described[(previousHyphen + 1)..lastHyphen],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var offset)
               && sha is ['g', .. var digits]
               && CommitSha.TryParse(digits) is { } commit
            ? new Described(tag, offset, commit)
            : null;
    }
}

using System.Reflection;

namespace Boreas.Ui.Contracts;

/// <summary>
/// Which application, built against which core.
/// </summary>
/// <remarks>
/// A pinned core release means neither the app version nor the core version
/// alone identifies what ran. These strings preserve the full build context,
/// including commit and core-release details that a four-part file version
/// cannot carry. The release notes use the same values produced by the build,
/// so they stay aligned without manual duplication.
/// </remarks>
public sealed record BuildIdentity(string App, string Position, string Core)
{
    /// <summary>
    /// What a build the release pipeline did not name says about itself.
    /// </summary>
    /// <remarks>
    /// A developer build carrying 0.0.0 and "local build" is visibly not a
    /// shipped version.
    /// </remarks>
    public const string Unstamped = "local build";

    /// <summary>
    /// Reads what the build stamped into the assembly.
    /// </summary>
    /// <remarks>
    /// <see cref="AssemblyInformationalVersionAttribute"/> carries the full
    /// SemVer tag; the other two travel as
    /// <see cref="AssemblyMetadataAttribute"/> because there is no dedicated
    /// attribute for "the dependency this was composed with" and inventing a
    /// file to hold it would be a second thing to keep in step.
    /// </remarks>
    public static BuildIdentity Read(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value);

        return new BuildIdentity(
            App: assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                 ?? assembly.GetName().Version?.ToString()
                 ?? Unstamped,
            Position: Value(metadata, "BoreasPosition"),
            Core: Value(metadata, "BoreasCoreTag"));
    }

    private static string Value(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : Unstamped;

    /// <summary>
    /// The two lines, aligned so the versions line up under each other.
    /// </summary>
    /// <remarks>
    /// Identical to what the release notes carry, because a reader comparing a
    /// support bundle against a release page should not have to translate
    /// between two spellings of one fact.
    /// </remarks>
    public override string ToString() =>
        $"app  {App}  ({Position}){Environment.NewLine}core {Core}";
}

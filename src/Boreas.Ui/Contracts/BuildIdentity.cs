using System.Reflection;

namespace Boreas.Ui.Contracts;

/// <summary>
/// Which application, built against which core.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bug report has to map to one of these pairs or it maps to nothing.</b>
/// This artefact embeds a pinned boreas-core release, so neither version alone
/// identifies what ran: the app version names a range of cores, and the core
/// version names a range of apps.
/// </para>
/// <para>
/// All three are strings, deliberately. The four-part file version cannot say
/// "seven commits after v0.1.0, built against a core cut at 09:14", and that
/// sentence is the whole content of a version that is not a release.
/// </para>
/// <para>
/// The same two lines appear in the release notes, produced from the same
/// values by the same tool. They are stamped in at build time and read back
/// here, so there is nothing to keep in step by hand.
/// </para>
/// </remarks>
public sealed record BuildIdentity(string App, string Position, string Core)
{
    /// <summary>
    /// What a build the release pipeline did not name says about itself.
    /// </summary>
    /// <remarks>
    /// Visibly not a version. A developer's build carrying 0.0.0 and "local
    /// build" is one nobody will mistake for something that shipped.
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

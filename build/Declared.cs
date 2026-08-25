using System.Xml.Linq;

namespace Boreas.Build;

/// <summary>
/// The properties this repository declares about itself, read from
/// <c>Directory.Build.props</c>.
/// </summary>
/// <remarks>
/// <para>
/// One file, read by both ends. MSBuild reads it natively so every project
/// inherits the version, and this reads it so the release gate has something to
/// compare a tag against. A second copy of either value is a second thing that
/// can be stale on the day it matters.
/// </para>
/// <para>
/// Parsed with the BCL's XML reader rather than scanned. A props file is XML,
/// the parser is already present, and the property must come from a
/// <c>PropertyGroup</c> directly under the root - a <c>Version</c> nested
/// inside an item or a target is not this repository's declaration.
/// </para>
/// </remarks>
public static class Declared
{
    /// <summary>
    /// The version a build with no tag carries.
    /// </summary>
    /// <remarks>
    /// <b>Never consulted by the algebra.</b> The tag is the version; this is
    /// the fallback MSBuild demands for a build the pipeline did not name, and
    /// it reads 0.0.0 so that a build carrying it is self-evidently a local one.
    /// </remarks>
    public const string VersionProperty = "Version";

    /// <summary>The pinned boreas-core release this build composes with.</summary>
    public const string CoreTagProperty = "BoreasCoreTag";

    /// <summary>
    /// One property's value, or null when the file does not declare it.
    /// </summary>
    /// <remarks>
    /// Matched by local name, so a props file carrying the legacy MSBuild
    /// namespace reads the same as one without it. The last declaration wins,
    /// which is MSBuild's own rule for repeated properties in one file.
    /// </remarks>
    public static string? Property(string? xml, string name)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            // Not a props file. Indistinguishable from one with no such
            // property, and identical in effect.
            return null;
        }

        return document.Root?
            .Elements().Where(static e => e.Name.LocalName == "PropertyGroup")
            .Elements().Where(e => e.Name.LocalName == name)
            .Select(static e => e.Value.Trim())
            .LastOrDefault();
    }

    /// <summary>
    /// The fallback version, or null when the file declares none that is a
    /// strict triple.
    /// </summary>
    public static ReleaseVersion? Version(string? xml) =>
        ReleaseVersion.TryParseTriple(Property(xml, VersionProperty));

    /// <summary>The pinned core tag, verbatim.</summary>
    public static string? CoreTag(string? xml) => Property(xml, CoreTagProperty);
}

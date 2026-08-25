using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// A Win32 <c>VERSIONINFO</c> quad: four <c>WORD</c> fields.
/// </summary>
public readonly record struct WindowsVersion : IComparable<WindowsVersion>
{
    /// <summary>Each field is a <c>WORD</c>.</summary>
    public const int MaxField = ushort.MaxValue;

    private WindowsVersion(int major, int minor, int build, int revision)
    {
        Major = major;
        Minor = minor;
        Build = build;
        Revision = revision;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Build { get; }

    public int Revision { get; }

    public static WindowsVersion? TryCreate(int major, int minor, int build, int revision) =>
        InRange(major) && InRange(minor) && InRange(build) && InRange(revision)
            ? new WindowsVersion(major, minor, build, revision)
            : null;

    private static bool InRange(int field) => field is >= 0 and <= MaxField;

    public int CompareTo(WindowsVersion other) =>
        (Major, Minor, Build, Revision).CompareTo((other.Major, other.Minor, other.Build, other.Revision));

    public static bool operator <(WindowsVersion left, WindowsVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(WindowsVersion left, WindowsVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(WindowsVersion left, WindowsVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(WindowsVersion left, WindowsVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Build}.{Revision}");
}

/// <summary>
/// How a <see cref="Publish"/> is written into the three version fields a .NET
/// assembly carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from the algebra on purpose.</b> The tag scheme is a statement
/// about what is being published; this is a statement about how one particular
/// distribution format wants to hear it. They change for different reasons, and
/// keeping them apart means a change of format costs this file and no
/// retesting of the laws.
/// </para>
/// <para>
/// <b>The distribution format today is an unpackaged .exe.</b>
/// <c>Boreas.Ui.csproj</c> sets <c>WindowsPackageType=None</c> with
/// <c>OutputType=WinExe</c>, there is no <c>.appxmanifest</c> in the tree and no
/// installer project, so nothing compares these numbers to decide an upgrade.
/// They are what a user sees in the file properties dialog and what a crash
/// dump carries.
/// </para>
/// <para>
/// <b>Both installer formats would refuse this encoding, and both were
/// checked.</b> Windows Installer: "The first field is the major version and
/// has a maximum value of 255. The second field is the minor version and has a
/// maximum value of 255… Note that Windows Installer uses only the first three
/// fields of the product version. If you include a fourth field in your product
/// version, the installer ignores the fourth field." So an MSI would discard
/// <c>Revision</c> outright and every pre-release of one patch would compare
/// equal - the upgrade from one nightly to the next would simply not happen.
/// MSIX is the mirror image: "the last (fourth) section of the version number is
/// reserved for Store use and must be left as 0… (except for the first section,
/// which cannot be 0)", which forbids both the field this uses and this
/// product's major version. <b>Neither format can carry this scheme, so
/// choosing one is a decision that changes this file</b> - most likely by moving
/// the counter into <see cref="WindowsVersion.Build"/> and the triple's patch
/// elsewhere.
/// </para>
/// </remarks>
public static class Rendering
{
    /// <summary>
    /// The revision a release carries: the field's maximum, so that a release
    /// sorts above every pre-release that preceded it.
    /// </summary>
    /// <remarks>
    /// Pre-releases of version V share V's triple, so the release of V must be
    /// distinguished in the fourth field alone. Giving it the maximum is the
    /// only assignment that holds however many pre-releases came first, and
    /// without knowing how many that will be.
    /// </remarks>
    public const int ReleaseRevision = WindowsVersion.MaxField;

    /// <summary>
    /// <c>major.minor.patch.n</c>, where <c>n</c> counts commits since the last
    /// release tag and a release takes the field's maximum.
    /// </summary>
    /// <returns>
    /// Null when a component will not fit, which is a refusal rather than a
    /// wrap: a version that silently truncated would sort below its predecessor.
    /// </returns>
    public static WindowsVersion? TryFileVersion(Publish publish, int commitsSinceRelease)
    {
        var version = publish.Version;

        var revision = publish switch
        {
            Publish.Release => ReleaseRevision,

            // Strictly below a release's, so a pre-release never ties with the
            // release it precedes. Equality would be two artefacts wearing one
            // version.
            _ when commitsSinceRelease < ReleaseRevision => commitsSinceRelease,
            _ => -1,
        };

        return WindowsVersion.TryCreate(version.Major, version.Minor, version.Patch, revision);
    }

    /// <summary>
    /// <c>major.minor.0.0</c>: held stable within a minor.
    /// </summary>
    /// <remarks>
    /// This is a binding identity, not a build stamp. Moving it on every build
    /// would make every assembly reference a distinct one, which is the problem
    /// strong-name binding policy exists to work around rather than a thing to
    /// create.
    /// </remarks>
    public static WindowsVersion AssemblyVersion(Publish publish) =>
        WindowsVersion.TryCreate(publish.Version.Major, publish.Version.Minor, 0, 0)
        ?? throw new InvalidOperationException(
            $"{publish.Version} has a major or minor beyond {WindowsVersion.MaxField}.");

    /// <summary>
    /// The full SemVer, verbatim and without the <c>v</c>.
    /// </summary>
    /// <remarks>
    /// A free-form string, and the only field that can carry a pre-release
    /// identifier at all. It is what the about box shows and what a crash
    /// report should quote, because it is the only rendering from which the
    /// tag - and therefore the build - can be recovered exactly.
    /// </remarks>
    public static string InformationalVersion(Publish publish) => publish.Tag[1..];
}

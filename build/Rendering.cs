using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// A Win32 <c>VERSIONINFO</c> quad: four <c>WORD</c> fields.
/// </summary>
/// <remarks>
/// C# checks assembly identity ranges but not file-version ranges:
/// <c>-p:FileVersion=65536.0.0.0</c> builds without complaint. The resource can
/// silently lose the extra bits, so <see cref="TryCreate"/> rejects values that
/// do not fit.
/// </remarks>
public readonly record struct WindowsVersion : IComparable<WindowsVersion>
{
    /// <summary>
    /// Each field is a <c>WORD</c> with no reserved value.
    /// </summary>
    /// <remarks>
    /// <see cref="AssemblyIdentityVersion.MaxField"/> is one lower because an
    /// assembly identity reserves a value for an unspecified component. The
    /// separate ceilings prevent applying that rule to a file version.
    /// </remarks>
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
/// The version part of a CLI assembly identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two fields, not four.</b> This scheme holds build and revision at zero,
/// so a four-field type would have to assert that two of them are - and a test
/// asserting a field is zero is a test of something a two-field type simply
/// cannot say. The zeroes appear once, in <see cref="ToString"/>, which is the
/// only place they are real.
/// </para>
/// <para>
/// <b>The ceiling is one below <see cref="WindowsVersion.MaxField"/>, and the
/// asymmetry is the difference between a name and a label.</b> An assembly
/// identity must be able to say "this component is unspecified" - that is what
/// a partial reference like <c>Foo, Version=1.0</c> is built from - and with no
/// presence bit beside the four metadata <c>USHORT</c>s, <c>0xFFFF</c> is spent
/// as the sentinel. So Roslyn is handed <c>ushort.MaxValue - 1</c> as the
/// ceiling unconditionally, not only where a wildcard appears. A VERSIONINFO
/// <c>WORD</c> names nothing and therefore reserves nothing.
/// </para>
/// <para>
/// <b>Checked against the compiler, in every position.</b>
/// <c>-p:AssemblyVersion=65534.0.0.0</c>, <c>0.65534.0.0</c> and the rest build;
/// <c>65535</c> in <i>any</i> of the four is CS7034. A file version takes 65535
/// in any position and builds.
/// </para>
/// <para>
/// There is deliberately no <c>IComparable</c> and there are no comparison
/// operators. Nothing orders assembly versions: the four-field type carries them
/// only because file versions are ordered, and copying what has no use here
/// would invite somebody to find one.
/// </para>
/// </remarks>
public readonly record struct AssemblyIdentityVersion
{
    /// <summary>
    /// The largest value assembly metadata accepts in a component, from
    /// CS7034's bound.
    /// </summary>
    public const int MaxField = ushort.MaxValue - 1;

    private AssemblyIdentityVersion(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }

    public int Major { get; }

    public int Minor { get; }

    public static AssemblyIdentityVersion? TryCreate(int major, int minor) =>
        InRange(major) && InRange(minor) ? new AssemblyIdentityVersion(major, minor) : null;

    private static bool InRange(int field) => field is >= 0 and <= MaxField;

    /// <summary>
    /// <c>major.minor.0.0</c>. The trailing zeroes are the rendering, not state.
    /// </summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Major}.{Minor}.0.0");
}

/// <summary>
/// How a <see cref="Publish"/> is written into the three version fields a .NET
/// assembly carries.
/// </summary>
/// <remarks>
/// The tag algebra says what is being published; this class maps it to the
/// current distribution format, an unpackaged .exe. Keeping those rules here
/// lets a format change avoid retesting the tag laws.
///
/// <c>Boreas.Ui.csproj</c> sets <c>WindowsPackageType=None</c> with
/// <c>OutputType=WinExe</c>, there is no <c>.appxmanifest</c> in the tree and no
/// installer project, so nothing compares these numbers to decide an upgrade.
/// They are what a user sees in the file properties dialog and what a crash
/// dump carries.
///
/// Both installer formats would refuse this encoding. Windows Installer: "The
/// first field is the major version and
/// has a maximum value of 255. The second field is the minor version and has a
/// maximum value of 255… Note that Windows Installer uses only the first three
/// fields of the product version. If you include a fourth field in your product
/// version, the installer ignores the fourth field." So an MSI would discard
/// <c>Revision</c> outright and every pre-release of one patch would compare
/// equal, so the upgrade from one nightly to the next would not happen.
/// MSIX is the mirror image: "the last (fourth) section of the version number is
/// reserved for Store use and must be left as 0… (except for the first section,
/// which cannot be 0)", which forbids both the field this uses and this
/// product's major version. Neither format can carry this scheme; choosing one
/// would require moving the counter into <see cref="WindowsVersion.Build"/>
/// and the triple's patch elsewhere.
/// </remarks>
public static class Rendering
{
    /// <summary>
    /// The maximum field value, so a release sorts above every preceding
    /// pre-release.
    /// </summary>
    /// <remarks>
    /// Pre-releases of version V share V's triple, so the release must use the
    /// fourth field. The maximum remains greater than any number of preceding
    /// pre-releases without requiring their count in advance.
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

            // Keep pre-releases below the release revision so they never tie.
            _ when commitsSinceRelease < ReleaseRevision => commitsSinceRelease,
            _ => -1,
        };

        return WindowsVersion.TryCreate(version.Major, version.Minor, version.Patch, revision);
    }

    /// <summary>
    /// <c>major.minor.0.0</c>: held stable within a minor.
    /// </summary>
    /// <remarks>
    /// This is a binding identity, not a build stamp: changing it on every
    /// build gives each assembly reference a distinct referent. It cannot carry
    /// the release counter because <see cref="ReleaseRevision"/> is one past
    /// the assembly metadata ceiling. The two-field return type keeps the
    /// assembly and file-version ceilings separate.
    /// </remarks>
    public static AssemblyIdentityVersion AssemblyVersion(Publish publish) =>
        AssemblyIdentityVersion.TryCreate(publish.Version.Major, publish.Version.Minor)
        ?? throw new InvalidOperationException(
            $"{publish.Version} has a major or minor beyond {AssemblyIdentityVersion.MaxField}, "
            + "which assembly metadata will not accept.");

    /// <summary>
    /// The full SemVer, verbatim and without the <c>v</c>.
    /// </summary>
    /// <remarks>
    /// This free-form field alone carries a pre-release identifier. The about
    /// box and crash reports use it because it preserves the tag exactly.
    /// </remarks>
    public static string InformationalVersion(Publish publish) => publish.Tag[1..];
}

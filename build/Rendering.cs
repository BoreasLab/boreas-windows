using System.Globalization;

namespace Boreas.Build;

/// <summary>
/// A Win32 <c>VERSIONINFO</c> quad: four <c>WORD</c> fields.
/// </summary>
/// <remarks>
/// <b>Nothing else enforces this range.</b> The compiler range-checks assembly
/// identity and does not check a file version at all:
/// <c>-p:FileVersion=65536.0.0.0</c> builds without complaint. Whatever happens
/// to the extra bits happens silently in the resource, so
/// <see cref="TryCreate"/> is the only place a value that will not fit is
/// refused.
/// </remarks>
public readonly record struct WindowsVersion : IComparable<WindowsVersion>
{
    /// <summary>
    /// Each field is a <c>WORD</c>, and a <c>WORD</c> reserves nothing.
    /// </summary>
    /// <remarks>
    /// One below this is <see cref="AssemblyIdentityVersion.MaxField"/>, and the
    /// difference is not a quirk - see that type for why a name reserves a value
    /// and a label does not. The two ceilings live on two types precisely so
    /// that neither can be reached for where the other belongs.
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
    /// <para>
    /// This is a binding identity, not a build stamp. Moving it on every build
    /// would make every assembly reference a distinct one, which is the problem
    /// strong-name binding policy exists to work around rather than a thing to
    /// create.
    /// </para>
    /// <para>
    /// <b>It also could not carry the counter even if it should</b>, because
    /// <see cref="ReleaseRevision"/> is one past what assembly metadata accepts.
    /// That is why the return type is not the four-field quad: the two fields
    /// have different ceilings, and a shared constant that is right for one
    /// caller and wrong for the other is the bug rather than the convenience.
    /// </para>
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
    /// A free-form string, and the only field that can carry a pre-release
    /// identifier at all. It is what the about box shows and what a crash
    /// report should quote, because it is the only rendering from which the
    /// tag - and therefore the build - can be recovered exactly.
    /// </remarks>
    public static string InformationalVersion(Publish publish) => publish.Tag[1..];
}

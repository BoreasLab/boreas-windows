using System.Diagnostics;

namespace Boreas.Build;

/// <summary>
/// What triggered a publish.
/// </summary>
/// <remarks>
/// A sum rather than a boolean beside an optional string. <see cref="Release"/>
/// carries its tag and <see cref="Push"/> has none, so "a release event with no
/// tag" is not a state anything can be asked about - which is what removes the
/// <c>if [ "$IS_TAG" = true ]</c> from the workflow, along with the branch that
/// read it.
/// </remarks>
public abstract record TriggerEvent
{
    private TriggerEvent() { }

    /// <summary>A commit landed on the default branch.</summary>
    public sealed record Push : TriggerEvent
    {
        public static readonly Push Instance = new();
    }

    /// <summary>
    /// A human pushed a release tag.
    /// </summary>
    /// <remarks>
    /// It carries a <see cref="ReleaseVersion"/> and not the text it came from.
    /// <b>That is what makes the algebra total:</b> the one untrusted string
    /// becomes a version at the edge that receives it, so the function that
    /// reasons about versions has no malformed case to answer for.
    /// </remarks>
    public sealed record Release(ReleaseVersion Version) : TriggerEvent;

    public TResult Match<TResult>(
        Func<Push, TResult> push,
        Func<Release, TResult> release) => this switch
        {
            Push e => push(e),
            Release e => release(e),
            _ => throw new UnreachableException($"Unhandled {nameof(TriggerEvent)}: {this}"),
        };
}

/// <summary>What is being published.</summary>
/// <remarks>
/// <para>
/// <see cref="Version"/> is a field on the base rather than a positional
/// parameter on each case, and that is not a stylistic choice. A positional
/// <c>Version</c> on a derived record whose base already declares one
/// synthesises no property at all: the reference would bind to the base, and a
/// base that computed it by matching on the case would call itself forever. The
/// compiler warns (CS8907) and the warning is right.
/// </para>
/// <para>
/// The private constructor is what closes the hierarchy. A nested type may
/// reach it, so the two cases below can; nothing outside can add a third.
/// </para>
/// </remarks>
public abstract record Publish : IComparable<Publish>
{
    private Publish(ReleaseVersion version) => Version = version;

    /// <summary>The release triple, whichever kind of publish this is.</summary>
    public ReleaseVersion Version { get; }

    public sealed record Release(ReleaseVersion Of) : Publish(Of);

    public sealed record Pre(ReleaseVersion Of, BuildStamp Stamp, CommitSha Sha) : Publish(Of);

    /// <summary>
    /// The git tag, which is also the name of the release and of every artefact
    /// in it.
    /// </summary>
    public string Tag => this switch
    {
        Release => $"v{Version}",
        Pre p => $"v{Version}-dev.{p.Stamp}.{p.Sha}",
        _ => throw new UnreachableException($"Unhandled {nameof(Publish)}: {this}"),
    };

    /// <summary>
    /// Whether GitHub should mark this a pre-release, and so keep it out of
    /// "Latest".
    /// </summary>
    /// <remarks>
    /// A projection of the variant, never a field that could disagree with it.
    /// This is what makes <c>gh release download</c> with no tag return a real
    /// release and never a nightly.
    /// </remarks>
    public bool IsPrerelease => this is Pre;

    public TResult Match<TResult>(
        Func<Release, TResult> release,
        Func<Pre, TResult> pre) => this switch
        {
            Release p => release(p),
            Pre p => pre(p),
            _ => throw new UnreachableException($"Unhandled {nameof(Publish)}: {this}"),
        };

    /// <summary>
    /// SemVer 2.0.0 precedence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three clauses, in the order the specification gives them. The core
    /// version first. Then, at equal core versions, a pre-release ranks below
    /// the release - which is the whole reason a pre-release is numbered for the
    /// patch that has not happened yet.
    /// </para>
    /// <para>
    /// Two pre-releases of one core version compare by their identifiers, and
    /// <b>ordinal comparison of the tag is that comparison</b> for the shape
    /// this scheme produces. The identifiers are <c>dev</c>, the stamp, and the
    /// commit; all three are alphanumeric, so SemVer compares each as ASCII, and
    /// they are fixed width, so ASCII on the whole suffix equals ASCII
    /// field-by-field. Laws 2 and 3 are exactly what buy that: an unpadded stamp
    /// or an all-digit commit would break the equivalence and this clause with
    /// it.
    /// </para>
    /// </remarks>
    public int CompareTo(Publish? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byVersion = Version.CompareTo(other.Version);

        if (byVersion != 0)
        {
            return byVersion;
        }

        return (this, other) switch
        {
            (Pre, Release) => -1,
            (Release, Pre) => 1,
            (Release, Release) => 0,
            _ => string.CompareOrdinal(Tag, other.Tag),
        };
    }

    public static bool operator <(Publish left, Publish right) => left.CompareTo(right) < 0;

    public static bool operator >(Publish left, Publish right) => left.CompareTo(right) > 0;

    public static bool operator <=(Publish left, Publish right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Publish left, Publish right) => left.CompareTo(right) >= 0;
}

/// <summary>The whole algebra.</summary>
public static class Release
{
    /// <summary>
    /// Names what is being published, for every event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Total: there is no gate here and no refusal.</b> The tag is the
    /// version. An earlier design read a version out of the build files as well
    /// and refused a tag that differed from it, which made cutting a release two
    /// acts and made the one you forget the one that fails the build. That check
    /// existed only because there were two sources; one source makes the
    /// invariant hold by construction rather than by inspection.
    /// </para>
    /// <para>
    /// The refusal that remains lives at the parse boundary, where a ref that is
    /// not a release tag never becomes a <see cref="TriggerEvent.Release"/> in
    /// the first place. A malformed tag is therefore not a state this function
    /// can be asked about.
    /// </para>
    /// <para>
    /// MSBuild demands a <c>Version</c> property of its own, and this never
    /// consults it. It is the fallback a build with no tag carries, and nothing
    /// more; see Directory.Build.props for why it reads 0.0.0.
    /// </para>
    /// <para>
    /// O(n) in the tag count and O(1) in space. n is the number of releases a
    /// repository has ever cut, so the fold is over tens of values and the cost
    /// that matters is the <c>git tag</c> that produced them.
    /// </para>
    /// </remarks>
    public static Publish Resolve(
        TriggerEvent trigger,
        IEnumerable<string> tags,
        BuildStamp now,
        CommitSha sha) => trigger.Match<Publish>(
        push: _ => new Publish.Pre(BaseVersion(tags), now, sha),
        release: r => new Publish.Release(r.Version));

    /// <summary>
    /// The patch above the newest release, or the first patch when nothing has
    /// ever shipped.
    /// </summary>
    /// <remarks>
    /// One operand, folded from <see cref="ReleaseVersion.Origin"/>. A
    /// repository that has never shipped heads for 0.0.1, and if the next
    /// release should be a minor, the way to say so is to tag a minor.
    /// </remarks>
    public static ReleaseVersion BaseVersion(IEnumerable<string> tags) =>
        tags.Select(ReleaseVersion.TryParseTag)
            .Where(static version => version.HasValue)
            .Select(static version => version!.Value)
            .Append(ReleaseVersion.Origin)
            .Max()
            .Successor;
}

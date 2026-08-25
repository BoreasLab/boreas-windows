using Boreas.Build;

// The imperative shell. Everything decidable is decided in the algebra beside
// it; what is left here is reading the environment, asking git three questions,
// and writing lines a workflow can read.
//
// Every value it prints is one key on one line, deliberately. GITHUB_OUTPUT's
// multi-line form needs a delimiter that must not appear in the value, and the
// values here include a tag - the one thing on this path whose text a human
// chooses. Single lines have no delimiter to collide with.

return Run(args);

static int Run(string[] args)
{
    if (args is ["--help" or "-h"] or ["help"])
    {
        Usage();
        return 0;
    }

    if (args is not ([] or ["resolve"]))
    {
        Usage();
        return Refuse($"unknown argument: {string.Join(' ', args)}");
    }

    if (Git.Read("rev-parse", "--show-toplevel") is not { } root)
    {
        return Refuse("this must run inside the repository: git rev-parse --show-toplevel failed.");
    }

    var propsPath = Path.Combine(root, "Directory.Build.props");
    var props = File.Exists(propsPath) ? File.ReadAllText(propsPath) : null;

    if (Declared.CoreTag(props) is not { } coreTag)
    {
        return Refuse(
            $"{propsPath} declares no <{Declared.CoreTagProperty}>. It names the pinned "
            + "boreas-core release this build composes with, and a build that cannot say which "
            + "core it embedded is one no bug report can be mapped to.");
    }

    var head = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? Git.Read("rev-parse", "HEAD");

    if (CommitSha.TryParse(head) is not { } sha)
    {
        return Refuse($"HEAD is not a commit this can name: {head ?? "git gave nothing"}.");
    }

    // THE PARSE BOUNDARY, AND THE ONLY REFUSAL LEFT.
    //
    // GITHUB_REF_TYPE and GITHUB_REF_NAME arrive as text. Both become values
    // here, so what reaches the algebra is already a version and a commit and
    // the algebra is total. A tag that is not a release never becomes a release
    // event; there is no later point at which it could be caught, because there
    // is no later check to catch it.
    if (Trigger() is not { } trigger)
    {
        return Refuse(
            $"'{Environment.GetEnvironmentVariable("GITHUB_REF_NAME")}' is not a release tag. "
            + "A release is vMAJOR.MINOR.PATCH with no leading zeros, for example v0.4.2, and the "
            + "tag is the version - nothing has to be bumped to match it. Delete the tag and push "
            + "the right one. A pre-release tag is cut by a push to main and never by hand.");
    }

    return Emit(Release.Resolve(trigger, Git.Tags(), BuildStamp.Now(), sha), coreTag);
}

static int Emit(Publish publish, string coreTag)
{
    // Where HEAD sits relative to the last release. v0.0.0 is tagged on the
    // repository's first commit precisely so this is always defined: without it
    // the first build has nothing to describe against and the count has no
    // origin.
    if (Git.Describe() is not { } position)
    {
        return Refuse(
            "git describe found no release tag. This repository should carry v0.0.0 on its first "
            + "commit: it anchors the build counter and removes the empty-repository case. "
            + "Run: git tag v0.0.0 <first commit> && git push origin v0.0.0");
    }

    if (Rendering.TryFileVersion(publish, position.Offset) is not { } file)
    {
        return Refuse(
            $"a file version for {publish.Tag} at {position.Offset} commits since v{position.Base} "
            + $"will not fit four WORD fields, whose maximum is {WindowsVersion.MaxField}.");
    }

    // One key per line, in the order a reader would want them.
    Console.WriteLine($"tag={publish.Tag}");
    Console.WriteLine($"version={publish.Version}");
    Console.WriteLine($"prerelease={(publish.IsPrerelease ? "true" : "false")}");
    Console.WriteLine($"informational={Rendering.InformationalVersion(publish)}");
    Console.WriteLine($"file={file}");
    Console.WriteLine($"assembly={Rendering.AssemblyVersion(publish)}");
    Console.WriteLine($"position=v{position.Base} + {position.Offset}");
    Console.WriteLine($"core={coreTag}");

    return 0;
}

static int Refuse(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static void Usage() => Console.Error.WriteLine(
    """
    usage: dotnet run --project build -- [resolve]

    Names what this commit publishes and prints it as KEY=value lines, which is
    the shape GITHUB_OUTPUT reads.

      tag             the git tag, and the name of the release
      version         the release triple inside it
      prerelease      true for a build cut by a push to main
      informational   the full SemVer, for AssemblyInformationalVersion
      file            major.minor.patch.n, for FileVersion
      assembly        major.minor.0.0, for AssemblyVersion
      position        where HEAD sits relative to the last release
      core            the pinned boreas-core release this build composes with

    Exits 1 with a message naming the fix when the pushed ref is a tag that is
    not a release. That is the only refusal: the tag is the version, so there is
    nothing left for it to disagree with.
    """);

/// The event, read from what GitHub already sets.
///
/// GITHUB_REF_TYPE is "tag" exactly when a tag was pushed, so the workflow
/// plumbs nothing and there is no branch to get wrong. Null means the ref was a
/// tag that is not a release tag, which is the one thing here that can fail.
static TriggerEvent? Trigger() =>
    Environment.GetEnvironmentVariable("GITHUB_REF_TYPE") == "tag"
        ? ReleaseVersion.TryParseTag(Environment.GetEnvironmentVariable("GITHUB_REF_NAME")) is { } version
            ? new TriggerEvent.Release(version)
            : null
        : TriggerEvent.Push.Instance;

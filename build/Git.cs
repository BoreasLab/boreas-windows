using System.Diagnostics;

namespace Boreas.Build;

/// <summary>
/// The three questions this tool asks git.
/// </summary>
/// <remarks>
/// All of the impurity in one place, so the algebra beside it takes values and
/// the laws can be stated over them.
/// </remarks>
internal static class Git
{
    /// <summary>Every tag in the repository, one per line.</summary>
    /// <remarks>
    /// Unfiltered. Only strict triples survive the fold, so filtering here as
    /// well would put the definition of a release in two places.
    /// </remarks>
    public static IEnumerable<string> Tags() =>
        (Read("tag", "--list") ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Where HEAD sits relative to the newest release tag.
    /// </summary>
    /// <remarks>
    /// The exclusion is load-bearing. Every push to main creates a pre-release
    /// tag and those match <c>v*.*.*</c> too, so without it describe answers
    /// with last night's nightly and the offset becomes "commits since the last
    /// build" rather than "commits since the last release". A release triple
    /// contains no hyphen, so excluding every tag that does keeps exactly the
    /// releases.
    /// </remarks>
    public static Described? Describe() =>
        Described.TryParse(Read("describe", "--tags", "--match", "v*.*.*", "--exclude", "*-*"));

    /// <summary>Standard output of a git command, or null when it failed.</summary>
    public static string? Read(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);

        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 ? output.Trim() : null;
    }
}

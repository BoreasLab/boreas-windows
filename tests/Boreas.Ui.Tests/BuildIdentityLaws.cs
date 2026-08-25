using System.Reflection;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws for the pair a bug report has to map to.
/// </summary>
public sealed class BuildIdentityLaws
{
    /// <summary>
    /// The two lines, aligned, and identical to what the release notes carry.
    /// A reader comparing a support bundle against a release page should not
    /// have to translate between two spellings of one fact.
    /// </summary>
    [Fact]
    public void The_rendering_is_the_app_and_the_core_on_two_lines()
    {
        var identity = new BuildIdentity(
            App: "0.1.1-dev.2026-08-25.11-30-00.g1a2b3c4",
            Position: "v0.1.0 + 7",
            Core: "v0.1.0-dev.2026-08-25.09-14-02.ge18b70f");

        var lines = identity.ToString().Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("app  0.1.1-dev.2026-08-25.11-30-00.g1a2b3c4  (v0.1.0 + 7)", lines[0]);
        Assert.Equal("core v0.1.0-dev.2026-08-25.09-14-02.ge18b70f", lines[1]);
    }

    /// <summary>
    /// An assembly the pipeline did not stamp says so, in every field, rather
    /// than reporting a version nobody cut. The test assembly is exactly such a
    /// build, which is what makes this checkable at all.
    /// </summary>
    [Fact]
    public void An_unstamped_build_says_so_rather_than_guessing()
    {
        var identity = BuildIdentity.Read(Assembly.GetExecutingAssembly());

        Assert.Equal(BuildIdentity.Unstamped, identity.Core);
        Assert.Equal(BuildIdentity.Unstamped, identity.Position);
    }

    /// <summary>
    /// Nothing is ever empty. A blank line in a support bundle reads as a
    /// missing field rather than as an unstamped build.
    /// </summary>
    [Fact]
    public void No_field_is_ever_blank()
    {
        var identity = BuildIdentity.Read(Assembly.GetExecutingAssembly());

        Assert.All(
            new[] { identity.App, identity.Position, identity.Core },
            value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }
}

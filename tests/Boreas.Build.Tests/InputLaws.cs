using Boreas.Build;

namespace Boreas.Build.Tests;

/// <summary>
/// Laws for the two things read from outside the algebra: what
/// <c>git describe</c> says, and what the props file declares.
/// </summary>
public sealed class InputLaws
{
    [Theory]
    [InlineData("v0.1.0-7-g1a2b3c4", "0.1.0", 7)]
    [InlineData("v0.0.0-32-g2e46e0f", "0.0.0", 32)]
    [InlineData("v10.20.30-1-gabcdef0", "10.20.30", 1)]
    // HEAD is the release itself.
    [InlineData("v0.1.0", "0.1.0", 0)]
    [InlineData("  v0.1.0-7-g1a2b3c4  ", "0.1.0", 7)]
    public void A_description_yields_the_release_it_counts_from(string text, string expected, int offset)
    {
        var described = Assert.NotNull(Described.TryParse(text));

        Assert.Equal(Assert.NotNull(ReleaseVersion.TryParseTriple(expected)), described.Base);
        Assert.Equal(offset, described.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("v0.1.0-7")]
    [InlineData("v0.1.0-7-1a2b3c4")]
    [InlineData("v0.1.0-x-g1a2b3c4")]
    [InlineData("v0.1-7-g1a2b3c4")]
    public void Anything_else_is_not_a_description(string? text) =>
        Assert.Null(Described.TryParse(text));

    /// <summary>
    /// The property must come from a PropertyGroup directly under the root. A
    /// <c>Version</c> nested inside an item or a target is not this
    /// repository's declaration, and reading one would be reading somebody
    /// else's number.
    /// </summary>
    [Fact]
    public void A_property_comes_from_a_top_level_property_group()
    {
        const string Props = """
            <Project>
              <PropertyGroup>
                <Version>0.0.0</Version>
                <BoreasCoreTag>v0.1.0-dev.2026-08-25.03-35-12.ge88cbbd</BoreasCoreTag>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="xunit.v3" Version="9.9.9" />
              </ItemGroup>
              <Target Name="Something">
                <PropertyGroup>
                  <Version>7.7.7</Version>
                </PropertyGroup>
              </Target>
            </Project>
            """;

        Assert.Equal("0.0.0", Declared.Property(Props, Declared.VersionProperty));
        Assert.Equal("v0.1.0-dev.2026-08-25.03-35-12.ge88cbbd", Declared.CoreTag(Props));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("not xml at all <")]
    [InlineData("<Project></Project>")]
    public void A_file_that_declares_nothing_answers_nothing(string? xml)
    {
        Assert.Null(Declared.Property(xml, Declared.VersionProperty));
        Assert.Null(Declared.CoreTag(xml));
    }

    /// <summary>A props file carrying the legacy namespace reads the same.</summary>
    [Fact]
    public void The_legacy_msbuild_namespace_reads_the_same() =>
        Assert.Equal("v1.2.3", Declared.CoreTag(
            """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><BoreasCoreTag>v1.2.3</BoreasCoreTag></PropertyGroup>
            </Project>
            """));

    /// <summary>
    /// This repository's own props file, so a rename or a restructure of it is
    /// caught here rather than at the moment a release is cut.
    /// </summary>
    [Fact]
    public void This_repositorys_props_file_declares_what_the_pipeline_reads()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));

        // The fallback, which is a placeholder and visibly not a claim.
        Assert.Equal("0.0.0", Declared.Property(props, Declared.VersionProperty));

        // The pin, which must be a tag boreas-core could have cut.
        var core = Present.Value(Declared.CoreTag(props));
        Assert.StartsWith("v", core, StringComparison.Ordinal);
        Assert.Contains("-dev.", core, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return Present.Value(directory).FullName;
    }
}

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Boreas.Ui.Tests;

/// <summary>
/// Application XAML parsed once for the design laws.
/// </summary>
/// <remarks>
/// Laws read this value instead of touching the filesystem; parsing cost is
/// paid once per test run.
/// </remarks>
public sealed record DesignCorpus(
    ImmutableArray<XamlFile> Files,
    ImmutableDictionary<Theme, ImmutableDictionary<string, string>> ThemeEntries,
    ImmutableHashSet<string> GlobalKeys)
{
    /// <summary>Dictionaries that every file may reference.</summary>
    private static readonly ImmutableArray<string> GlobalDictionaries =
        ["Design/Tokens.xaml", "Design/Controls.xaml", "App.xaml"];

    /// <summary>
    /// Platform-supplied keys excluded from unresolved-key failures.
    /// </summary>
    private static readonly Regex PlatformKey = new(@"^SystemColor\w+$", RegexOptions.Compiled);

    private const string PlatformFontKey = "SymbolThemeFontFamily";

    private static readonly Regex BrushPattern =
        new(@"<SolidColorBrush\s+x:Key=""(?<key>[^""]+)""\s+Color=""(?<value>[^""]+)""",
            RegexOptions.Compiled);

    private static readonly Regex AliasEntryPattern =
        new(@"<StaticResource\s+x:Key=""(?<key>[^""]+)""\s+ResourceKey=""(?<target>[^""]+)""",
            RegexOptions.Compiled);

    private static readonly Regex ThemeBlockPattern =
        new(@"<ResourceDictionary x:Key=""(?<theme>Light|Dark|HighContrast)"">(?<body>.*?)\n        </ResourceDictionary>",
            RegexOptions.Compiled | RegexOptions.Singleline);

    public static DesignCorpus Load()
    {
        var source = SourceTree.Root;
        var files = Directory
            .EnumerateFiles(Path.Combine(source, "src"), "*.xaml", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => XamlFile.Read(source, path))
            .ToImmutableArray();

        var byRelativePath = files.ToImmutableDictionary(static file => file.RelativePath);

        var globalKeys = GlobalDictionaries
            .Where(byRelativePath.ContainsKey)
            .SelectMany(name => byRelativePath[name].DefinedKeys)
            .ToImmutableHashSet(StringComparer.Ordinal);

        return new DesignCorpus(files, ReadThemes(byRelativePath), globalKeys);
    }

    public XamlFile File(string relativePath) =>
        Files.Single(file => file.RelativePath == relativePath);

    /// <summary>
    /// Every reference in the corpus that no dictionary in scope defines.
    /// Scope is the file's own resources plus the merged global dictionaries,
    /// which is how XAML resolves a key at runtime.
    /// </summary>
    public IEnumerable<(string File, string Key)> UnresolvedReferences() =>
        from file in Files
        from key in file.ReferencedKeys
        where !GlobalKeys.Contains(key)
              && !file.DefinedKeys.Contains(key)
              && key != PlatformFontKey
              && !PlatformKey.IsMatch(key)
        select (file.RelativePath, key);

    /// <summary>
    /// Resolves a role to its literal colour in one theme, following alias
    /// entries. Returns null when the entry is a platform indirection rather
    /// than a literal, which is the expected answer under forced colours.
    /// </summary>
    public Colour? Resolve(Theme theme, string key)
    {
        var entries = ThemeEntries[theme];

        // Bound hops so cyclic aliases terminate.
        for (var hop = 0; hop <= entries.Count; hop++)
        {
            if (!entries.TryGetValue(key, out var value))
            {
                return null;
            }

            if (Colour.TryParse(value) is { } colour)
            {
                return colour;
            }

            if (!value.StartsWith("->", StringComparison.Ordinal))
            {
                return null;
            }

            key = value[2..];
        }

        return null;
    }

    private static ImmutableDictionary<Theme, ImmutableDictionary<string, string>> ReadThemes(
        ImmutableDictionary<string, XamlFile> byRelativePath)
    {
        var tokens = byRelativePath["Design/Tokens.xaml"].Text;

        return ThemeBlockPattern
            .Matches(tokens)
            .ToImmutableDictionary(
                match => Enum.Parse<Theme>(match.Groups["theme"].Value),
                match => ReadEntries(match.Groups["body"].Value));
    }

    private static ImmutableDictionary<string, string> ReadEntries(string body)
    {
        var brushes = BrushPattern
            .Matches(body)
            .Select(match => (Key: match.Groups["key"].Value, Value: match.Groups["value"].Value));

        // Mark aliases so resolution distinguishes colours from references.
        var aliases = AliasEntryPattern
            .Matches(body)
            .Select(match => (Key: match.Groups["key"].Value, Value: "->" + match.Groups["target"].Value));

        return brushes.Concat(aliases).ToImmutableDictionary(
            static entry => entry.Key,
            static entry => entry.Value,
            StringComparer.Ordinal);
    }
}

/// <summary>One XAML file, with every key it defines and every key it uses.</summary>
public sealed record XamlFile(
    string RelativePath,
    string Text,
    ImmutableHashSet<string> DefinedKeys,
    ImmutableHashSet<string> ReferencedKeys,
    ImmutableArray<string> LiteralColours)
{
    private static readonly Regex KeyPattern = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex ReferencePattern =
        new(@"\{(?:StaticResource|ThemeResource)\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private static readonly Regex AliasPattern = new(@"ResourceKey=""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex LiteralColourPattern = new(@"#[0-9a-fA-F]{6,8}\b", RegexOptions.Compiled);

    public static XamlFile Read(string root, string path)
    {
        var text = System.IO.File.ReadAllText(path);

        return new XamlFile(
            RelativePath: Path.GetRelativePath(Path.Combine(root, "src", "Boreas.Ui"), path)
                .Replace(Path.DirectorySeparatorChar, '/'),
            Text: text,
            DefinedKeys: KeyPattern.Matches(text)
                .Select(static match => match.Groups[1].Value)
                .ToImmutableHashSet(StringComparer.Ordinal),
            ReferencedKeys: ReferencePattern.Matches(text)
                .Select(static match => match.Groups[1].Value)
                .Concat(AliasPattern.Matches(text).Select(static match => match.Groups[1].Value))
                .ToImmutableHashSet(StringComparer.Ordinal),
            LiteralColours: LiteralColourPattern.Matches(text)
                .Select(static match => match.Value)
                .ToImmutableArray());
    }
}

public enum Theme
{
    Light,
    Dark,
    HighContrast,
}

/// <summary>Locates the repository root from the test assembly.</summary>
internal static class SourceTree
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "BoreasWindows.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root above " + AppContext.BaseDirectory);
    }
}

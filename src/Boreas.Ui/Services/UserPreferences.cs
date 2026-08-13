using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boreas.Ui.Services;

/// <summary>
/// The serializer contract for <see cref="Preferences"/>, generated at compile
/// time rather than discovered by reflection.
/// </summary>
/// <remarks>
/// Source generation is what makes this file safe to trim and to compile
/// ahead of time: the reflection-based overloads of
/// <see cref="JsonSerializer"/> have no way to tell the trimmer which members
/// survive, so they either keep everything or silently lose properties in a
/// trimmed build. It also moves the enum-to-string decision into the
/// generated context instead of allocating a converter per call.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Preferences))]
internal sealed partial class PreferenceJsonContext : JsonSerializerContext;

/// <summary>Which theme the user chose, or that they did not choose.</summary>
public enum ThemePreference
{
    /// <summary>Follow Windows. The default, and the right default.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// The app's own preferences, which are not tunnel configuration and never
/// travel over the control pipe.
/// </summary>
public sealed record Preferences(
    ThemePreference Theme = ThemePreference.System);

/// <summary>
/// Reads and writes <see cref="Preferences"/> under LOCALAPPDATA.
/// </summary>
/// <remarks>
/// A plain file, because this is an unpackaged application and there is no
/// package identity to hang ApplicationData off. Failure to read or write is
/// swallowed: a preference file is never worth failing to start over, and the
/// defaults are correct.
/// </remarks>
public sealed class PreferenceStore
{
    private readonly string _path;

    public PreferenceStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Boreas");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "preferences.json");
    }

    public Preferences Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize(File.ReadAllText(_path), PreferenceJsonContext.Default.Preferences)
                  ?? new Preferences()
                : new Preferences();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Preferences();
        }
    }

    public void Save(Preferences preferences)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences, PreferenceJsonContext.Default.Preferences));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A preference that fails to persist still applies this session.
        }
    }
}

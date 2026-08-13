using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boreas.Ui.Services;

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
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

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
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_path), Options) ?? new Preferences()
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
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A preference that fails to persist still applies this session.
        }
    }
}

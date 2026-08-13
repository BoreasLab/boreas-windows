using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Window-level state: the channel chip, the channel banner, and the theme.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IControlChannel _channel;
    private readonly PreferenceStore _store;

    public ShellViewModel(IControlChannel channel, PreferenceStore store, bool usingSampleData)
    {
        _channel = channel;
        _store = store;
        Preferences = store.Load();
        UsingSampleData = usingSampleData;

        Reconnect = new AsyncCommand(_channel.RefreshAsync);
        _channel.Changed += OnChannelChanged;
    }

    /// <summary>
    /// True when the window is showing invented values from
    /// <c>SampleControlChannel</c>. The window says so, permanently and
    /// visibly, because a screen that looks like a live tunnel and is not one
    /// is the most dangerous thing this application could display.
    /// </summary>
    public bool UsingSampleData { get; }

    public AsyncCommand Reconnect { get; }

    public ChannelPresentation Channel => ChannelPresentation.For(_channel.Channel);

    private Preferences Preferences { get; set; }

    public ThemePreference Theme
    {
        get => Preferences.Theme;
        set
        {
            if (Preferences.Theme == value)
            {
                return;
            }

            Preferences = Preferences with { Theme = value };
            _store.Save(Preferences);
            Raise();
            ThemeChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<ThemePreference>? ThemeChanged;

    private void OnChannelChanged(object? sender, EventArgs e) => Raise(nameof(Channel));

    public void Dispose() => _channel.Changed -= OnChannelChanged;
}

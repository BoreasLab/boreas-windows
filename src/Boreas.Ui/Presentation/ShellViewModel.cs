using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Window-level state: the channel chip and the channel banner.
/// </summary>
/// <remarks>
/// Appearance is intentionally absent: the window follows the system theme
/// instead of storing a separate preference.
/// </remarks>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IControlChannel _channel;

    public ShellViewModel(IControlChannel channel, bool usingSampleData)
    {
        _channel = channel;
        UsingSampleData = usingSampleData;

        Reconnect = new AsyncCommand(_channel.RefreshAsync);
        _channel.Changed += OnChannelChanged;
    }

    /// <summary>
    /// True when the window shows invented values from
    /// <c>SampleControlChannel</c>; the window marks them visibly.
    /// </summary>
    public bool UsingSampleData { get; }

    public AsyncCommand Reconnect { get; }

    public ChannelPresentation Channel => ChannelPresentation.For(_channel.Channel);

    private void OnChannelChanged(object? sender, EventArgs e) => Raise(nameof(Channel));

    public void Dispose() => _channel.Changed -= OnChannelChanged;
}

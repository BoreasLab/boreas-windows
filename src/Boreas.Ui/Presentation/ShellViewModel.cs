using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Window-level state: the channel chip and the channel banner.
/// </summary>
/// <remarks>
/// Appearance is not among them, and that is the design. A theme preference
/// was a third state beside the two the platform already has, stored in a file
/// that outlived the session it was chosen in: someone who picked Dark once
/// kept a dark window after switching Windows to light, with the control that
/// explains it two pages away and no reason left to look for it. The window
/// now sets no theme at all, so it inherits the system's and follows it when
/// it changes, which is both the behaviour people expect and one fewer state
/// this application can hold an opinion about.
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
    /// True when the window is showing invented values from
    /// <c>SampleControlChannel</c>. The window says so, permanently and
    /// visibly, because a screen that looks like a live tunnel and is not one
    /// is the most dangerous thing this application could display.
    /// </summary>
    public bool UsingSampleData { get; }

    public AsyncCommand Reconnect { get; }

    public ChannelPresentation Channel => ChannelPresentation.For(_channel.Channel);

    private void OnChannelChanged(object? sender, EventArgs e) => Raise(nameof(Channel));

    public void Dispose() => _channel.Changed -= OnChannelChanged;
}

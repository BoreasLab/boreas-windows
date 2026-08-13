using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

/// <summary>
/// Markup only. Appearance follows Windows and there is nothing here to hold.
/// </summary>
/// <remarks>
/// This page used to own a theme selector, a bound index, and a preference
/// written under LOCALAPPDATA. All three are gone: a stored choice survives the
/// reason it was made, so someone who picked Dark once kept a dark window after
/// switching Windows to light and had no reason left to come back here. The
/// window sets no theme, inherits the system's, and follows it when it changes.
/// </remarks>
public sealed partial class SettingsView : Page
{
    public SettingsView() => InitializeComponent();
}

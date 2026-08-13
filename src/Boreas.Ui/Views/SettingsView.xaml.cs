using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;
using Boreas.Ui.Services;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

public sealed partial class SettingsView : Page
{
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// The theme lives on the shared shell view model, because the window is
    /// what applies it. This page edits that one value rather than keeping a
    /// copy of it to synchronise.
    /// </summary>
    private static ShellViewModel Shell => App.Shell;

    public int ThemeIndex
    {
        get => Shell.Theme switch
        {
            ThemePreference.System => 0,
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => throw Unreachable.Value(Shell.Theme),
        };
        set => Shell.Theme = value switch
        {
            1 => ThemePreference.Light,
            2 => ThemePreference.Dark,
            _ => ThemePreference.System,
        };
    }
}

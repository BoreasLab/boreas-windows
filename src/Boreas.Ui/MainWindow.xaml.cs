using System.ComponentModel;
using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;
using Boreas.Ui.Services;
using Boreas.Ui.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Boreas.Ui;

public sealed partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Shell;

        // Named handlers, not lambdas, because both have to come off again and
        // an anonymous delegate cannot be removed. The shell outlives this
        // window, so a subscription left behind keeps the window alive with it.
        _viewModel.PropertyChanged += OnShellPropertyChanged;
        _viewModel.ThemeChanged += OnThemeChanged;

        Title = "Boreas";

        // A branded canvas rather than Mica. Mica tints from whatever is on
        // the desktop behind the window, which would make the canvas colour
        // indeterminate, and the canvas is the one surface every measured
        // contrast pairing in Tokens.xaml is measured against.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        TitleBar.SizeChanged += (_, _) => ReserveCaptionSpace();

        SampleMarker.Visibility = _viewModel.UsingSampleData ? Visibility.Visible : Visibility.Collapsed;

        ApplyTheme(_viewModel.Theme);
        ReserveCaptionSpace();
        RenderChannel();

        Navigate(NavigationSection.Status);

        App.NavigationRequested += OnNavigationRequested;
        Closed += OnClosed;
    }

    public ShellViewModel ViewModel => _viewModel;

    /// <summary>
    /// The one place this process shuts down. Every subscription taken in the
    /// constructor is released here, and then the application releases the
    /// channel and the shell that own the resources behind them.
    /// </summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        App.NavigationRequested -= OnNavigationRequested;
        _viewModel.PropertyChanged -= OnShellPropertyChanged;
        _viewModel.ThemeChanged -= OnThemeChanged;

        App.Shutdown();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args) => RenderChannel();

    private void OnThemeChanged(object? sender, ThemePreference theme) => ApplyTheme(theme);

    /// <summary>
    /// Keeps the chip clear of the minimise, maximise and close buttons at
    /// every scale factor, rather than guessing a pixel width that is wrong
    /// on a high-DPI display or when a language reverses the layout.
    /// </summary>
    private void ReserveCaptionSpace()
    {
        var scale = TitleBar.XamlRoot?.RasterizationScale ?? 1d;
        CaptionInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }

    private void ApplyTheme(ThemePreference preference)
    {
        Shell.RequestedTheme = preference switch
        {
            ThemePreference.System => ElementTheme.Default,
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => throw Unreachable.Value(preference),
        };
    }

    private void RenderChannel()
    {
        var channel = _viewModel.Channel;

        ChannelDot.Tone = channel.ChipTone;
        ChannelLabel.Text = channel.ChipLabel;

        if (channel.Banner is { } banner)
        {
            ChannelBanner.Tone = banner.Tone;
            ChannelBanner.Glyph = banner.Tone == StatusTone.Fault ? Glyphs.Error : Glyphs.Warning;
            ChannelBanner.Title = banner.Title;
            ChannelBanner.Message = banner.Message;
            ChannelBanner.ActionLabel = banner.ActionLabel;
            ChannelBanner.ActionCommand = _viewModel.Reconnect;
            ChannelBanner.Visibility = Visibility.Visible;
        }
        else
        {
            ChannelBanner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Moves the pane selection too, so the highlighted entry and the page on
    /// screen never disagree about where the user is.
    /// </summary>
    private void OnNavigationRequested(object? sender, NavigationSection section)
    {
        var tag = section.ToString();
        var match = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => (string?)item.Tag == tag);

        if (match is not null)
        {
            Navigation.SelectedItem = match;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }
            && Enum.TryParse<NavigationSection>(tag, ignoreCase: true, out var section))
        {
            Navigate(section);
        }
    }

    private void Navigate(NavigationSection section)
    {
        var page = section switch
        {
            NavigationSection.Status => typeof(StatusView),
            NavigationSection.Network => typeof(NetworkView),
            NavigationSection.Diagnostics => typeof(DiagnosticsView),
            NavigationSection.Settings => typeof(SettingsView),
            NavigationSection.About => typeof(AboutView),
            _ => throw Unreachable.Value(section),
        };

        if (ContentFrame.CurrentSourcePageType == page)
        {
            return;
        }

        // No slide. The pane already says where the user is, and a transition
        // the user sees on every navigation costs time on every navigation.
        ContentFrame.Navigate(page, null, new SuppressNavigationTransitionInfo());
    }
}

/// <summary>
/// The sections, closed. Adding one without giving it a page fails the build
/// rather than silently landing the user back on Status.
/// </summary>
public enum NavigationSection
{
    Status,
    Network,
    Diagnostics,
    Settings,
    About,
}

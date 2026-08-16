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

        // Use a named handler so it can be removed when the shell outlives the
        // window.
        _viewModel.PropertyChanged += OnShellPropertyChanged;

        Title = "Boreas";

        // Use a stable branded canvas; Mica would make measured contrast
        // depend on the desktop behind the window.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        TitleBar.SizeChanged += (_, _) => ReserveCaptionSpace();

        SampleMarker.Visibility = _viewModel.UsingSampleData ? Visibility.Visible : Visibility.Collapsed;

        // Leave RequestedTheme at Default so the window follows Windows without
        // storing a separate appearance state.

        ReserveCaptionSpace();
        RenderChannel();

        Navigate(NavigationSection.Status);

        App.NavigationRequested += OnNavigationRequested;
        Closed += OnClosed;
    }

    public ShellViewModel ViewModel => _viewModel;

    /// <summary>
    /// Removes window subscriptions before releasing application resources.
    /// </summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        App.NavigationRequested -= OnNavigationRequested;
        _viewModel.PropertyChanged -= OnShellPropertyChanged;

        App.Shutdown();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args) => RenderChannel();

    /// <summary>
    /// Reserves caption-button space at the current scale factor.
    /// </summary>
    private void ReserveCaptionSpace()
    {
        var scale = TitleBar.XamlRoot?.RasterizationScale ?? 1d;
        CaptionInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
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
    /// Keeps pane selection aligned with the requested page.
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

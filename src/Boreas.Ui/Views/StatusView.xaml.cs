using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

public sealed partial class StatusView : Page
{
    public StatusView()
    {
        InitializeComponent();
        ViewModel = new StatusViewModel(App.Channel);
        ViewModel.PropertyChanged += (_, _) => RenderBypass();

        Loaded += (_, _) => RenderBypass();
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    public StatusViewModel ViewModel { get; }


    private void RenderBypass()
    {
        if (ViewModel.BypassDegradation is { } error)
        {
            BypassBanner.Glyph = Glyphs.Warning;
            BypassBanner.Title = "Upstream traffic is not outside the tunnel";
            BypassBanner.Message = error.Summary + " " + error.NextStep;
            BypassBanner.ActionLabel = null;
            BypassBanner.Visibility = Visibility.Visible;
        }
        else
        {
            BypassBanner.Visibility = Visibility.Collapsed;
        }
    }
}

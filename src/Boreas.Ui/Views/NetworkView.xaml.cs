using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

public sealed partial class NetworkView : Page
{
    public NetworkView()
    {
        InitializeComponent();
        ViewModel = new ConfigurationViewModel(App.Channel);
        ViewModel.PropertyChanged += (_, _) => RenderOutcome();

        Loaded += async (_, _) =>
        {
            await ViewModel.LoadAsync();
            RenderOutcome();
        };
    }

    public ConfigurationViewModel ViewModel { get; }


    // Validation on blur, which is the moment a field is finished. Doing it
    // per keystroke tells people their half-typed address is wrong.
    private void OnAdapterBlur(object sender, RoutedEventArgs e) =>
        ViewModel.MarkTouched(ConfigField.Adapter);

    private void OnAddressBlur(object sender, RoutedEventArgs e) =>
        ViewModel.MarkTouched(ConfigField.Address);

    private void OnMtuBlur(object sender, RoutedEventArgs e) =>
        ViewModel.MarkTouched(ConfigField.Mtu);

    private void OnDnsBlur(object sender, RoutedEventArgs e) =>
        ViewModel.MarkTouched(ConfigField.Dns);

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        await ViewModel.Apply.ExecuteAsync(CancellationToken.None);

        // Focus moves to the first thing that needs fixing, so the fix does
        // not start with hunting for which field failed.
        FocusFirstError();
        RenderOutcome();
    }

    private void FocusFirstError()
    {
        if (ViewModel.FirstErrorField is not { } field)
        {
            return;
        }

        Control target = field switch
        {
            ConfigField.Adapter => AdapterInput,
            ConfigField.Address => AddressInput,
            ConfigField.Mtu => MtuInput,
            ConfigField.Dns => DnsInput,
            _ => throw Unreachable.Value(field),
        };

        target.Focus(FocusState.Programmatic);
    }

    private void RenderOutcome()
    {
        if (ViewModel.OutcomeMessage is not { } message)
        {
            OutcomeBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var tone = ViewModel.OutcomeTone;
        OutcomeBanner.Tone = tone;
        OutcomeBanner.Glyph = tone switch
        {
            StatusTone.Active => Glyphs.Accept,
            StatusTone.Caution => Glyphs.Warning,
            StatusTone.Fault => Glyphs.Error,
            StatusTone.Idle => Glyphs.Info,
            _ => throw Unreachable.Value(tone),
        };
        OutcomeBanner.Title = tone switch
        {
            StatusTone.Active => "Settings saved",
            StatusTone.Caution => "Saved, and waiting for a restart",
            StatusTone.Fault => "The service did not accept these settings",
            StatusTone.Idle => "Settings",
            _ => throw Unreachable.Value(tone),
        };
        OutcomeBanner.Message = message;
        OutcomeBanner.ActionLabel = null;
        OutcomeBanner.Visibility = Visibility.Visible;
    }
}

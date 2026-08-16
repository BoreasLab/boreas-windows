using Boreas.Ui.Contracts;
using Boreas.Ui.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Boreas.Ui.Views;

public sealed partial class DiagnosticsView : Page
{
    private static readonly TimeSpan CopyConfirmation = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _copyReset = new() { Interval = CopyConfirmation };

    public DiagnosticsView()
    {
        InitializeComponent();
        ViewModel = new DiagnosticsViewModel(App.Channel);
        ViewModel.PropertyChanged += (_, _) => RenderState();

        _copyReset.Tick += OnCopyResetTick;

        Loaded += async (_, _) =>
        {
            RenderState();
            await ViewModel.Reload.ExecuteAsync(CancellationToken.None);
        };

        Unloaded += (_, _) =>
        {
            _copyReset.Stop();
            _copyReset.Tick -= OnCopyResetTick;
            ViewModel.Dispose();
        };
    }

    public DiagnosticsViewModel ViewModel { get; }

    /// <summary>
    /// Rows for the list; empty whenever the state has no rows.
    /// </summary>
    public IReadOnlyList<EventRow> Rows => ViewModel.Events.ItemsOrEmpty;

    /// <summary>
    /// Shows exactly one region for the closed collection state.
    /// </summary>
    private void RenderState()
    {
        var state = ViewModel.Events;

        LoadingState.Visibility = Collapsed(state is CollectionState<EventRow>.Loading);
        FailedState.Visibility = Collapsed(state is CollectionState<EventRow>.Failed);
        EmptyState.Visibility = Collapsed(state is CollectionState<EventRow>.Empty);
        FilteredState.Visibility = Collapsed(state is CollectionState<EventRow>.Filtered);
        ListState.Visibility = Collapsed(
            state is CollectionState<EventRow>.Ready or CollectionState<EventRow>.Partial);

        FailedMessage.Text = state.Match(
            loading: static _ => string.Empty,
            failed: static f => f.Error.Summary + " " + f.Error.NextStep,
            empty: static _ => string.Empty,
            filtered: static _ => string.Empty,
            partial: static _ => string.Empty,
            ready: static _ => string.Empty);

        PartialNote.Visibility = Collapsed(state is CollectionState<EventRow>.Partial);

        Bindings.Update();
    }

    private static Visibility Collapsed(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(ViewModel.ComposeSupportText());
        Clipboard.SetContent(package);

        // Confirm the copy action where it was pressed, then reset it.
        CopyLabel.Text = "Copied";
        _copyReset.Start();
    }

    private void OnCopyResetTick(object? sender, object e)
    {
        _copyReset.Stop();
        CopyLabel.Text = "Copy for support";
    }

    private void OnClearFilter(object sender, RoutedEventArgs e) => ViewModel.FilterIndex = 0;

    private void OnGoToStatus(object sender, RoutedEventArgs e) =>
        App.RequestNavigation(NavigationSection.Status);
}

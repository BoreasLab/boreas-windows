using System.Text;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Control-plane history: transitions, commands, channel changes, failures.
/// </summary>
/// <remarks>
/// Not a log viewer. The pipe carries no arbitrary log stream, so this shows
/// the bounded event record and nothing else. "Copy for support" produces the
/// same text a person would otherwise transcribe by hand, which is the whole
/// reason it exists.
/// </remarks>
public sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly IControlChannel _channel;
    private ControlEventKind? _filter;
    private bool _isLoading = true;

    public DiagnosticsViewModel(IControlChannel channel)
    {
        _channel = channel;
        Reload = new AsyncCommand(ReloadAsync);
        _channel.Changed += OnChannelChanged;
    }

    public AsyncCommand Reload { get; }

    /// <summary>Null means every kind.</summary>
    public ControlEventKind? Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                Raise(nameof(Events));
            }
        }
    }

    /// <summary>
    /// The filter as the segmented control indexes it, mapped both ways over
    /// the closed kind set so the order is stated once.
    /// </summary>
    public int FilterIndex
    {
        get => _filter switch
        {
            null => 0,
            ControlEventKind.Transition => 1,
            ControlEventKind.Command => 2,
            ControlEventKind.Channel => 3,
            ControlEventKind.Failure => 4,
        };
        set
        {
            Filter = value switch
            {
                1 => ControlEventKind.Transition,
                2 => ControlEventKind.Command,
                3 => ControlEventKind.Channel,
                4 => ControlEventKind.Failure,
                _ => null,
            };
            Raise();
        }
    }

    public CollectionState<EventRow> Events
    {
        get
        {
            if (_isLoading)
            {
                return new CollectionState<EventRow>.Loading();
            }

            var all = _channel.Events;
            if (all.Count == 0)
            {
                return new CollectionState<EventRow>.Empty();
            }

            var matching = (_filter is { } kind ? all.Where(e => e.Kind == kind) : all)
                .Select(EventRow.From)
                .ToArray();

            if (matching.Length == 0)
            {
                return new CollectionState<EventRow>.Filtered(ClearFilter);
            }

            // The service bounds its subscription; this window is what the
            // client keeps. Saying so beats silently showing a truncated list.
            return matching.Length >= EventWindow
                ? new CollectionState<EventRow>.Partial(matching, LoadOlder)
                : new CollectionState<EventRow>.Ready(matching);
        }
    }

    private const int EventWindow = 200;

    public string ComposeSupportText()
    {
        var text = new StringBuilder();
        text.AppendLine("Boreas control-plane events");
        text.AppendLine($"Copied {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine();

        foreach (var row in Events.ItemsOrEmpty)
        {
            text.Append(row.Timestamp);
            text.Append("  ");
            text.Append(row.Kind);
            text.Append("  ");
            text.AppendLine(row.Summary);

            foreach (var line in row.SupportLines)
            {
                text.Append("    ");
                text.AppendLine(line);
            }
        }

        return text.ToString();
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        Raise(nameof(Events));
        await _channel.RefreshAsync(cancellationToken);
        _isLoading = false;
        Raise(nameof(Events));
    }

    private void ClearFilter() => Filter = null;

    private void LoadOlder()
    {
        // The client holds one bounded window and the service does not serve
        // history beyond it, so there is nothing older to fetch. Refreshing is
        // the honest behaviour behind this affordance.
        _ = Reload.ExecuteAsync(CancellationToken.None);
    }

    private void OnChannelChanged(object? sender, EventArgs e)
    {
        _isLoading = false;
        Raise(nameof(Events));
    }

    public void Dispose() => _channel.Changed -= OnChannelChanged;
}

/// <summary>
/// One event, formatted for display, with no nullable path for a template to
/// navigate and no UI type on a contract record.
/// </summary>
public sealed record EventRow(
    Guid Id,
    string Timestamp,
    string Kind,
    string Summary,
    string NextStep,
    bool HasNextStep,
    IReadOnlyList<string> SupportLines)
{
    public static EventRow From(ControlEvent source)
    {
        var lines = new List<string>();
        if (source.Error is { } error)
        {
            lines.Add($"code: {error.Code}");
            lines.Add($"next: {error.NextStep}");
            if (error.Detail is { Length: > 0 } detail)
            {
                lines.Add($"detail: {detail}");
            }
        }

        return new EventRow(
            Id: source.Id,
            Timestamp: source.At.ToString("HH:mm:ss"),
            Kind: Describe(source.Kind),
            Summary: source.Summary,
            NextStep: source.Error?.NextStep ?? string.Empty,
            HasNextStep: source.Error is not null,
            SupportLines: lines);
    }

    private static string Describe(ControlEventKind kind) => kind switch
    {
        ControlEventKind.Transition => "State",
        ControlEventKind.Command => "Command",
        ControlEventKind.Channel => "Channel",
        ControlEventKind.Failure => "Failure",
    };
}

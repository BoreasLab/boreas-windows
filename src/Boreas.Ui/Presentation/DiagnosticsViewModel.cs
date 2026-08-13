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
    private CollectionState<EventRow>? _events;
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
        get;
        set
        {
            if (Set(ref field, value))
            {
                Invalidate();
            }
        }
    }

    /// <summary>
    /// The filter as the segmented control indexes it, mapped both ways over
    /// the closed kind set so the order is stated once.
    /// </summary>
    public int FilterIndex
    {
        get => Filter switch
        {
            null => 0,
            ControlEventKind.Transition => 1,
            ControlEventKind.Command => 2,
            ControlEventKind.Channel => 3,
            ControlEventKind.Failure => 4,
            _ => throw Unreachable.Value(Filter),
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

    /// <summary>
    /// The list, projected once per change.
    /// </summary>
    /// <remarks>
    /// Filtering and projecting is O(n) in the event window, and the view
    /// reads this several times per render: once to choose which of the six
    /// regions is visible, once for the repeater, and again on every binding
    /// refresh. Recomputing per read made a render O(kn) and allocated a fresh
    /// row for every event each time. Now it is O(n) per change.
    /// </remarks>
    public CollectionState<EventRow> Events => _events ??= Project();

    private CollectionState<EventRow> Project()
    {
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

            var matching = (Filter is { } kind ? all.Where(e => e.Kind == kind) : all)
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

    private void Invalidate()
    {
        _events = null;
        Raise(nameof(Events));
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
        Invalidate();
        await _channel.RefreshAsync(cancellationToken);
        _isLoading = false;
        Invalidate();
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
        Invalidate();
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
    IReadOnlyList<string> SupportLines)
{
    /// <summary>
    /// Derived, not stored. A stored flag alongside the text it describes is
    /// a second copy of the same fact, and the two can be set out of step.
    /// </summary>
    public bool HasNextStep => NextStep.Length > 0;

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
            SupportLines: lines);
    }

    private static string Describe(ControlEventKind kind) => kind switch
    {
        ControlEventKind.Transition => "State",
        ControlEventKind.Command => "Command",
        ControlEventKind.Channel => "Channel",
        ControlEventKind.Failure => "Failure",
        _ => throw Unreachable.Value(kind),
    };
}

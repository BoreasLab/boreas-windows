using System.Collections.Immutable;
using System.Text;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Presentation;

/// <summary>
/// Control-plane history: transitions, commands, channel changes, failures.
/// </summary>
/// <remarks>
/// This is a bounded control-plane record, not an arbitrary service log.
/// Support copy preserves the rows and technical details shown here.
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
    /// The segments, in the order they appear. Null is first: "everything" is
    /// a filter choice, not the absence of one.
    /// </summary>
    public static readonly ControlEventKind?[] FilterOrder =
    [
        null,
        ControlEventKind.Transition,
        ControlEventKind.Command,
        ControlEventKind.Channel,
        ControlEventKind.Failure,
    ];

    /// <summary>
    /// The filter index used by the segmented control.
    /// </summary>
    public int FilterIndex
    {
        get => Array.IndexOf(FilterOrder, Filter);
        set
        {
            Filter = FilterOrder[Math.Clamp(value, 0, FilterOrder.Length - 1)];
            Raise();
        }
    }

    /// <summary>
    /// The projected list, cached until state changes.
    /// </summary>
    /// <remarks>
    /// The view reads this several times per render, so project once per
    /// change rather than allocate rows on every binding read.
    /// </remarks>
    public CollectionState<EventRow> Events => _events ??= Project();

    private CollectionState<EventRow> Project()
    {
        if (_isLoading)
        {
            return new CollectionState<EventRow>.Loading();
        }

        var all = _channel.Events;
        if (all.IsEmpty)
        {
            return new CollectionState<EventRow>.Empty();
        }

        var matching = (Filter is { } kind ? all.Where(e => e.Kind == kind) : all.AsEnumerable())
            .Select(EventRow.From)
            .ToArray();

        if (matching.Length == 0)
        {
            return new CollectionState<EventRow>.Filtered(ClearFilter);
        }

        // A full client window is partial, not silently presented as complete.
        return matching.Length >= ControlProtocol.EventWindow
            ? new CollectionState<EventRow>.Partial(matching, LoadOlder)
            : new CollectionState<EventRow>.Ready(matching);
    }

    private void Invalidate()
    {
        _events = null;
        Raise(nameof(Events));
    }

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
        // No older history exists; refresh the bounded window instead.
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
/// One event formatted for display without UI types in the contract record.
/// </summary>
public sealed record EventRow(
    Guid Id,
    string Timestamp,
    string Kind,
    string Summary,
    string NextStep,
    ImmutableArray<string> SupportLines)
{
    /// <summary>
    /// Derived from <see cref="NextStep"/> to avoid duplicate state.
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
            // Freeze support lines before exposing the row.
            SupportLines: [.. lines]);
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

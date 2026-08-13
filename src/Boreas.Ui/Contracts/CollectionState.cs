namespace Boreas.Ui.Contracts;

/// <summary>
/// The six states every data container in this application ships.
/// </summary>
/// <remarks>
/// Independent flags admit contradictions (loading together with error) and
/// cannot tell "none exist" from "none match the filter", which are different
/// screens with different copy and different actions. This is one closed set
/// instead, so a view that forgets a state does not compile.
/// </remarks>
public abstract record CollectionState<T>
{
    private CollectionState() { }

    public sealed record Loading : CollectionState<T>;

    public sealed record Failed(TypedError Error, Action Retry) : CollectionState<T>;

    /// <summary>Nothing has ever been recorded.</summary>
    public sealed record Empty : CollectionState<T>;

    /// <summary>Records exist, but none match the active filter.</summary>
    public sealed record Filtered(Action ClearFilter) : CollectionState<T>;

    /// <summary>A bounded window over a larger set.</summary>
    public sealed record Partial(IReadOnlyList<T> Items, Action LoadMore) : CollectionState<T>;

    public sealed record Ready(IReadOnlyList<T> Items) : CollectionState<T>;

    public TResult Match<TResult>(
        Func<Loading, TResult> loading,
        Func<Failed, TResult> failed,
        Func<Empty, TResult> empty,
        Func<Filtered, TResult> filtered,
        Func<Partial, TResult> partial,
        Func<Ready, TResult> ready) => this switch
        {
            Loading s => loading(s),
            Failed s => failed(s),
            Empty s => empty(s),
            Filtered s => filtered(s),
            Partial s => partial(s),
            Ready s => ready(s),
        };

    /// <summary>
    /// The items, or none. Exhaustive rather than defaulted, so a new state
    /// has to say here whether it carries items.
    /// </summary>
    public IReadOnlyList<T> ItemsOrEmpty => Match(
        loading: static _ => (IReadOnlyList<T>)[],
        failed: static _ => [],
        empty: static _ => [],
        filtered: static _ => [],
        partial: static p => p.Items,
        ready: static r => r.Items);
}

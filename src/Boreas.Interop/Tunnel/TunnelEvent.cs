using Boreas.Interop.Native;

namespace Boreas.Interop.Tunnel;

/// <summary>
/// Occurrences since the previous counted event.
/// </summary>
/// <remarks>
/// Every field is a thing that went wrong or was refused, so a tunnel working
/// normally reports zeroes and a caller can surface any non-zero field without
/// knowing what it means. Values are per-interval: they sum rather than diff.
/// </remarks>
public readonly record struct TunnelCounters(
    ulong DatagramsDropped,
    ulong PacketsRejected,
    ulong QuicSteered,
    ulong PathsReported,
    ulong EventsLost,
    ulong TasksPanicked)
{
    internal static TunnelCounters From(BoreasCounters source) => new(
        source.DatagramsDropped,
        source.PacketsRejected,
        source.QuicSteered,
        source.PathsReported,
        source.EventsLost,
        source.TasksPanicked);

    /// <summary>True when this interval had nothing to report.</summary>
    public bool IsQuiet => this == default;
}

/// <summary>
/// One event from the tunnel, as the closed sum the flat struct describes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BoreasEvent"/> is a tag and every arm's fields side by side
/// rather than a union, which is the right choice at a C boundary and the wrong
/// shape to hand a caller: <b>only the fields the tag names carry meaning</b>
/// and the rest are zero, so a reader of the flat struct can silently use a
/// field the tag did not name. This is where that stops being possible.
/// </para>
/// <para>
/// There is deliberately no arm for an event kind this build does not know.
/// api/stability.md reserves adding one and says to ignore what cannot be
/// interpreted, so <see cref="TryFrom"/> answers null and the reader skips it.
/// An event added later is an event that was not being missed before.
/// </para>
/// </remarks>
public abstract record TunnelEvent
{
    private TunnelEvent() { }

    /// <summary>
    /// One DNS question was answered.
    /// </summary>
    /// <param name="Blocked">
    /// The answer came from policy without anything leaving the device.
    /// </param>
    /// <param name="Rule">
    /// The rule that decided it, or null when no rule did.
    /// </param>
    /// <param name="Truncated">
    /// The name or the rule was longer than the buffer it was copied into. The
    /// text held is still a valid, UTF-8-boundary-aligned prefix.
    /// </param>
    public sealed record Resolved(string Name, string? Rule, bool Blocked, bool Truncated) : TunnelEvent;

    public sealed record Reloaded(ulong Allowed, ulong BlockedRules, ulong Inspected) : TunnelEvent;

    public sealed record Counted(TunnelCounters Counters) : TunnelEvent;

    public TResult Match<TResult>(
        Func<Resolved, TResult> resolved,
        Func<Reloaded, TResult> reloaded,
        Func<Counted, TResult> counted) => this switch
        {
            Resolved e => resolved(e),
            Reloaded e => reloaded(e),
            Counted e => counted(e),
            _ => throw new System.Diagnostics.UnreachableException($"Unhandled {nameof(TunnelEvent)}: {this}"),
        };

    /// <summary>
    /// Reads one event, or null for a kind this build does not recognise.
    /// </summary>
    /// <param name="name">
    /// The name buffer that was passed to <c>next_event</c>, already cut at its
    /// NUL. Ignored unless the kind is <see cref="Resolved"/>.
    /// </param>
    /// <param name="rule">The rule buffer, on the same terms.</param>
    /// <param name="nameCapacity">
    /// The capacity that was offered, which is what makes truncation
    /// detectable: <c>name_len</c> is the length the text <b>would</b> have
    /// needed, so larger than this means it did not all fit.
    /// </param>
    /// <param name="ruleCapacity">The rule buffer's capacity, on the same terms.</param>
    internal static TunnelEvent? TryFrom(
        in BoreasEvent source, string name, string rule, nuint nameCapacity, nuint ruleCapacity) =>
        source.Kind switch
        {
            BoreasEventKind.Resolved => new Resolved(
                Name: name,
                // rule_len == 0 means no rule decided it, which is different
                // from a rule whose text happened to be empty.
                Rule: source.RuleLen == 0 ? null : rule,
                Blocked: source.Blocked,
                Truncated: source.NameLen > nameCapacity || source.RuleLen > ruleCapacity),

            BoreasEventKind.Reloaded => new Reloaded(
                Allowed: source.Allowed,
                BlockedRules: source.BlockedRules,
                Inspected: source.Inspected),

            BoreasEventKind.Counted => new Counted(TunnelCounters.From(source.Counters)),

            _ => null,
        };
}

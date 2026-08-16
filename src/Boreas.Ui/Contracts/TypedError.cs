namespace Boreas.Ui.Contracts;

/// <summary>
/// A failure as the user needs to read it, plus the detail an engineer needs.
/// </summary>
/// <param name="Code">
/// Stable, machine-readable, safe to show and to paste into a support thread.
/// </param>
/// <param name="Summary">
/// What happened, in one sentence, in the user's terms.
/// </param>
/// <param name="NextStep">
/// What to do about it. Never empty: an error the user can do nothing about
/// still tells them who can.
/// </param>
/// <param name="Detail">
/// Secondary technical text. Kept available and kept out of the way.
/// </param>
/// <remarks>
/// The service produces these; the client does not turn local exceptions into
/// service messages. They may contain no credentials, keys, packet payloads, or
/// unrestricted diagnostic text.
/// </remarks>
public sealed record TypedError(
    string Code,
    string Summary,
    string NextStep,
    string? Detail = null);

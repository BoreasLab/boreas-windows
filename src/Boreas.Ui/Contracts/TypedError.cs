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
/// The service produces these. The client never composes an error message from
/// an exception it caught, because a message assembled on this side would
/// describe the client's confusion rather than the service's failure.
///
/// Nothing here may carry credentials, key material, packet payloads or
/// unrestricted diagnostic text; docs/core-contract.md puts that constraint on
/// the pipe, and this record is the shape that constraint has to hold for.
/// </remarks>
public sealed record TypedError(
    string Code,
    string Summary,
    string NextStep,
    string? Detail = null);

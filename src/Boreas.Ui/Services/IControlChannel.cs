using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// The client's whole view of the service. W2 implements this over the
/// authenticated local named pipe; nothing above this interface knows that.
/// </summary>
/// <remarks>
/// The shape follows the logical interface in docs/core-contract.md. Three
/// properties of it matter to the interface built on top:
///
/// 1. <see cref="State"/> and <see cref="Channel"/> are pushed, not polled, so
///    a state change the user did not cause still reaches the screen.
/// 2. Commands return the resulting state rather than void, so the client
///    renders what the service decided instead of what it hoped.
/// 3. <see cref="RefreshAsync"/> is idempotent, which is what makes reconnect
///    after a service restart safe to issue at any time.
/// </remarks>
public interface IControlChannel
{
    ControlChannelState Channel { get; }

    /// <summary>
    /// The last authoritative service state. Meaningless unless
    /// <see cref="Channel"/> is connected, and the interface must not present
    /// it as current while the channel is down.
    /// </summary>
    ServiceState State { get; }

    IReadOnlyList<ControlEvent> Events { get; }

    event EventHandler? Changed;

    /// <summary>Idempotent <c>status_snapshot</c>. Safe to call on reconnect.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<ServiceState> StartAsync(CancellationToken cancellationToken = default);

    Task<ServiceState> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a configuration that has already crossed the parse boundary.
    /// Taking <see cref="ValidatedConfiguration"/> rather than raw text means
    /// no caller can send something unchecked, and no implementation has to
    /// re-check.
    /// </summary>
    Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default);
}

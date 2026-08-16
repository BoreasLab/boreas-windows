using System.Collections.Immutable;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// The client's service view; W2 implements it over the authenticated pipe.
/// </summary>
/// <remarks>
/// Implementations push state changes on the UI thread, return authoritative
/// command results, never throw except for caller-requested cancellation, keep
/// a bounded newest-first event window, and release owned resources.
/// </remarks>
public interface IControlChannel : IDisposable
{
    ControlChannelState Channel { get; }

    /// <summary>
    /// Last authoritative service state; meaningful only while the channel is
    /// connected.
    /// </summary>
    ServiceState State { get; }

    /// <summary>
    /// The bounded control-plane record, newest first, never longer than
    /// <see cref="ControlProtocol.EventWindow"/>.
    /// </summary>
    /// <remarks>
    /// Immutable snapshots prevent a render from observing a buffer while its
    /// producer mutates it.
    /// </remarks>
    ImmutableArray<ControlEvent> Events { get; }

    event EventHandler? Changed;

    /// <summary>Idempotent <c>status_snapshot</c>. Safe to call on reconnect.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<ServiceState> StartAsync(CancellationToken cancellationToken = default);

    Task<ServiceState> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends configuration that has already passed the parse boundary.
    /// </summary>
    Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default);
}

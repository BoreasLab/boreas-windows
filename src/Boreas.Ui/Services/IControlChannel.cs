using System.Collections.Immutable;
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
///
/// Four obligations an implementation must meet. Each was previously a
/// convention the sample channel happened to follow, which is to say a
/// question W2 would have had to ask and could have answered differently.
///
/// <b>Never throw.</b> Every failure this interface can have is already a
/// state: a pipe that will not open is <see cref="ControlChannelState"/>, a
/// command the service refused is <see cref="ServiceState.Failed"/> with a
/// <see cref="TypedError"/>, and a configuration it rejected is
/// <see cref="ConfigurationOutcome.Rejected"/>. An exception escaping into
/// the view models reaches an <c>async void</c> command handler and takes the
/// process down, so an implementation catches its own I/O and reports it.
/// <see cref="OperationCanceledException"/> is the exception: cancellation is
/// the caller's own request and is expected to propagate.
///
/// <b>Raise <see cref="Changed"/> on the UI thread.</b> Handlers touch view
/// models that XAML is bound to, and the pipe reader will not be on that
/// thread. Marshalling is the implementation's job, at the one place the
/// thread changes, rather than every subscriber's.
///
/// <b>Bound <see cref="Events"/> to <see cref="ControlProtocol.EventWindow"/>,
/// newest first.</b> The list is read on every render, so it is a window and
/// not a log, and the diagnostics view reports a full window as a bounded
/// record rather than pretending it is complete.
///
/// <b>Close what you opened.</b> A pipe client owns a stream, a reader task
/// and a cancellation source; <see cref="IDisposable"/> is where they go.
/// Synchronous rather than <see cref="IAsyncDisposable"/> on purpose: the one
/// caller is a window-closed handler, which cannot await, and cancelling the
/// source and disposing the stream is enough to unwind a reader.
/// </remarks>
public interface IControlChannel : IDisposable
{
    ControlChannelState Channel { get; }

    /// <summary>
    /// The last authoritative service state. Meaningless unless
    /// <see cref="Channel"/> is connected, and the interface must not present
    /// it as current while the channel is down.
    /// </summary>
    ServiceState State { get; }

    /// <summary>
    /// The bounded control-plane record, newest first, never longer than
    /// <see cref="ControlProtocol.EventWindow"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ImmutableArray{T}"/> rather than
    /// <see cref="IReadOnlyList{T}"/>, which promises the holder will not
    /// write and promises nothing about anyone else. An implementation
    /// answering with its live buffer would hand the view a list it goes on
    /// mutating, and appending to a list mid-enumeration throws. The value
    /// handed out here cannot change after it is handed out, so a render
    /// reads a fixed record. It also iterates as fast as the array it wraps,
    /// with no interface dispatch per element.
    /// </remarks>
    ImmutableArray<ControlEvent> Events { get; }

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

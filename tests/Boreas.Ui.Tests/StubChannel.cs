using System.Collections.Immutable;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Tests;

/// <summary>
/// A channel frozen at one state pair. Used where a law is about what the
/// interface says, not about what the service does.
/// </summary>
public sealed class StubChannel(ControlChannelState channel, ServiceState state) : IControlChannel
{
    public ControlChannelState Channel { get; } = channel;

    public ServiceState State { get; } = state;

    public ImmutableArray<ControlEvent> Events { get; init; } = [];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <summary>Nothing is opened, so nothing is closed.</summary>
    public void Dispose()
    {
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ServiceState> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ServiceState> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConfigurationOutcome>(new ConfigurationOutcome.Applied());

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationDraft("Boreas", "10.0.0.2/24", "1420", "", RouteMode.Default, EgressPolicy.Direct));
}

/// <summary>
/// A channel that records the draft it was handed and answers with a chosen
/// outcome. Used to prove what the form sends and what it does with the reply.
/// </summary>
public sealed class RecordingChannel(ConfigurationOutcome outcome, ConfigurationDraft? initial = null)
    : IControlChannel
{
    private readonly ConfigurationDraft _initial = initial
        ?? new ConfigurationDraft("Boreas", "10.7.0.2/24", "1420", "10.7.0.1", RouteMode.Default, EgressPolicy.Direct);

    public ValidatedConfiguration? LastApplied { get; private set; }

    public int ApplyCount { get; private set; }

    public ControlChannelState Channel { get; } = new ControlChannelState.Connected(1);

    public ServiceState State { get; } = new ServiceState.Stopped();

    public ImmutableArray<ControlEvent> Events { get; } = [];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <summary>Nothing is opened, so nothing is closed.</summary>
    public void Dispose()
    {
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ServiceState> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ServiceState> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration, CancellationToken cancellationToken = default)
    {
        LastApplied = configuration;
        ApplyCount++;
        return Task.FromResult(outcome);
    }

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_initial);
}

using System.Collections.Immutable;
using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// The channel this application ships with until W2 exists.
/// </summary>
/// <remarks>
/// No pipe client exists yet, so tunnel state is unknown rather than stopped.
/// W2 replaces this implementation without changing <see cref="IControlChannel"/>.
/// </remarks>
public sealed class UnimplementedControlChannel : IControlChannel
{
    private static readonly TypedError NotBuilt = new(
        Code: "control.client.not_implemented",
        Summary: "This build of the Boreas app has no control-pipe client.",
        NextStep: "Install a build that includes the control client, or start and stop the "
                + "Boreas service from Windows Services in the meantime.",
        Detail: "The named-pipe client is phase W2 of docs/implementation-plan.md.");

    public ControlChannelState Channel { get; } = new ControlChannelState.Unavailable(NotBuilt);

    public ServiceState State { get; } = new ServiceState.Stopped();

    public ImmutableArray<ControlEvent> Events { get; } = [];

    public event EventHandler? Changed
    {
        // State never changes; accessors satisfy the interface.
        add { }
        remove { }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ServiceState> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ServiceState> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ValidatedConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ConfigurationOutcome>(
            new ConfigurationOutcome.Rejected(NotBuilt, new Dictionary<ConfigField, string>()));

    /// <summary>
    /// No resources were opened; present to satisfy the interface.
    /// </summary>
    public void Dispose()
    {
    }

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationDraft(
            AdapterName: string.Empty,
            InterfaceAddress: string.Empty,
            Mtu: string.Empty,
            DnsServers: string.Empty,
            Routes: RouteMode.Default,
            Egress: EgressPolicy.Direct));
}

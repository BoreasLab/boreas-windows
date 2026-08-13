using Boreas.Ui.Contracts;

namespace Boreas.Ui.Services;

/// <summary>
/// The channel this application ships with until W2 exists.
/// </summary>
/// <remarks>
/// It reports the truth: there is no pipe client yet, so nothing is known
/// about the tunnel. It deliberately does not pretend the service is stopped.
///
/// W2 replaces this with the framed, versioned, authenticated named-pipe
/// client. Nothing above <see cref="IControlChannel"/> changes when it does.
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

    public IReadOnlyList<ControlEvent> Events { get; } = [];

    public event EventHandler? Changed
    {
        // Nothing here ever changes, so there is nothing to subscribe to and
        // nothing to leak. The accessors exist to satisfy the interface.
        add { }
        remove { }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ServiceState> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ServiceState> StopAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<ConfigurationOutcome> ApplyConfigurationAsync(
        ConfigurationDraft draft,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ConfigurationOutcome>(
            new ConfigurationOutcome.Rejected(NotBuilt, new Dictionary<string, string>()));

    public Task<ConfigurationDraft> ReadConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationDraft(
            AdapterName: string.Empty,
            InterfaceAddress: string.Empty,
            Mtu: string.Empty,
            DnsServers: string.Empty,
            Routes: RouteMode.Default,
            Egress: EgressPolicy.Direct));
}

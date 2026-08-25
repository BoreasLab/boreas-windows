using Boreas.Interop.Native;
using Boreas.Interop.Tunnel;
using Boreas.Ui.Contracts;
using Boreas.Ui.Services;

namespace Boreas.Ui.Tests;

/// <summary>
/// Laws for the control surface over a real tunnel.
/// </summary>
/// <remarks>
/// These run without Windows and without a Boreas library because the operating
/// system is behind <see cref="ITunnelHost"/> and the UI thread is behind an
/// injected posting delegate. What is left is a state machine, and a state
/// machine is a thing that can be asserted.
/// </remarks>
public sealed class NativeChannelLaws
{
    private sealed class FakeTunnel : IRunningTunnel
    {
        public SessionCounters Counters { get; set; }

        public EgressBypass Bypass { get; set; } = new EgressBypass.Bound("Ethernet");

        public int Stops { get; private set; }

        public int Disposals { get; private set; }

        public Action<TunnelEvent>? Emit { get; set; }

        public TunnelEvent.Reloaded Reload(IReadOnlyCollection<string> lists) =>
            new((ulong)lists.Count, 0, 0);

        public BoreasStatus Stop()
        {
            Stops++;
            return BoreasStatus.Ok;
        }

        public void Dispose() => Disposals++;
    }

    private sealed class FakeHost : ITunnelHost
    {
        public FakeTunnel Tunnel { get; } = new();

        public Exception? Failure { get; set; }

        public int Starts { get; private set; }

        public IRunningTunnel Start(ValidatedConfiguration configuration, Action<TunnelEvent> onEvent)
        {
            Starts++;

            if (Failure is { } failure)
            {
                throw failure;
            }

            Tunnel.Emit = onEvent;
            return Tunnel;
        }
    }

    private static ValidatedConfiguration Configuration() =>
        Assert.IsType<ConfigurationParse.Valid>(
            ConfigurationParser.Parse(new ConfigurationDraft(
                "Boreas", "10.7.0.2/24", "1420", "10.7.0.1", RouteMode.Default, EgressPolicy.Direct)))
            .Configuration;

    private static NativeControlChannel Channel(
        FakeHost host, ControlChannelState? state = null) =>
        new(host,
            Configuration(),
            // Inline, which is what "already on the UI thread" looks like.
            static action => action(),
            state ?? new ControlChannelState.Connected(ControlProtocol.Version));

    [Fact]
    public async Task A_start_moves_through_starting_to_running()
    {
        var host = new FakeHost();
        using var channel = Channel(host);
        var seen = new List<ServiceState>();

        channel.Changed += (_, _) => seen.Add(channel.State);

        Assert.IsType<ServiceState.Stopped>(channel.State);

        await channel.StartAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ServiceState.Running>(channel.State);
        Assert.Contains(seen, state => state is ServiceState.Starting);
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task A_stop_moves_through_stopping_to_stopped_and_releases_the_tunnel()
    {
        var host = new FakeHost();
        using var channel = Channel(host);
        var seen = new List<ServiceState>();

        await channel.StartAsync(TestContext.Current.CancellationToken);
        channel.Changed += (_, _) => seen.Add(channel.State);

        await channel.StopAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ServiceState.Stopped>(channel.State);
        Assert.Contains(seen, state => state is ServiceState.Stopping);
        Assert.Equal(1, host.Tunnel.Stops);
        Assert.Equal(1, host.Tunnel.Disposals);
    }

    /// <summary>
    /// A second press must not build a second native runtime beside the first.
    /// </summary>
    [Fact]
    public async Task A_second_start_does_not_create_a_second_tunnel()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        await channel.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task A_stop_with_nothing_running_changes_nothing()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StopAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ServiceState.Stopped>(channel.State);
        Assert.Equal(0, host.Tunnel.Stops);
    }

    /// <summary>
    /// The client sends no command it knows will be refused, which is why the
    /// channel state gates this rather than the button.
    /// </summary>
    [Fact]
    public async Task A_disconnected_channel_sends_no_command()
    {
        var host = new FakeHost();
        using var channel = Channel(host, new ControlChannelState.VersionMismatch(2));

        await channel.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, host.Starts);
        Assert.IsType<ServiceState.Stopped>(channel.State);
    }

    [Fact]
    public async Task A_failed_start_becomes_a_typed_failure_the_user_can_act_on()
    {
        var host = new FakeHost { Failure = new BoreasException(BoreasStatus.Config, "Starting the tunnel") };
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);

        var failed = Assert.IsType<ServiceState.Failed>(channel.State);

        Assert.Equal(ControlOperation.Start, failed.Operation);
        Assert.True(failed.Recoverable);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error.Summary));
        Assert.False(string.IsNullOrWhiteSpace(failed.Error.NextStep));
    }

    /// <summary>
    /// Recoverable is the service's judgement, and the two it must get right
    /// are the two where retrying cannot help: a defect in the core, and a
    /// library that does not match the header.
    /// </summary>
    [Fact]
    public async Task A_defect_and_an_abi_mismatch_are_not_offered_as_retryable()
    {
        foreach (var failure in new Exception[]
        {
            new BoreasException(BoreasStatus.Panic, "Starting the tunnel"),
            new BoreasAbiMismatchException(1, 2),
        })
        {
            var host = new FakeHost { Failure = failure };
            using var channel = Channel(host);

            await channel.StartAsync(TestContext.Current.CancellationToken);

            Assert.False(Assert.IsType<ServiceState.Failed>(channel.State).Recoverable);
        }
    }

    /// <summary>
    /// <b>One event per DNS question would evict every transition inside a
    /// minute.</b> The window is a bounded control-plane record, not a name log.
    /// </summary>
    [Fact]
    public async Task A_resolution_is_never_recorded_in_the_control_window()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        var before = channel.Events.Length;

        for (var i = 0; i < 1000; i++)
        {
            host.Tunnel.Emit!(new TunnelEvent.Resolved($"host{i}.example", "||ads^", Blocked: true, Truncated: false));
        }

        Assert.Equal(before, channel.Events.Length);
    }

    [Fact]
    public async Task A_reload_is_recorded_as_a_command()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        host.Tunnel.Emit!(new TunnelEvent.Reloaded(1000, 40, 3));

        var newest = channel.Events[0];

        Assert.Equal(ControlEventKind.Command, newest.Kind);
        Assert.Contains("1000", newest.Summary, StringComparison.Ordinal);
        Assert.Contains("40", newest.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tunnel working normally reports zeroes, and zeroes are not news. A
    /// quiet interval that produced a row would make the list unreadable
    /// exactly when nothing was wrong.
    /// </summary>
    [Fact]
    public async Task A_quiet_counting_interval_is_not_recorded()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        var before = channel.Events.Length;

        host.Tunnel.Emit!(new TunnelEvent.Counted(default));

        Assert.Equal(before, channel.Events.Length);
    }

    /// <summary>
    /// Every counter is a thing that went wrong or was refused, so any non-zero
    /// field is surfaced without this having to know what it means - and the
    /// ones that do mean something specific get the sentence that says so.
    /// </summary>
    [Theory]
    [InlineData("core.task_panicked")]
    [InlineData("core.path_mtu")]
    [InlineData("core.ceiling")]
    [InlineData("core.events_lost")]
    public async Task A_loud_counting_interval_is_recorded_with_the_advice_for_it(string code)
    {
        var counters = code switch
        {
            "core.task_panicked" => new TunnelCounters(0, 0, 0, 0, 0, 1),
            "core.path_mtu" => new TunnelCounters(0, 0, 0, 9, 0, 0),
            "core.ceiling" => new TunnelCounters(7, 0, 0, 0, 0, 0),
            _ => new TunnelCounters(0, 0, 0, 0, 5, 0),
        };

        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        host.Tunnel.Emit!(new TunnelEvent.Counted(counters));

        var newest = channel.Events[0];

        Assert.Equal(ControlEventKind.Failure, newest.Kind);
        Assert.Equal(code, Assert.IsType<TypedError>(newest.Error).Code);
        Assert.False(string.IsNullOrWhiteSpace(newest.Error!.NextStep));
    }

    /// <summary>
    /// A defect outranks everything else in the same interval: it is the only
    /// counter that is not a condition of the network.
    /// </summary>
    [Fact]
    public async Task A_defect_outranks_the_other_counters_reported_beside_it()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        host.Tunnel.Emit!(new TunnelEvent.Counted(new TunnelCounters(9, 9, 9, 9, 9, 1)));

        Assert.Equal("core.task_panicked", channel.Events[0].Error!.Code);
    }

    /// <summary>The window is bounded and newest first.</summary>
    [Fact]
    public async Task The_event_window_stays_within_its_bound_and_newest_first()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);

        for (var i = 0; i < ControlProtocol.EventWindow * 3; i++)
        {
            host.Tunnel.Emit!(new TunnelEvent.Reloaded((ulong)i, 0, 0));
        }

        Assert.Equal(ControlProtocol.EventWindow, channel.Events.Length);
        Assert.Contains((ControlProtocol.EventWindow * 3) - 1, channel.Events[0].Summary.Split(' ').Select(
            token => int.TryParse(token, out var value) ? value : -1));
    }

    /// <summary>
    /// Events arrive on the tunnel's reader thread, and the interface owes the
    /// UI thread. Everything the channel raises has to go through the post.
    /// </summary>
    [Fact]
    public async Task Every_notification_goes_through_the_posting_delegate()
    {
        var host = new FakeHost();
        var posts = 0;

        using var channel = new NativeControlChannel(
            host,
            Configuration(),
            action => { posts++; action(); },
            new ControlChannelState.Connected(ControlProtocol.Version));

        await channel.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, posts);

        host.Tunnel.Emit!(new TunnelEvent.Reloaded(1, 0, 0));

        Assert.Equal(1, posts);
    }

    /// <summary>
    /// Every field this form edits is fixed at start: reload replaces the rules
    /// in force and nothing else. Saying "applied" while a session runs on the
    /// old values would be the partial silent application the contract forbids.
    /// </summary>
    [Fact]
    public async Task A_configuration_applied_while_running_reports_restart_required()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        Assert.IsType<ConfigurationOutcome.Applied>(
            await channel.ApplyConfigurationAsync(Configuration(), TestContext.Current.CancellationToken));

        await channel.StartAsync(TestContext.Current.CancellationToken);

        Assert.IsType<ConfigurationOutcome.RestartRequired>(
            await channel.ApplyConfigurationAsync(Configuration(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_stored_configuration_is_what_the_form_reads_back()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        var replacement = Assert.IsType<ConfigurationParse.Valid>(
            ConfigurationParser.Parse(new ConfigurationDraft(
                "Other", "10.9.0.3/16", "9000", "1.1.1.1", RouteMode.Selected, EgressPolicy.WireGuard)))
            .Configuration;

        await channel.ApplyConfigurationAsync(replacement, TestContext.Current.CancellationToken);

        Assert.Equal(replacement.ToDraft(), await channel.ReadConfigurationAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The counters live on the tunnel, so a refresh reads them rather than
    /// keeping a second copy that could drift.
    /// </summary>
    [Fact]
    public async Task Refreshing_re_reads_the_counters_from_the_tunnel()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        host.Tunnel.Counters = new SessionCounters(11, 13, 17, 19);

        await channel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new SessionCounters(11, 13, 17, 19),
            Assert.IsType<ServiceState.Running>(channel.State).Status.Counters);
    }

    /// <summary>
    /// A bypass that degrades while running has to reach the status, because a
    /// tunnel reporting itself healthy with unprotected sockets is the first of
    /// the two silent mistakes.
    /// </summary>
    [Fact]
    public async Task A_degraded_bypass_reaches_the_status()
    {
        var host = new FakeHost();
        using var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);

        host.Tunnel.Bypass = new EgressBypass.Degraded(
            new TypedError("host.bypass", "Upstream sockets are not bound.", "Check the network adapter."));

        await channel.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.IsType<EgressBypass.Degraded>(
            Assert.IsType<ServiceState.Running>(channel.State).Status.Bypass);
    }

    /// <summary>
    /// Disposing without stopping would free a handle a reader is blocked
    /// inside, so disposal goes through the tunnel's own stop rather than past
    /// it.
    /// </summary>
    [Fact]
    public async Task Disposing_releases_the_running_tunnel()
    {
        var host = new FakeHost();
        var channel = Channel(host);

        await channel.StartAsync(TestContext.Current.CancellationToken);
        channel.Dispose();

        Assert.Equal(1, host.Tunnel.Disposals);
    }
}

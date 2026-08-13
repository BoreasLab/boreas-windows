using Boreas.Ui.Contracts;

namespace Boreas.Ui.Presentation;

/// <summary>
/// The one place a state becomes something a person can read.
/// </summary>
/// <remarks>
/// This is a pure function of the channel state and the service state, so the
/// entire behaviour of the status screen can be read, reviewed and tested
/// without a window, a pipe or a service.
///
/// The rule it encodes: while the channel is not connected, the client makes
/// no claim about the tunnel at all. Not "stopped", not "unknown but probably
/// fine". A stale service state is exactly as untrustworthy as no state, and
/// this application is one a person consults to decide whether their traffic
/// is protected.
/// </remarks>
public sealed record StatusPresentation(
    string Headline,
    string Detail,
    StatusTone Tone,
    string Glyph,
    string GlyphLabel,
    bool ShowProgress,
    PrimaryAction Action)
{
    /// <summary>
    /// What a screen reader is told when the state changes. The headline alone
    /// would announce "Off" with no subject, which is not a sentence.
    /// </summary>
    public string Announcement => $"{Headline}. {Detail}";

    public static StatusPresentation For(ControlChannelState channel, ServiceState service) =>
        channel.Match(
            connecting: _ => new StatusPresentation(
                Headline: "Connecting",
                Detail: "Reading the current state from the Boreas service.",
                Tone: StatusTone.Idle,
                Glyph: Glyphs.Sync,
                GlyphLabel: "Connecting",
                ShowProgress: true,
                Action: PrimaryAction.None),

            connected: _ => ForService(service),

            unavailable: u => new StatusPresentation(
                Headline: "No service",
                Detail: "The Boreas service is not answering, so this app cannot tell you "
                      + "whether the tunnel is running. " + u.Error.NextStep,
                Tone: StatusTone.Caution,
                Glyph: Glyphs.Warning,
                GlyphLabel: "Service unreachable",
                ShowProgress: false,
                Action: PrimaryAction.Reconnect),

            unauthorized: _ => new StatusPresentation(
                Headline: "Not permitted",
                Detail: "The Boreas service refused this Windows account. An administrator "
                      + "sets which accounts may control the tunnel when Boreas is installed.",
                Tone: StatusTone.Fault,
                Glyph: Glyphs.Error,
                GlyphLabel: "Not permitted",
                ShowProgress: false,
                Action: PrimaryAction.None),

            versionMismatch: v => new StatusPresentation(
                Headline: "Update needed",
                Detail: $"This app speaks control protocol {v.ClientVersion} and the installed "
                      + $"service speaks {v.ServiceVersion}. Install the matching version of "
                      + "Boreas to control the tunnel again.",
                Tone: StatusTone.Fault,
                Glyph: Glyphs.Error,
                GlyphLabel: "Version mismatch",
                ShowProgress: false,
                Action: PrimaryAction.None));

    private static StatusPresentation ForService(ServiceState service) =>
        service.Match(
            stopped: _ => new StatusPresentation(
                Headline: "Off",
                Detail: "This device is using its normal network connection.",
                Tone: StatusTone.Idle,
                Glyph: Glyphs.Unlock,
                GlyphLabel: "Tunnel off",
                ShowProgress: false,
                Action: PrimaryAction.Start),

            starting: _ => new StatusPresentation(
                Headline: "Starting",
                Detail: "Creating the network adapter and handing it to the engine.",
                Tone: StatusTone.Caution,
                Glyph: Glyphs.Sync,
                GlyphLabel: "Starting",
                ShowProgress: true,
                Action: PrimaryAction.None),

            running: r => r.Status.Bypass.Match(
                // The claim the product exists to make. It is only made when
                // upstream sockets are provably outside the tunnel.
                bound: _ => new StatusPresentation(
                    Headline: "Protected",
                    Detail: "Traffic from this device is going through Boreas.",
                    Tone: StatusTone.Active,
                    Glyph: Glyphs.Lock,
                    GlyphLabel: "Protected",
                    ShowProgress: false,
                    Action: PrimaryAction.Stop),

                // Running, but the host could not bind the physical interface,
                // so upstream traffic may be re-entering the tunnel. Saying
                // "Protected" here would be the worst thing this screen could
                // do, so it says the weaker true thing instead.
                degraded: d => new StatusPresentation(
                    Headline: "Running",
                    Detail: "The tunnel is up, but Boreas could not keep its own upstream "
                          + "connection outside it. " + d.Error.NextStep,
                    Tone: StatusTone.Caution,
                    Glyph: Glyphs.Warning,
                    GlyphLabel: "Running with degraded bypass",
                    ShowProgress: false,
                    Action: PrimaryAction.Stop)),

            stopping: _ => new StatusPresentation(
                Headline: "Stopping",
                Detail: "Stopping the engine before the network adapter is released.",
                Tone: StatusTone.Caution,
                Glyph: Glyphs.Sync,
                GlyphLabel: "Stopping",
                ShowProgress: true,
                Action: PrimaryAction.None),

            failed: f => new StatusPresentation(
                Headline: "Stopped",
                Detail: f.Error.Summary,
                Tone: StatusTone.Fault,
                Glyph: Glyphs.Error,
                GlyphLabel: $"{Describe(f.Operation)} failed",
                ShowProgress: false,
                // The service decides whether retrying is worth offering. A
                // retry button on an unrecoverable failure trains people to
                // press buttons that never work.
                Action: f.Recoverable ? PrimaryAction.Retry : PrimaryAction.None));

    private static string Describe(ControlOperation operation) => operation switch
    {
        ControlOperation.Start => "Start",
        ControlOperation.Stop => "Stop",
        ControlOperation.ConfigurationChanged => "Applying configuration",
        ControlOperation.NetworkChanged => "Handling a network change",
        ControlOperation.StatusSnapshot => "Reading status",
    };
}

/// <summary>
/// The four tones, named for what they signal rather than for one state.
/// Each maps to exactly one brush key in Tokens.xaml.
/// </summary>
public enum StatusTone
{
    Idle,
    Active,
    Caution,
    Fault,
}

/// <summary>
/// The single most prominent action, chosen by the state rather than always
/// present. <see cref="None"/> is a real case: while the service is starting,
/// there is nothing useful to press, and offering something anyway would
/// produce a command the service serialises and rejects.
/// </summary>
public enum PrimaryAction
{
    None,
    Start,
    Stop,
    Retry,
    Reconnect,
}

/// <summary>
/// Every glyph the application uses, from one family at one weight: Segoe
/// Fluent Icons, which ships with Windows.
/// </summary>
internal static class Glyphs
{
    public const string Home = "\uE80F";
    public const string Globe = "\uE774";
    public const string Diagnostic = "\uE9D9";
    public const string Info = "\uE946";
    public const string Lock = "\uE72E";
    public const string Unlock = "\uE785";
    public const string Sync = "\uE895";
    public const string Warning = "\uE7BA";
    public const string Error = "\uE783";
    public const string Refresh = "\uE72C";
    public const string Copy = "\uE8C8";
    public const string Accept = "\uE8FB";
}

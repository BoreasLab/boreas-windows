using Boreas.Ui.Contracts;

namespace Boreas.Ui.Presentation;

/// <summary>
/// How the control channel is shown: always as a chip, sometimes as a banner.
/// </summary>
/// <remarks>
/// The chip always answers whether the app can reach the service; the banner
/// appears only when the channel needs user action.
/// </remarks>
public sealed record ChannelPresentation(
    string ChipLabel,
    StatusTone ChipTone,
    ChannelBanner? Banner)
{
    public static ChannelPresentation For(ControlChannelState channel) => channel.Match(
        connecting: _ => new ChannelPresentation(
            ChipLabel: "Connecting",
            ChipTone: StatusTone.Idle,
            Banner: null),

        connected: _ => new ChannelPresentation(
            ChipLabel: "Service connected",
            ChipTone: StatusTone.Active,
            Banner: null),

        unavailable: u => new ChannelPresentation(
            ChipLabel: "No service",
            ChipTone: StatusTone.Caution,
            Banner: new ChannelBanner(
                Title: "Not connected to the Boreas service",
                Message: u.Error.Summary + " " + u.Error.NextStep,
                Tone: StatusTone.Caution,
                ActionLabel: "Try again")),

        unauthorized: _ => new ChannelPresentation(
            ChipLabel: "Not permitted",
            ChipTone: StatusTone.Fault,
            Banner: new ChannelBanner(
                Title: "This account cannot control Boreas",
                Message: "The service checks the Windows account before it accepts any "
                       + "command. Ask whoever installed Boreas to add this account, then "
                       + "sign out and back in.",
                Tone: StatusTone.Fault,
                  // Authorization cannot change through a retry.
                ActionLabel: null)),

        versionMismatch: v => new ChannelPresentation(
            ChipLabel: "Version mismatch",
            ChipTone: StatusTone.Fault,
            Banner: new ChannelBanner(
                Title: "This app and the Boreas service do not match",
                Message: $"App protocol {v.ClientVersion}, service protocol {v.ServiceVersion}. "
                       + "Install the version of Boreas that ships both together. The tunnel "
                       + "is unaffected by this app being out of date.",
                Tone: StatusTone.Fault,
                ActionLabel: null)));
}

/// <param name="ActionLabel">
/// Null when there is genuinely nothing for the user to press. An action that
/// cannot change the outcome is worse than none.
/// </param>
public sealed record ChannelBanner(
    string Title,
    string Message,
    StatusTone Tone,
    string? ActionLabel);

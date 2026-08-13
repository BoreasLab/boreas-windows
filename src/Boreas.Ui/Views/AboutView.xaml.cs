using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

public sealed partial class AboutView : Page
{
    public AboutView()
    {
        InitializeComponent();

        // Read, not typed in. A version string maintained by hand is wrong
        // the first time someone forgets to change it.
        AppVersion.Text = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        ProtocolVersion.Text = App.Channel.Channel.Match(
            connecting: static _ => "not known yet",
            connected: static c => $"version {c.ProtocolVersion}, agreed with the service",
            unavailable: static _ => "not known, the service is not answering",
            unauthorized: static _ => "not known, this account is not permitted",
            versionMismatch: static v => $"this app speaks {v.ClientVersion}, the service speaks {v.ServiceVersion}");
    }
}

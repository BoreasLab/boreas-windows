using System.Reflection;
using Boreas.Ui.Contracts;
using Microsoft.UI.Xaml.Controls;

namespace Boreas.Ui.Views;

public sealed partial class AboutView : Page
{
    public AboutView()
    {
        InitializeComponent();

        // Read from assembly metadata so it cannot drift from the build. The
        // same two values go into the release notes, from the same tool.
        var build = BuildIdentity.Read(Assembly.GetExecutingAssembly());

        AppVersion.Text = $"{build.App}  ({build.Position})";
        CoreVersion.Text = build.Core;

        ProtocolVersion.Text = App.Channel.Channel.Match(
            connecting: static _ => "not known yet",
            connected: static c => $"version {c.ProtocolVersion}, agreed with the service",
            unavailable: static _ => "not known, the service is not answering",
            unauthorized: static _ => "not known, this account is not permitted",
            versionMismatch: static v => $"this app speaks {v.ClientVersion}, the service speaks {v.ServiceVersion}");
    }
}

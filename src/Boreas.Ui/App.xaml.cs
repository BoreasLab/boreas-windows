using Boreas.Ui.Presentation;
using Boreas.Ui.Services;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Boreas.Ui;

public partial class App : Application
{
    private static readonly UISettings SystemUiSettings = new();

    private MainWindow? _window;

    public App() => InitializeComponent();

    /// <summary>
    /// Whether the system permits animation, read at the point of use.
    /// </summary>
    /// <remarks>
    /// Windows exposes reduced motion through "Animation effects". Disabling
    /// it removes movement without hiding state or affordances.
    /// </remarks>
    public static bool MotionEnabled => SystemUiSettings.AnimationsEnabled;

    /// <summary>The shared channel instance.</summary>
    public static IControlChannel Channel { get; private set; } = null!;

    public static bool UsingSampleData { get; private set; }

    /// <summary>
    /// Window-level state shared by the window and settings page.
    /// </summary>
    public static ShellViewModel Shell { get; private set; } = null!;

    /// <summary>
    /// Raised when a page requests navigation.
    /// </summary>
    public static event EventHandler<NavigationSection>? NavigationRequested;

    public static void RequestNavigation(NavigationSection section) =>
        NavigationRequested?.Invoke(null, section);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if DEBUG
    // Debug builds use marked sample data; release builds use the honest
    // no-client channel.
        Channel = new SampleControlChannel();
        UsingSampleData = true;
#else
        Channel = new UnimplementedControlChannel();
        UsingSampleData = false;
#endif

        Shell = new ShellViewModel(Channel, UsingSampleData);

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// Releases resources acquired by <see cref="OnLaunched"/>, in reverse.
    /// </summary>
    /// <remarks>
    /// The shell unsubscribes before the channel is disposed, preventing a
    /// handler from referencing a closed channel.
    /// </remarks>
    public static void Shutdown()
    {
        Shell?.Dispose();
        Channel?.Dispose();
    }
}

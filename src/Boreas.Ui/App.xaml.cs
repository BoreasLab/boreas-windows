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
    /// This is the Windows setting behind "Animation effects", which is how
    /// reduced motion is expressed on this platform. Nothing in the interface
    /// is gated behind an animation, so switching it off removes movement and
    /// leaves every state, affordance and transition fully readable.
    /// </remarks>
    public static bool MotionEnabled => SystemUiSettings.AnimationsEnabled;

    /// <summary>The one channel instance, owned here and shared downward.</summary>
    public static IControlChannel Channel { get; private set; } = null!;

    public static bool UsingSampleData { get; private set; }

    public static PreferenceStore Preferences { get; } = new();

    /// <summary>
    /// Window-level state, owned here so the window and the settings page
    /// edit one value rather than keeping two copies to synchronise.
    /// </summary>
    public static ShellViewModel Shell { get; private set; } = null!;

    /// <summary>
    /// Raised when a page asks to move somewhere else, so an empty state can
    /// offer the action that fills it without reaching into the window's
    /// navigation control itself.
    /// </summary>
    public static event EventHandler<NavigationSection>? NavigationRequested;

    public static void RequestNavigation(NavigationSection section) =>
        NavigationRequested?.Invoke(null, section);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if DEBUG
        // Sample data, and the window says so. Release builds get the honest
        // "no control client yet" channel instead.
        Channel = new SampleControlChannel();
        UsingSampleData = true;
#else
        Channel = new UnimplementedControlChannel();
        UsingSampleData = false;
#endif

        Shell = new ShellViewModel(Channel, Preferences, UsingSampleData);

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// Releases what <see cref="OnLaunched"/> acquired, in reverse.
    /// </summary>
    /// <remarks>
    /// Called from the window's Closed handler, which is the only moment this
    /// process has between "the user is finished" and exit. Until now nothing
    /// released either: the channel was created and abandoned, which the sample
    /// channel survives because a leaked timer dies with the process, and which
    /// the pipe client will not, because abandoning a pipe makes every exit
    /// look to the service like a client that crashed.
    ///
    /// Order matters. The shell unsubscribes from the channel before the
    /// channel is disposed, so nothing is left holding a handler on an object
    /// that has closed its stream.
    /// </remarks>
    public static void Shutdown()
    {
        Shell?.Dispose();
        Channel?.Dispose();
    }
}

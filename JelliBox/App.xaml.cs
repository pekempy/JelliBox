using JelliBox.Services;
using JelliBox.Views;
using Windows.ApplicationModel.Activation;
using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace JelliBox;

sealed partial class App : Application
{
    public static JellyfinApiService Api { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static NavigationService Navigation { get; private set; } = null!;

    private readonly RawGamepadFallback _rawGamepad = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[CRITICAL UNHANDLED ERROR] {e.Message} \n {e.Exception}");
            e.Handled = true;
        };

        // 1. Disable 200% TV layout scaling on Xbox to reduce UI size by 50% (enables crisp native 1080p/4K resolution)
        if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationViewScaling"))
        {
            ApplicationViewScaling.TrySetDisableLayoutScaling(true);
        }

        // 2. Controller-first focus navigation on Xbox (no virtual mouse cursor)
        RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;

        Settings = new SettingsService();
        Api = new JellyfinApiService(Settings);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {

        // Resources are only safe to touch once the app has started launching.
        AccentService.Apply(Settings.AccentColorHex);

        // 3. Make sure the app fills the entire TV screen edge-to-edge (eliminates TV safe margins / letterboxing)
        if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationView"))
        {
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
        }

        var rootFrame = Window.Current.Content as Frame;

        if (rootFrame == null)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += (_, ex) =>
            {
                System.Diagnostics.Debug.WriteLine($"[NAVIGATION FAILED] {ex.SourcePageType.FullName}\n{ex.Exception}");
                ex.Handled = true;
            };
            rootFrame.Navigated += (s, args) =>
            {
                if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.ApplicationView"))
                {
                    ApplicationView.GetForCurrentView().SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
                }
            };
            Window.Current.Content = rootFrame;
            rootFrame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
            rootFrame.RequestedTheme = Windows.UI.Xaml.ElementTheme.Dark;
            Navigation = new NavigationService(rootFrame);
        }

        if (e.PrelaunchActivated) return;

        if (rootFrame.Content == null)
        {
            var dest = Settings.HasValidServer ? typeof(HomePage) : typeof(LoginPage);
            rootFrame.Navigate(dest);
        }

        Window.Current.Activate();
        _rawGamepad.Start();
    }
}

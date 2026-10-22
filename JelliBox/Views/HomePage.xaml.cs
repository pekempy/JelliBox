using JelliBox.ViewModels;
using System.ComponentModel;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Core;

namespace JelliBox.Views;

public sealed partial class HomePage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private HomeViewModel VM => (HomeViewModel)DataContext;

    private bool _heroLayerB;
    private string? _lastHeroUrl;

    private readonly JelliBox.Services.FocusGrid _grid;

    public HomePage()
    {
        InitializeComponent();
        _grid = new JelliBox.Services.FocusGrid(HomeContentRoot, x => TopBar.FocusNearest(x));
        NavigationCacheMode = NavigationCacheMode.Enabled;
        VM.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM.HeroBackdropUrl))
            CrossfadeHeroBackdrop(VM.HeroBackdropUrl);
    }

    private void CrossfadeHeroBackdrop(string? url)
    {
        if (string.IsNullOrEmpty(url) || url == _lastHeroUrl) return;
        _lastHeroUrl = url;

        var incoming = _heroLayerB ? HeroBackdropImageB : HeroBackdropImageA;
        var outgoing  = _heroLayerB ? HeroBackdropImageA : HeroBackdropImageB;

        incoming.Source = new BitmapImage(new Uri(url)) { DecodePixelWidth = 1920 };

        var sb = new Storyboard();
        var fadeIn  = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(fadeIn,  incoming);
        Storyboard.SetTargetProperty(fadeIn,  "Opacity");
        Storyboard.SetTarget(fadeOut, outgoing);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        sb.Children.Add(fadeIn);
        sb.Children.Add(fadeOut);
        sb.Begin();

        _heroLayerB = !_heroLayerB;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var focusBar = App.Navigation.TakeBarFocusRequest();
        if (App.Navigation.TakeSlide() is bool slideFromRight)
            JelliBox.Services.PageTransition.SlideIn(HomeContentRoot, slideFromRight);
        try
        {
            if (e.NavigationMode == NavigationMode.Back)
                await VM.RefreshQuickAsync();
            else
            {
                _lastHeroUrl = null; // force crossfade even if same URL on fresh load
                await VM.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HOMEPAGE LOAD ERROR] {ex}");
        }
        
        if (focusBar)
        {
            await TopBar.FocusCurrentTabAsync();
            return;
        }

        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
        {
            // Focus hero play if visible, otherwise first item in content
            if (VM.HeroItem != null && HeroPlayButton.IsEnabled)
                HeroPlayButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
            else
                (Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(HomeContentRoot)
                    as Windows.UI.Xaml.Controls.Control)?.Focus(Windows.UI.Xaml.FocusState.Keyboard);
        });
    }

    private void Page_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var args = new JelliBox.Services.KeyEvent(e.Key);
        HandleKey(args);
        if (args.Handled) e.Handled = true;
    }

    public void HandleKey(JelliBox.Services.KeyEvent e)
    {
        if (TopBar.HandleKey(e)) return;

        if (ToDirection(e.Key) is { } direction)
        {
            var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Windows.UI.Xaml.Controls.Button;
            if (focused != null && TopBar.ContainsFocus(focused))
            {
                // Down from the bar goes into the page at the nearest column.
                if (direction == Windows.UI.Xaml.Input.FocusNavigationDirection.Down)
                {
                    var x = focused.TransformToVisual(Windows.UI.Xaml.Window.Current.Content)
                        .TransformPoint(new Windows.Foundation.Point(focused.ActualWidth / 2, 0)).X;
                    if (_grid.EnterFromTop(x)) { e.Handled = true; return; }
                }
            }
            else if (focused == null && _grid.EnterFromTop(0))
            {
                // Focus is on nothing useful (a container, or lost): start from the hero / first row.
                e.Handled = true;
                return;
            }
            else if (_grid.TryMove(direction))
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == VirtualKey.GamepadLeftShoulder)
        {
            VM.PrevHeroCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.GamepadRightShoulder)
        {
            VM.NextHeroCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static Windows.UI.Xaml.Input.FocusNavigationDirection? ToDirection(VirtualKey key) => key switch
    {
        VirtualKey.GamepadDPadUp or VirtualKey.GamepadLeftThumbstickUp or VirtualKey.Up
            => Windows.UI.Xaml.Input.FocusNavigationDirection.Up,
        VirtualKey.GamepadDPadDown or VirtualKey.GamepadLeftThumbstickDown or VirtualKey.Down
            => Windows.UI.Xaml.Input.FocusNavigationDirection.Down,
        VirtualKey.GamepadDPadLeft or VirtualKey.GamepadLeftThumbstickLeft or VirtualKey.Left
            => Windows.UI.Xaml.Input.FocusNavigationDirection.Left,
        VirtualKey.GamepadDPadRight or VirtualKey.GamepadLeftThumbstickRight or VirtualKey.Right
            => Windows.UI.Xaml.Input.FocusNavigationDirection.Right,
        _ => null,
    };
}

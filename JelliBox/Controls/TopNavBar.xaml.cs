using JelliBox.Services;
using JelliBox.ViewModels;
using System.ComponentModel;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace JelliBox.Controls;

/// <summary>
/// The navigation bar every page shares: Home and library tabs, search, settings and the user switcher.
/// Pages place it, say where they are via <see cref="Current"/>, and may slot extra controls in via <see cref="Extra"/>.
/// </summary>
public sealed partial class TopNavBar : UserControl
{
    private readonly TopNavViewModel _vm = new();

    public static readonly DependencyProperty CurrentProperty = DependencyProperty.Register(
        nameof(Current), typeof(string), typeof(TopNavBar),
        new PropertyMetadata("", (d, e) => ((TopNavBar)d)._vm.SetCurrent(e.NewValue as string)));

    public static readonly DependencyProperty ExtraProperty = DependencyProperty.Register(
        nameof(Extra), typeof(object), typeof(TopNavBar),
        new PropertyMetadata(null, (d, e) => ((TopNavBar)d).ExtraHost.Content = e.NewValue));

    /// <summary>Which section the page belongs to: "home", "livetv", or a library id. That tab is highlighted.</summary>
    public string Current
    {
        get => (string)GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    /// <summary>Page-specific controls shown at the right of the bar (before search).</summary>
    public object? Extra
    {
        get => GetValue(ExtraProperty);
        set => SetValue(ExtraProperty, value);
    }

    public TopNavBar()
    {
        InitializeComponent();
        // Set here rather than in XAML: these attributes on a user control's root crash the XAML engine at startup.
        IsTabStop = false;
        // Only the bar's own parts use the bar's view model; the page's Extra content keeps the page's.
        LeftPanel.DataContext = _vm;
        ButtonsPanel.DataContext = _vm;
        AccountPanel.DataContext = _vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += async (_, _) =>
        {
            AccountSession.Switched += OnAccountSwitched;
            await _vm.ReloadAsync(); // picks up library visibility / order changes made in Settings
        };
        Unloaded += (_, _) => AccountSession.Switched -= OnAccountSwitched;
    }

    private async void OnAccountSwitched() => await _vm.ReloadAsync();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TopNavViewModel.IsAccountMenuOpen))
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplyAccountMenuState);
    }

    /// <summary>True when keyboard / controller focus is somewhere in the bar (not the switcher panel).</summary>
    public bool ContainsFocus(DependencyObject? element)
        => element != null && RowNavigation.Contains(Bar, element);

    /// <summary>Put focus on the highlighted tab, waiting for the tabs to load and lay out.</summary>
    public async Task FocusCurrentTabAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (FindCurrentTab(TabsList) is { } tab && tab.Focus(FocusState.Keyboard)) return;
            await Task.Delay(75);
        }
    }

    private static Button? FindCurrentTab(DependencyObject root)
    {
        var count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button { DataContext: NavTab { IsCurrent: true }, ActualWidth: > 0, Visibility: Visibility.Visible } button)
                return button;
            if (FindCurrentTab(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Focus the bar's button nearest the given screen column (used when leaving a page upwards).</summary>
    public bool FocusNearest(double screenX) => RowNavigation.FocusNearest(Bar, screenX);

    private void ApplyAccountMenuState()
    {
        var open = _vm.IsAccountMenuOpen;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
        {
            if (!open)
            {
                AccountButton.Focus(FocusState.Keyboard);
                return;
            }

            // The panel and its account buttons are only laid out a moment after it becomes visible.
            for (var attempt = 0; attempt < 20 && _vm.IsAccountMenuOpen; attempt++)
            {
                if (RowNavigation.FocusFirstButton(AccountPanel)) return;
                await Task.Delay(50);
            }
        });
    }

    /// <summary>
    /// Handle a key for the bar. Returns true when the page should stop handling it; the key is only
    /// marked Handled when it should not also activate the focused control.
    /// </summary>
    public bool HandleKey(KeyEvent e)
    {
        if (_vm.IsAccountMenuOpen)
        {
            // The switcher is modal: B closes it, Up/Down step through it, A activates, nothing else gets out.
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
            {
                _vm.IsAccountMenuOpen = false;
                e.Handled = true;
            }
            else if (ToDirection(e.Key) is { } menuDirection)
            {
                var insidePanel = FocusManager.GetFocusedElement() is Button focusedNow
                                  && RowNavigation.Contains(AccountPanel, focusedNow);
                if (!insidePanel)
                    RowNavigation.FocusFirstButton(AccountPanel); // focus was lost: pull it back in
                else if (menuDirection is FocusNavigationDirection.Up or FocusNavigationDirection.Down)
                    RowNavigation.TryMoveVertical(AccountPanel, menuDirection == FocusNavigationDirection.Down);
                e.Handled = true;
            }
            return true;
        }

        if (FocusManager.GetFocusedElement() is not Button focused || !ContainsFocus(focused)) return false;
        if (ToDirection(e.Key) is not { } direction) return false;

        if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
        {
            RowNavigation.TryMove(Bar, direction == FocusNavigationDirection.Right);
            e.Handled = true;
            return true;
        }

        // Behaves like a drop-down: Down from the user button opens the switcher.
        if (focused == AccountButton && direction == FocusNavigationDirection.Down)
        {
            _vm.IsAccountMenuOpen = true;
            e.Handled = true;
            return true;
        }

        return false; // Up / Down elsewhere: the page decides where focus goes
    }

    private static FocusNavigationDirection? ToDirection(VirtualKey key) => key switch
    {
        VirtualKey.GamepadDPadUp or VirtualKey.GamepadLeftThumbstickUp or VirtualKey.Up
            => FocusNavigationDirection.Up,
        VirtualKey.GamepadDPadDown or VirtualKey.GamepadLeftThumbstickDown or VirtualKey.Down
            => FocusNavigationDirection.Down,
        VirtualKey.GamepadDPadLeft or VirtualKey.GamepadLeftThumbstickLeft or VirtualKey.Left
            => FocusNavigationDirection.Left,
        VirtualKey.GamepadDPadRight or VirtualKey.GamepadLeftThumbstickRight or VirtualKey.Right
            => FocusNavigationDirection.Right,
        _ => null,
    };
}

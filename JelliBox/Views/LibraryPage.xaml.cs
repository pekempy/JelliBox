using JelliBox.ViewModels;
using System.ComponentModel;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace JelliBox.Views;

public sealed partial class LibraryPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private LibraryViewModel VM => (LibraryViewModel)DataContext;

    private readonly JelliBox.Services.FocusGrid _grid;
    private readonly JelliBox.Services.FocusGrid _filterGrid;
    private ScrollViewer? _tileScroller;
    private LibraryArgs? _args;
    private Models.MediaItem? _lastFocusedItem;
    private LibraryViewModel? _attached;

    // A library you leave (e.g. into a movie or a collection) is kept here so Back restores it exactly.
    // The page itself is not cached: one cached instance would be shared by every library you open.
    private static readonly Dictionary<string, (LibraryViewModel Vm, Models.MediaItem? Focused)> Saved = new();

    // The GridView's own scroller; it only exists once the control template has been applied.
    private ScrollViewer TileScroller
    {
        get
        {
            if (_tileScroller == null)
            {
                TileGrid.ApplyTemplate();
                _tileScroller = FindDescendant<ScrollViewer>(TileGrid);
            }
            return _tileScroller!;
        }
    }

    public LibraryPage()
    {
        InitializeComponent();
        GotFocus += OnPageGotFocus;
        TileGrid.Loaded += (_, _) => ApplyTileSize();
        _filterGrid = new JelliBox.Services.FocusGrid(FilterScroller);
        _grid = new JelliBox.Services.FocusGrid(() => TileScroller,
            exitTop: x => TopBar.FocusNearest(x),
            exitRight: y => JelliBox.Services.RowNavigation.FocusNearestY(JumpList, y));
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var focusBar = App.Navigation.TakeBarFocusRequest();
        if (App.Navigation.TakeSlide() is bool slideFromRight)
            JelliBox.Services.PageTransition.SlideIn(ContentArea, slideFromRight);

        if (e.Parameter is not LibraryArgs args)
        {
            Attach(VM);
            return;
        }
        _args = args;
        TopBar.Current = args.ParentId;

        // Back to a library we left: bring back its view model (items, sort, filters, letter) and focus.
        if (e.NavigationMode == NavigationMode.Back && Saved.Remove(args.ParentId, out var saved))
        {
            DataContext = saved.Vm;
            Attach(saved.Vm);
            _lastFocusedItem = saved.Focused;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, ApplyTileSize);
            if (saved.Focused != null) FocusItem(saved.Focused, ScrollIntoViewAlignment.Default);
            return;
        }

        Attach(VM);
        await VM.LoadAsync(args);

        // Arrived by choosing a tab: stay on that tab until the user moves down.
        if (focusBar)
        {
            await TopBar.FocusCurrentTabAsync();
            return;
        }

        // Wait for the tiles to be laid out, then focus the first one (containers don't count).
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var focused = false;
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                focused = FirstTile()?.Focus(Windows.UI.Xaml.FocusState.Keyboard) == true);
            if (focused) break;
            await Task.Delay(100);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_args != null) Saved[_args.ParentId] = (VM, _lastFocusedItem);
        Detach();
    }

    private void Attach(LibraryViewModel vm)
    {
        Detach();
        vm.ScrollToItemRequested += OnScrollToItem;
        vm.PropertyChanged += OnVmPropertyChanged;
        _attached = vm;
    }

    private void Detach()
    {
        if (_attached == null) return;
        _attached.ScrollToItemRequested -= OnScrollToItem;
        _attached.PropertyChanged -= OnVmPropertyChanged;
        _attached = null;
    }

    // Remember the last tile that had focus so Back can return to it.
    private void OnPageGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<Controls.MediaCard>(source) is { Item: { } item })
            _lastFocusedItem = item;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is T match) return match;
        return null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryViewModel.IsEpisodeView):
            case nameof(LibraryViewModel.IsMusicView):
            case nameof(LibraryViewModel.IsPosterView):
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplyTileSize);
                break;
            case nameof(LibraryViewModel.IsFilterMenuOpen):
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplyFilterMenuState);
                break;
        }
    }

    // Poster, episode and music views use different tile sizes and column counts.
    private void ApplyTileSize()
    {
        if (TileGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        if (VM.IsEpisodeView) { panel.ItemWidth = 336; panel.ItemHeight = 236; panel.MaximumRowsOrColumns = 5; }
        else if (VM.IsMusicView) { panel.ItemWidth = 196; panel.ItemHeight = 236; panel.MaximumRowsOrColumns = 8; }
        else { panel.ItemWidth = 136; panel.ItemHeight = 236; panel.MaximumRowsOrColumns = 12; }
    }

    // While the panel is open every direction key is handled by HandleFilterMenuKey, so focus can't leave it.
    private void ApplyFilterMenuState()
    {
        var open = VM.IsFilterMenuOpen;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
        {
            if (!open)
            {
                FilterButton.Focus(FocusState.Keyboard);
                return;
            }

            // The panel is laid out a moment after it becomes visible.
            for (var attempt = 0; attempt < 20 && VM.IsFilterMenuOpen; attempt++)
            {
                if (_filterGrid.EnterFromTop(0)) return;
                await Task.Delay(50);
            }
        });
    }

    // Letter jump: scroll the tile into existence (the grid is virtualized), focus it, and bring it into view.
    private void OnScrollToItem(Models.MediaItem item) => FocusItem(item, ScrollIntoViewAlignment.Leading);

    private void FocusItem(Models.MediaItem item, ScrollIntoViewAlignment alignment)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
        {
            TileGrid.ScrollIntoView(item, alignment);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (TileGrid.ContainerFromItem(item) is GridViewItem { ContentTemplateRoot: Controls.MediaCard card }
                    && card.ActualHeight > 0
                    && Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(card) is Control tile)
                {
                    tile.Focus(FocusState.Keyboard);
                    tile.StartBringIntoView(new BringIntoViewOptions
                    {
                        AnimationDesired = alignment == ScrollIntoViewAlignment.Leading,
                        VerticalAlignmentRatio = alignment == ScrollIntoViewAlignment.Leading ? 0.3 : double.NaN,
                        HorizontalAlignmentRatio = double.NaN,
                    });
                    return;
                }
                await Task.Delay(100);
            }
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
        if (VM.IsFilterMenuOpen)
        {
            HandleFilterMenuKey(e);
            return;
        }

        if (TopBar.HandleKey(e)) return;

        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            VM.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ToDirection(e.Key) is not { } direction) return;

        var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Button;
        if (focused == null)
        {
            // Focus is on nothing useful (a container, or lost): start from the first tile.
            if (FirstTile() is { } first)
            {
                first.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            return;
        }

        if (TopBar.ContainsFocus(focused))
        {
            // Down from the header bar goes straight to the nearest tile in the first row.
            if (direction == FocusNavigationDirection.Down)
            {
                var x = focused.TransformToVisual(Window.Current.Content)
                    .TransformPoint(new Windows.Foundation.Point(focused.ActualWidth / 2, 0)).X;
                if (_grid.EnterFromTop(x)) e.Handled = true;
                else if (FirstTile() is { } tile)
                {
                    tile.Focus(FocusState.Keyboard);
                    e.Handled = true;
                }
            }
        }
        else if (JelliBox.Services.RowNavigation.Contains(JumpList, focused))
        {
            // Left from the A-Z list returns to the tile row beside it.
            if (direction == FocusNavigationDirection.Left)
            {
                var y = focused.TransformToVisual(Window.Current.Content)
                    .TransformPoint(new Windows.Foundation.Point(0, focused.ActualHeight / 2)).Y;
                if (_grid.EnterFromRight(y)) e.Handled = true;
            }
        }
        else if (_grid.TryMove(direction))
        {
            e.Handled = true;
        }
    }

    private void HandleFilterMenuKey(JelliBox.Services.KeyEvent e)
    {
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            VM.IsFilterMenuOpen = false;
            e.Handled = true;
            return;
        }

        if (ToDirection(e.Key) is not { } direction) return;
        e.Handled = true; // never let the default focus engine take us out of the panel

        var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Control;
        if (focused == null || !JelliBox.Services.RowNavigation.Contains(FilterPanel, focused))
        {
            _filterGrid.EnterFromTop(0); // focus was lost: pull it back in
            return;
        }

        // Rows of options: Left/Right step along a row, Up/Down move between rows (scrolling if needed).
        _filterGrid.TryMove(direction);
    }

    private Control? FirstTile()
    {
        var card = FindFirstVisible<Controls.MediaCard>(TileScroller);
        return card == null ? null
            : Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(card) as Control;
    }

    private static T? FindFirstVisible<T>(DependencyObject root) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.ActualHeight > 0 && match.Visibility == Visibility.Visible)
                return match;
            if (FindFirstVisible<T>(child) is { } found) return found;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
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

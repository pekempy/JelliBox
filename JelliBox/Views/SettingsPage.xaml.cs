using JelliBox.ViewModels;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace JelliBox.Views;

public sealed partial class SettingsPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private SettingsViewModel VM => (SettingsViewModel)DataContext;

    // Two columns: category list on the left, that category's settings on the right.
    private readonly JelliBox.Services.FocusGrid _rail;
    private readonly JelliBox.Services.FocusGrid _pane;

    public SettingsPage()
    {
        InitializeComponent();
        VM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSignOutConfirmOpen))
                _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplySignOutConfirmState);
        };
        _pane = new JelliBox.Services.FocusGrid(RightPaneScrollViewer,
            exitTop: x => TopBar.FocusNearest(x),
            exitLeft: _ => FocusActiveCategory());
        _rail = new JelliBox.Services.FocusGrid(RailScrollViewer,
            exitTop: x => TopBar.FocusNearest(x),
            exitRight: _ => _pane.EnterFromTop(0));
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await VM.LoadAsync();
        FocusActiveCategoryWhenReady();
    }

    // Focus the active category in the left list (waits for it to be laid out).
    private void FocusActiveCategoryWhenReady()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (FocusActiveCategory()) return;
                await Task.Delay(100);
            }
        });
    }

    private bool FocusActiveCategory()
    {
        var buttons = new List<Button>();
        CollectButtons(RailScrollViewer, buttons);
        var active = buttons.FirstOrDefault(b => b.CommandParameter is int i && i == VM.SelectedCategoryIndex)
                     ?? buttons.FirstOrDefault();
        return active != null && active.Focus(FocusState.Keyboard);
    }

    private static void CollectButtons(DependencyObject root, List<Button> into)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { ActualWidth: > 0 } b) { into.Add(b); continue; }
            CollectButtons(child, into);
        }
    }

    private void FocusRightPane()
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => _pane.EnterFromTop(0));
    }

    // Default to CANCEL so a stray A press never signs you out.
    private void ApplySignOutConfirmState()
    {
        var open = VM.IsSignOutConfirmOpen;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            (open ? CancelSignOutButton : SignOutButton).Focus(FocusState.Keyboard));
    }

    private void Page_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var args = new JelliBox.Services.KeyEvent(e.Key);
        HandleKey(args);
        if (args.Handled) e.Handled = true;
    }

    public void HandleKey(JelliBox.Services.KeyEvent e)
    {
        if (VM.IsSignOutConfirmOpen)
        {
            if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
            {
                VM.IsSignOutConfirmOpen = false;
                e.Handled = true;
            }
            else if (ToDirection(e.Key) is { } confirmDirection)
            {
                // Two buttons side by side: Left/Right switch between them, nothing else may leave the dialog.
                if (confirmDirection is FocusNavigationDirection.Left or FocusNavigationDirection.Right
                    && Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() is Button { } current
                    && VisualTreeHelper.GetParent(current) is StackPanel row)
                {
                    JelliBox.Services.RowNavigation.TryMove(row, confirmDirection == FocusNavigationDirection.Right);
                }
                e.Handled = true;
            }
            return;
        }

        if (e.Key == VirtualKey.GamepadLeftShoulder)
        {
            VM.PreviousCategory();
            FocusRightPane();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.GamepadRightShoulder)
        {
            VM.NextCategory();
            FocusRightPane();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            VM.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (TopBar.HandleKey(e)) return;

        if (ToDirection(e.Key) is not { } direction) return;

        var focused = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as Control;
        if (focused == null || !JelliBox.Services.RowNavigation.Contains(this, focused))
        {
            e.Handled = FocusActiveCategory();
            return;
        }

        if (JelliBox.Services.RowNavigation.Contains(RailScrollViewer, focused))
        {
            if (_rail.TryMove(direction))
            {
                e.Handled = true;
                // Moving through the list previews each category on the right.
                if (direction is FocusNavigationDirection.Up or FocusNavigationDirection.Down
                    && Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() is Button { Command: { } command } category
                    && command.CanExecute(category.CommandParameter))
                {
                    command.Execute(category.CommandParameter);
                }
            }
        }
        else if (JelliBox.Services.RowNavigation.Contains(RightPaneScrollViewer, focused))
        {
            if (_pane.TryMove(direction)) e.Handled = true;
        }
        else
        {
            // The shared bar: Down enters the category list, the rest stays put.
            if (direction == FocusNavigationDirection.Down) FocusActiveCategory();
            e.Handled = true;
        }
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

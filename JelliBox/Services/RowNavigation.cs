using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace JelliBox.Services;

/// <summary>
/// Left/Right focus movement inside a horizontal bar (top nav, sort buttons). The focus engine's
/// default can skip neighbours or drop into content below, so we step through the bar's buttons in order.
/// </summary>
public static class RowNavigation
{
    /// <summary>Returns true when focus is inside <paramref name="bar"/> and the key was consumed.</summary>
    public static bool TryMove(FrameworkElement bar, bool toRight)
    {
        if (FocusManager.GetFocusedElement() is not Button current || !Contains(bar, current)) return false;

        var buttons = new List<Button>();
        Collect(bar, buttons);
        buttons = buttons.OrderBy(XPosition).ToList();

        var index = buttons.IndexOf(current);
        if (index < 0) return false;

        var next = index + (toRight ? 1 : -1);
        if (next >= 0 && next < buttons.Count)
            buttons[next].Focus(FocusState.Keyboard);
        return true; // at either end: stay put rather than jumping out of the bar
    }

    /// <summary>Up/Down through a vertical list of buttons (a menu panel). Returns true when focus is inside and the key was consumed.</summary>
    public static bool TryMoveVertical(FrameworkElement list, bool down)
    {
        if (FocusManager.GetFocusedElement() is not Button current || !Contains(list, current)) return false;

        var buttons = new List<Button>();
        Collect(list, buttons);
        buttons = buttons.OrderBy(YPosition).ToList();

        var index = buttons.IndexOf(current);
        if (index < 0) return false;

        var next = index + (down ? 1 : -1);
        if (next >= 0 && next < buttons.Count) buttons[next].Focus(FocusState.Keyboard);
        return true;
    }

    /// <summary>Focus the topmost button inside a container (containers such as list controls are never a focus target).</summary>
    public static bool FocusFirstButton(FrameworkElement container)
    {
        var buttons = new List<Button>();
        Collect(container, buttons);
        var first = buttons.OrderBy(YPosition).FirstOrDefault();
        return first != null && first.Focus(FocusState.Keyboard);
    }

    private static double YPosition(UIElement e)
        => e.TransformToVisual(Window.Current.Content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;

    /// <summary>Focus the bar's button whose centre is closest to the given screen column.</summary>
    public static bool FocusNearest(FrameworkElement bar, double screenX)
    {
        var buttons = new List<Button>();
        Collect(bar, buttons);
        var best = buttons.OrderBy(b => Math.Abs(XPosition(b) + b.ActualWidth / 2 - screenX)).FirstOrDefault();
        return best != null && best.Focus(FocusState.Keyboard);
    }

    /// <summary>Focus the container's button whose centre is closest to the given screen Y.</summary>
    public static bool FocusNearestY(FrameworkElement container, double screenY)
    {
        var buttons = new List<Button>();
        Collect(container, buttons);
        var best = buttons
            .OrderBy(b => Math.Abs(b.TransformToVisual(Window.Current.Content).TransformPoint(new Windows.Foundation.Point(0, b.ActualHeight / 2)).Y - screenY))
            .FirstOrDefault();
        return best != null && best.Focus(FocusState.Keyboard);
    }

    private static double XPosition(UIElement e)
        => e.TransformToVisual(Window.Current.Content).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

    public static bool Contains(DependencyObject ancestor, DependencyObject d)
    {
        for (; d != null; d = VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private static void Collect(DependencyObject root, List<Button> into)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button { IsEnabled: true, IsTabStop: true, ActualWidth: > 0, Visibility: Visibility.Visible } b)
                into.Add(b);
            Collect(child, into);
        }
    }
}

using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace JelliBox.Services;

/// <summary>
/// Deterministic D-pad navigation for a scrolling page made of rows of buttons (hero actions,
/// "See all" headers, card strips). Rows are derived from layout, Left/Right step through a row,
/// and Up/Down move to the adjacent row keeping the same screen column.
/// </summary>
public sealed class FocusGrid
{
    private const double RowTolerance = 24;

    private readonly Func<ScrollViewer> _getScroller;
    private ScrollViewer _scroller => _getScroller();
    private readonly Action<double>? _exitTop;
    private readonly Func<double, bool>? _exitRight;
    private readonly Func<double, bool>? _exitLeft;
    private double _anchorX = double.NaN;
    private Control? _lastFocused;

    /// <param name="exitTop">Called with the current screen column when Up is pressed on the first row.</param>
    /// <param name="exitRight">Called with the current screen row (Y) when Right is pressed at a row's end; returns true if it took focus.</param>
    public FocusGrid(ScrollViewer scroller, Action<double>? exitTop = null, Func<double, bool>? exitRight = null,
        Func<double, bool>? exitLeft = null)
        : this(() => scroller, exitTop, exitRight, exitLeft)
    {
    }

    /// <summary>For scrollers that only exist once a templated control has loaded (e.g. a GridView's).</summary>
    public FocusGrid(Func<ScrollViewer> getScroller, Action<double>? exitTop = null, Func<double, bool>? exitRight = null,
        Func<double, bool>? exitLeft = null)
    {
        _getScroller = getScroller;
        _exitTop = exitTop;
        _exitRight = exitRight;
        _exitLeft = exitLeft;
    }

    /// <summary>Returns true when focus is inside the grid and the key was consumed.</summary>
    public bool TryMove(FocusNavigationDirection direction)
    {
        if (FocusManager.GetFocusedElement() is not Control current || !IsTarget(current) || !RowNavigation.Contains(_scroller, current))
            return false;

        var rows = BuildRows();
        var (r, c) = Find(rows, current);
        if (r < 0) return false;

        // Focus was moved by something else (mouse, page load): restart the column from here.
        var external = !ReferenceEquals(current, _lastFocused) || double.IsNaN(_anchorX);
        if (external) _anchorX = CenterX(current);

        switch (direction)
        {
            case FocusNavigationDirection.Left:
            case FocusNavigationDirection.Right:
            {
                var next = c + (direction == FocusNavigationDirection.Right ? 1 : -1);
                if (next >= 0 && next < rows[r].Count)
                {
                    Focus(rows[r][next], r);
                    _anchorX = CenterX(rows[r][next]);
                }
                else if (next >= rows[r].Count && _exitRight != null)
                {
                    _exitRight(CenterY(current));
                }
                else if (next < 0 && _exitLeft != null)
                {
                    _exitLeft(CenterY(current));
                }
                return true; // at the ends stay put rather than jumping rows
            }
            case FocusNavigationDirection.Up:
            case FocusNavigationDirection.Down:
            {
                var down = direction == FocusNavigationDirection.Down;
                var nr = r + (down ? 1 : -1);
                if (nr < 0)
                {
                    _lastFocused = null;
                    _exitTop?.Invoke(_anchorX);
                    _scroller.ChangeView(null, 0, null);
                    return _exitTop != null;
                }
                if (nr >= rows.Count) return true;

                var target = rows[nr];
                Control pick;
                // A lone button is a "See all" header; entering its strip from it starts at the first card.
                if (external && rows[r].Count == 1 && down)
                {
                    pick = target[0];
                    _anchorX = CenterX(pick);
                }
                else
                {
                    pick = Nearest(target, _anchorX);
                }
                Focus(pick, nr);
                return true;
            }
        }
        return false;
    }

    /// <summary>Focus the first row at the given screen column (used when coming down from the top bar).</summary>
    public bool EnterFromTop(double screenX)
    {
        var rows = BuildRows();
        if (rows.Count == 0) return false;
        _anchorX = screenX;
        Focus(Nearest(rows[0], screenX), 0);
        _scroller.ChangeView(null, 0, null);
        return true;
    }

    /// <summary>Focus the last button of the row closest to the given screen Y (coming back from a side list).</summary>
    public bool EnterFromRight(double screenY)
    {
        var rows = BuildRows();
        if (rows.Count == 0) return false;
        var row = rows.OrderBy(r => Math.Abs(CenterY(r[0]) - screenY)).First();
        var target = row[row.Count - 1];
        _anchorX = CenterX(target);
        Focus(target, rows.IndexOf(row));
        return true;
    }

    private void Focus(Control button, int rowIndex)
    {
        button.Focus(FocusState.Keyboard);
        if (rowIndex == 0)
        {
            // First row (e.g. the hero): show the page from the very top so nothing above it is hidden.
            _scroller.ChangeView(null, 0, null);
        }
        else
        {
            button.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.4,
                HorizontalAlignmentRatio = double.NaN,
            });
        }
        _lastFocused = button;
    }

    private static Control Nearest(List<Control> row, double x)
        => row.OrderBy(b => Math.Abs(CenterX(b) - x)).First();

    private static (int row, int col) Find(List<List<Control>> rows, Control b)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            var c = rows[r].IndexOf(b);
            if (c >= 0) return (r, c);
        }
        return (-1, -1);
    }

    private List<List<Control>> BuildRows()
    {
        var space = (UIElement)_scroller.Content;
        var buttons = new List<Control>();
        Collect(_scroller, buttons);

        // Y is measured in the scroll content's own space so scrolling doesn't change the grouping.
        var items = buttons
            .Select(b => (button: b, y: b.TransformToVisual(space).TransformPoint(new Point(0, b.ActualHeight / 2)).Y))
            .OrderBy(i => i.y)
            .ToList();

        var rows = new List<List<Control>>();
        var rowY = double.NaN;
        foreach (var item in items)
        {
            if (double.IsNaN(rowY) || item.y - rowY > RowTolerance)
            {
                rows.Add(new List<Control>());
                rowY = item.y;
            }
            rows[rows.Count - 1].Add(item.button);
        }
        return rows.Select(r => r.OrderBy(CenterX).ToList()).ToList();
    }

    // The kinds of control a D-pad can land on (settings rows mix buttons, combo boxes and switches).
    private static bool IsTarget(Control c)
        => c is Button or ComboBox or ToggleSwitch or CheckBox or Slider or TextBox or PasswordBox;

    private static double CenterY(UIElement e)
        => e.TransformToVisual(Window.Current.Content)
            .TransformPoint(new Point(0, ((FrameworkElement)e).ActualHeight / 2)).Y;

    private static double CenterX(UIElement e)
        => e.TransformToVisual(Window.Current.Content)
            .TransformPoint(new Point(((FrameworkElement)e).ActualWidth / 2, 0)).X;

    private static void Collect(DependencyObject root, List<Control> into)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Visibility: Visibility.Collapsed }) continue;
            if (child is Control control && IsTarget(control))
            {
                // A control's own parts never hold another focus target, so don't walk inside it.
                if (control is { IsEnabled: true, IsTabStop: true, ActualWidth: > 0 }) into.Add(control);
                continue;
            }
            Collect(child, into);
        }
    }
}

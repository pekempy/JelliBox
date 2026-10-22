using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace JelliBox.Services;

public class NavigationService
{
    private readonly Frame _frame;

    public NavigationService(Frame frame) => _frame = frame;

    public bool CanGoBack => _frame.CanGoBack;

    /// <summary>The page type currently showing.</summary>
    public Type? CurrentPageType => _frame.CurrentSourcePageType;

    public void Navigate<T>(object? parameter = null) => _frame.Navigate(typeof(T), parameter);
    public void Navigate(Type pageType, object? parameter = null) => _frame.Navigate(pageType, parameter);

    /// <summary>
    /// Navigate to a page in place of the current one, so Back skips the page being left
    /// (used when hopping between sibling libraries from the tab bar).
    /// </summary>
    public void NavigateReplacing<T>(object? parameter = null)
    {
        if (_frame.Navigate(typeof(T), parameter) && _frame.BackStack.Count > 0)
            _frame.BackStack.RemoveAt(_frame.BackStack.Count - 1);
    }

    private bool? _slideFromRight;

    /// <summary>
    /// Navigate between sibling sections. The frame's own transition is suppressed (it would carry the shared top bar
    /// along); instead the arriving page slides just its content in from the direction of travel — from the right when
    /// going to a tab further right, from the left when going left.
    /// </summary>
    public void NavigateSlide<T>(object? parameter, bool fromRight, bool replace)
    {
        _slideFromRight = fromRight;
        if (_frame.Navigate(typeof(T), parameter, new SuppressNavigationTransitionInfo()))
        {
            if (replace && _frame.BackStack.Count > 0)
                _frame.BackStack.RemoveAt(_frame.BackStack.Count - 1);
        }
        else
        {
            _slideFromRight = null;
        }
    }

    /// <summary>The slide direction requested for the page now arriving (once), or null for none.</summary>
    public bool? TakeSlide()
    {
        var slide = _slideFromRight;
        _slideFromRight = null;
        return slide;
    }

    private bool _focusBarOnArrival;

    /// <summary>Ask the next page to leave focus on the top bar's current tab instead of jumping into its content.</summary>
    public void RequestBarFocus() => _focusBarOnArrival = true;

    /// <summary>True once if the arriving page should keep focus on the top bar.</summary>
    public bool TakeBarFocusRequest()
    {
        var requested = _focusBarOnArrival;
        _focusBarOnArrival = false;
        return requested;
    }

    public void GoBack()
    {
        if (_frame.CanGoBack) _frame.GoBack();
    }

    public void ClearBackStack() => _frame.BackStack.Clear();
}

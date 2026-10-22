using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace JelliBox.Services;

/// <summary>
/// Slides a page's content in from the side while the shared top bar stays exactly where it is.
/// (A frame transition would move the whole page, bar included.)
/// </summary>
public static class PageTransition
{
    private const double Distance = 140;
    private static readonly Duration SlideTime = new(TimeSpan.FromMilliseconds(380));
    private static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(300));

    public static void SlideIn(UIElement content, bool fromRight)
    {
        var shift = content.RenderTransform as TranslateTransform;
        if (shift == null)
        {
            shift = new TranslateTransform();
            content.RenderTransform = shift;
        }

        // Start off to the side and invisible, then ease into place.
        shift.X = fromRight ? Distance : -Distance;
        content.Opacity = 0;

        var slide = new DoubleAnimation
        {
            To = 0,
            Duration = SlideTime,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slide, shift);
        Storyboard.SetTargetProperty(slide, "X");

        var fade = new DoubleAnimation
        {
            To = 1,
            Duration = FadeTime,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, content);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }
}

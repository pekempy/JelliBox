using JelliBox.Services;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace JelliBox.Controls;

/// <summary>Soft artwork behind a page, following <see cref="AmbientService"/>.</summary>
public sealed partial class AmbientBackdrop : UserControl
{
    private static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(600);

    private bool _useLayerA = true;
    private string? _shownUrl;

    public AmbientBackdrop()
    {
        InitializeComponent();
        // Set here rather than in XAML: these attributes on a user control's root crash the XAML engine at startup.
        IsHitTestVisible = false;
        IsTabStop = false;
        Loaded += (_, _) =>
        {
            AmbientService.Changed += OnChanged;
            Apply(AmbientService.CurrentUrl);
        };
        Unloaded += (_, _) => AmbientService.Changed -= OnChanged;
    }

    private void OnChanged()
        => _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Apply(AmbientService.CurrentUrl));

    private void Apply(string? url)
    {
        if (url == _shownUrl) return;
        _shownUrl = url;

        if (url == null)
        {
            Fade(LayerA, 0);
            Fade(LayerB, 0);
            Fade(Shade, 0);
            return;
        }

        var incoming = _useLayerA ? LayerA : LayerB;
        var outgoing = _useLayerA ? LayerB : LayerA;
        _useLayerA = !_useLayerA;

        var bitmap = new BitmapImage();
        bitmap.ImageOpened += (_, _) =>
        {
            if (_shownUrl != url) return; // focus has already moved on
            Fade(incoming, 1);
            Fade(outgoing, 0);
            Fade(Shade, 1);
        };
        bitmap.UriSource = new Uri(url);
        incoming.Source = bitmap;
    }

    private static void Fade(UIElement target, double to)
    {
        var animation = new DoubleAnimation { To = to, Duration = FadeTime, EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}

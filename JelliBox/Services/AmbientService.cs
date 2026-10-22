using JelliBox.Models;
using Windows.UI.Xaml;

namespace JelliBox.Services;

/// <summary>
/// Tracks which item's artwork should glow softly behind the pages. Focus moves quickly, so changes are
/// settled for a moment before the image is requested.
/// </summary>
public static class AmbientService
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    private static MediaItem? _item;
    private static DispatcherTimer? _timer;

    /// <summary>Artwork to show, or null for the plain background.</summary>
    public static string? CurrentUrl { get; private set; }

    public static event Action? Changed;

    private static bool Enabled => App.Settings.AmbientBackground;

    public static void SetItem(MediaItem? item)
    {
        _item = item;
        if (!Enabled) return;

        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = Settle };
            _timer.Tick += (_, _) =>
            {
                _timer!.Stop();
                Recompute();
            };
        }
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Re-evaluate after the setting changes.</summary>
    public static void Refresh() => Recompute();

    private static void Recompute()
    {
        var url = Enabled && _item != null ? UrlFor(_item) : null;
        if (url == CurrentUrl) return;
        CurrentUrl = url;
        Changed?.Invoke();
    }

    // A tiny image scaled up to fill the screen is what gives the soft, blurred look (and keeps it cheap).
    private static string? UrlFor(MediaItem item)
    {
        var api = App.Api;
        if (item.BackdropImageTags is { Length: > 0 })
            return api.GetImageUrl(item.Id, "Backdrop", item.BackdropImageTags[0], 320);
        if (!string.IsNullOrEmpty(item.ParentBackdropItemId))
            return api.GetImageUrl(item.ParentBackdropItemId, "Backdrop", null, 320);
        if (!string.IsNullOrEmpty(item.SeriesId))
            return api.GetImageUrl(item.SeriesId, "Backdrop", null, 320);
        if (item.ImageTags?.Primary != null)
            return api.GetImageUrl(item.Id, "Primary", item.ImageTags.Primary, 320);
        return null;
    }
}

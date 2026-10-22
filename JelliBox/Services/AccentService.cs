using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace JelliBox.Services;

/// <summary>
/// The accent colour is one shared brush used across the whole app, so changing its colour at runtime
/// recolours every button, highlight and badge at once.
/// </summary>
public static class AccentService
{
    public const string DefaultHex = "#00A4DC";

    public static readonly IReadOnlyList<(string Name, string Hex)> Presets = new List<(string, string)>
    {
        ("Jellyfin Blue", "#00A4DC"),
        ("Violet",        "#8E5CF7"),
        ("Rose",          "#F25C9A"),
        ("Crimson",       "#E5484D"),
        ("Amber",         "#F59E0B"),
        ("Emerald",       "#2ECC71"),
        ("Teal",          "#14B8A6"),
        ("Sky",           "#5B9CFF"),
    };

    public static void Apply(string? hex)
    {
        try
        {
            var color = Parse(string.IsNullOrWhiteSpace(hex) ? DefaultHex : hex!);

            SetBrush("AccentBrush", color);
            SetBrush("AccentTintBrush", Color.FromArgb(0x26, color.R, color.G, color.B));
            SetBrush("AccentGlowBrush", Color.FromArgb(0x33, color.R, color.G, color.B));
        }
        catch (Exception ex)
        {
            // The default accent is fine; never let theming stop the app from starting.
            System.Diagnostics.Debug.WriteLine($"[ACCENT] could not apply colour: {ex.Message}");
        }
    }

    public static Color Parse(string hex)
    {
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
            {
                return Color.FromArgb(0xFF,
                    Convert.ToByte(h.Substring(0, 2), 16),
                    Convert.ToByte(h.Substring(2, 2), 16),
                    Convert.ToByte(h.Substring(4, 2), 16));
            }
        }
        catch { /* fall through to the default */ }
        return Parse(DefaultHex);
    }

    private static void SetBrush(string key, Color color)
    {
        if (Find(Application.Current.Resources, key) is SolidColorBrush brush)
            brush.Color = color;
    }

    // Resources live in a merged dictionary, so search those too.
    private static object? Find(ResourceDictionary dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value)) return value;
        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, key) is { } found) return found;
        }
        return null;
    }
}

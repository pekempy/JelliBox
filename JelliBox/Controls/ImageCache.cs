using System.Collections.Generic;
using Windows.UI.Xaml.Media.Imaging;

namespace JelliBox.Controls;

/// <summary>
/// Static LRU in-memory cache for decoded BitmapImages.
/// Eliminates re-decode cost when navigating back to pages already visited.
/// 150 entries ≈ ~120 MB worst case (2× decode @ 360×540 each).
/// Must only be accessed from the UI thread.
/// </summary>
internal static class ImageCache
{
    private const int MaxSize = 150;

    private static readonly Dictionary<string, LinkedListNode<(string url, BitmapImage bmp)>> _index = new();
    private static readonly LinkedList<(string url, BitmapImage bmp)> _lru = new();

    internal static BitmapImage? Get(string url)
    {
        if (!_index.TryGetValue(url, out var node)) return null;
        // Promote to MRU position
        _lru.Remove(node);
        _lru.AddFirst(node);
        return node.Value.bmp;
    }

    internal static void Put(string url, BitmapImage bmp)
    {
        if (_index.ContainsKey(url)) return;

        // Evict LRU when full
        while (_lru.Count >= MaxSize && _lru.Last != null)
        {
            _index.Remove(_lru.Last.Value.url);
            _lru.RemoveLast();
        }

        var node = _lru.AddFirst((url, bmp));
        _index[url] = node;
    }
}

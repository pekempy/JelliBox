using JelliBox.Models;
using System.IO;
using System.Runtime.Serialization.Json;

namespace JelliBox.Services;

public class JellyfinApiService
{
    private readonly HttpClient _http;
    private readonly SettingsService _settings;

    private const string ClientName = "JelliBox";
    private const string DeviceName = "Xbox Series X";
    private const string AppVersion = "1.0.0";

    public string? UserId => _settings.UserId;
    public string? Token => _settings.Token;

    private sealed class CacheEntry
    {
        public object Value { get; }
        public DateTime ExpiresAt { get; }

        public CacheEntry(object value, TimeSpan ttl)
        {
            Value = value;
            ExpiresAt = DateTime.UtcNow + ttl;
        }

        public bool IsValid => DateTime.UtcNow < ExpiresAt;
    }

    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly object _cacheLock = new();

    private bool TryGetCached<T>(string key, out T? value) where T : class
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.IsValid && entry.Value is T casted)
            {
                value = casted;
                return true;
            }
            _cache.Remove(key);
            value = null;
            return false;
        }
    }

    // Returns cached value regardless of TTL — caller shows stale data while refreshing
    private bool TryGetCachedAny<T>(string key, out T? value) where T : class
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Value is T casted)
            {
                value = casted;
                return true;
            }
            value = null;
            return false;
        }
    }

    // ── Stale-first accessors (return immediately from cache, null if nothing cached) ──

    public List<MediaItem>? GetLibrariesCached()
    {
        TryGetCachedAny<List<MediaItem>>($"libraries_{UserId}", out var v);
        return v;
    }

    public List<MediaItem>? GetContinueWatchingCached(int limit = 10)
    {
        TryGetCachedAny<List<MediaItem>>($"continue_{UserId}_{limit}", out var v);
        return v;
    }

    public List<MediaItem>? GetLatestCached(string libraryId, int limit = 16)
    {
        TryGetCachedAny<List<MediaItem>>($"latest_{UserId}_{libraryId}_{limit}", out var v);
        return v;
    }

    public List<MediaItem>? GetItemsCached(string parentId, int limit, string sortBy, string sortOrder)
    {
        var key = $"items_{UserId}_{parentId}_{limit}_0_{sortBy}_{sortOrder}_False_____";
        TryGetCachedAny<List<MediaItem>>(key, out var v);
        return v;
    }


    private void SetCached<T>(string key, T value, TimeSpan ttl) where T : class
    {
        lock (_cacheLock)
        {
            _cache[key] = new CacheEntry(value, ttl);
        }
    }

    public void InvalidateCache(string? prefix = null)
    {
        lock (_cacheLock)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                _cache.Clear();
            }
            else
            {
                var keysToRemove = _cache.Keys.Where(k => k.StartsWith(prefix)).ToList();
                foreach (var k in keysToRemove) _cache.Remove(k);
            }
        }
    }

    public JellyfinApiService(SettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        RefreshAuthHeader();
    }

    public void RefreshAuthHeader()
    {
        var token = _settings.Token;
        var header = $"MediaBrowser Client=\"{ClientName}\", Device=\"{DeviceName}\", " +
                     $"DeviceId=\"{_settings.DeviceId}\", Version=\"{AppVersion}\"" +
                     (token != null ? $", Token=\"{token}\"" : "");
        _http.DefaultRequestHeaders.Remove("X-Emby-Authorization");
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Emby-Authorization", header);
    }

    private string Url(string path) => $"{_settings.ServerUrl?.TrimEnd('/')}{path}";

    private static T? Deserialize<T>(string json) where T : class
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ser.ReadObject(ms) as T;
    }

    private static string Serialize<T>(T obj)
    {
        var ser = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream();
        ser.WriteObject(ms, obj);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // Build simple JSON objects without external libs
    private static string J(params (string k, string v)[] pairs)
        => "{" + string.Join(",", pairs.Select(p => $"\"{p.k}\":{p.v}")) + "}";
    private static string Jstr(string s) => $"\"{ s.Replace("\\\\", "\\\\\\\\").Replace("\"", "\\\"")}\"";
    private static string Jnum(long n) => n.ToString();
    private static string Jbool(bool b) => b ? "true" : "false";

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct = default) where T : class
    {
        var r = await _http.GetAsync(Url(path), ct);
        if ((int)r.StatusCode is 301 or 302 or 307 or 308 && r.Headers.Location != null)
        {
            var targetUri = r.Headers.Location.IsAbsoluteUri ? r.Headers.Location : new Uri(new Uri(Url(path)), r.Headers.Location);
            r = await _http.GetAsync(targetUri, ct);
        }
        r.EnsureSuccessStatusCode();
        var json = await r.Content.ReadAsStringAsync();
        return Deserialize<T>(json);
    }

    private async Task PostRawAsync(string path, string json, CancellationToken ct = default)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var r = await _http.PostAsync(Url(path), content, ct);
        if ((int)r.StatusCode is 301 or 302 or 307 or 308 && r.Headers.Location != null)
        {
            var targetUri = r.Headers.Location.IsAbsoluteUri ? r.Headers.Location : new Uri(new Uri(Url(path)), r.Headers.Location);
            var content2 = new StringContent(json, Encoding.UTF8, "application/json");
            r = await _http.PostAsync(targetUri, content2, ct);
        }
        r.EnsureSuccessStatusCode();
    }

    private async Task<T?> PostAsync<T>(string path, string json, CancellationToken ct = default) where T : class
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var r = await _http.PostAsync(Url(path), content, ct);
        if ((int)r.StatusCode is 301 or 302 or 307 or 308 && r.Headers.Location != null)
        {
            var targetUri = r.Headers.Location.IsAbsoluteUri ? r.Headers.Location : new Uri(new Uri(Url(path)), r.Headers.Location);
            if (path.StartsWith("/Users/Authenticate") || path.StartsWith("/QuickConnect"))
            {
                var newBase = $"{targetUri.Scheme}://{targetUri.Authority}";
                _settings.ServerUrl = newBase;
            }
            var content2 = new StringContent(json, Encoding.UTF8, "application/json");
            r = await _http.PostAsync(targetUri, content2, ct);
        }
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return default;
        return Deserialize<T>(body);
    }

    // ── Auth & Quick Connect ──────────────────────────────────────────────────

    public async Task<AuthResult?> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        InvalidateCache();
        var result = await PostAsync<AuthResult>("/Users/AuthenticateByName",
            J(("Username", Jstr(username)), ("Pw", Jstr(password))), ct);
        if (result != null)
        {
            _settings.Token = result.AccessToken;
            _settings.UserId = result.User.Id;
            _settings.Username = result.User.Name;
            RefreshAuthHeader();
        }
        return result;
    }

    public async Task<bool> GetQuickConnectEnabledAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _http.GetAsync(Url("/QuickConnect/Enabled"), ct);
            if (r.IsSuccessStatusCode)
            {
                var body = await r.Content.ReadAsStringAsync();
                return bool.TryParse(body.Trim(), out var enabled) && enabled;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<QuickConnectResult?> InitiateQuickConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var r = await _http.PostAsync(Url("/QuickConnect/Initiate"), content, ct);
            if (r.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                return await GetAsync<QuickConnectResult>("/QuickConnect/Initiate", ct);
            }
            r.EnsureSuccessStatusCode();
            var json = await r.Content.ReadAsStringAsync();
            return Deserialize<QuickConnectResult>(json);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("405"))
        {
            return await GetAsync<QuickConnectResult>("/QuickConnect/Initiate", ct);
        }
    }

    public async Task<QuickConnectResult?> CheckQuickConnectAsync(string secret, CancellationToken ct = default)
    {
        return await GetAsync<QuickConnectResult>($"/QuickConnect/Connect?secret={Uri.EscapeDataString(secret)}", ct);
    }

    public async Task<AuthResult?> AuthenticateWithQuickConnectAsync(string secret, CancellationToken ct = default)
    {
        InvalidateCache();
        var result = await PostAsync<AuthResult>("/Users/AuthenticateWithQuickConnect",
            J(("Secret", Jstr(secret))), ct);
        if (result != null)
        {
            _settings.Token = result.AccessToken;
            _settings.UserId = result.User.Id;
            _settings.Username = !string.IsNullOrEmpty(result.User.Name) ? result.User.Name : "User";
            RefreshAuthHeader();
        }
        return result;
    }

    public async Task<AuthResult?> CompleteQuickConnectAsync(QuickConnectResult qc, CancellationToken ct = default)
    {
        InvalidateCache();
        try
        {
            var auth = await AuthenticateWithQuickConnectAsync(qc.Secret, ct);
            if (auth != null && !string.IsNullOrEmpty(auth.AccessToken))
            {
                return auth;
            }
        }
        catch
        {
            // Fallback if AuthenticateWithQuickConnect route is not supported
        }

        if (!string.IsNullOrEmpty(qc.AuthenticationToken) && !string.IsNullOrEmpty(qc.UserId))
        {
            _settings.Token = qc.AuthenticationToken;
            _settings.UserId = qc.UserId;
            RefreshAuthHeader();

            var authUser = new AuthUser { Id = qc.UserId, Name = "User" };
            try
            {
                var user = await GetUserAsync(qc.UserId, ct);
                if (user != null && !string.IsNullOrEmpty(user.Name))
                {
                    authUser.Name = user.Name;
                    _settings.Username = user.Name;
                }
            }
            catch { }

            return new AuthResult
            {
                AccessToken = qc.AuthenticationToken,
                User = authUser
            };
        }

        return null;
    }

    public Task<AuthUser?> GetUserAsync(string userId, CancellationToken ct = default)
        => GetAsync<AuthUser>($"/Users/{userId}", ct);

    // ── Library ───────────────────────────────────────────────────────────────

    public Task<List<MediaItem>> GetLibrariesAsync(CancellationToken ct = default)
        => GetLibrariesAsync(false, ct);

    public async Task<List<MediaItem>> GetLibrariesAsync(bool forceRefresh, CancellationToken ct = default)
    {
        var cacheKey = $"libraries_{UserId}";
        if (!forceRefresh && TryGetCached<List<MediaItem>>(cacheKey, out var cached) && cached != null)
            return cached;

        var r = await GetAsync<ItemsResult<MediaItem>>($"/Users/{UserId}/Views", ct);
        var result = r?.Items.ToList() ?? new List<MediaItem>();
        if (result.Count > 0)
            SetCached(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    public Task<List<MediaItem>> GetContinueWatchingAsync(int limit = 20, CancellationToken ct = default)
        => GetContinueWatchingAsync(limit, false, ct);

    public async Task<List<MediaItem>> GetContinueWatchingAsync(int limit, bool forceRefresh, CancellationToken ct = default)
    {
        var cacheKey = $"continue_{UserId}_{limit}";
        if (!forceRefresh && TryGetCached<List<MediaItem>>(cacheKey, out var cached) && cached != null)
            return cached;

        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Users/{UserId}/Items/Resume?Limit={limit}&MediaTypes=Video&Recursive=true" +
            "&Fields=Overview,BackdropImageTags,UserData,SeriesName", ct);
        var result = r?.Items.ToList() ?? new List<MediaItem>();
        SetCached(cacheKey, result, TimeSpan.FromSeconds(30));
        return result;
    }

    public Task<List<MediaItem>> GetLatestAsync(string libraryId, int limit = 16, CancellationToken ct = default)
        => GetLatestAsync(libraryId, limit, false, ct);

    public async Task<List<MediaItem>> GetLatestAsync(string libraryId, int limit, bool forceRefresh, CancellationToken ct = default)
    {
        var cacheKey = $"latest_{UserId}_{libraryId}_{limit}";
        if (!forceRefresh && TryGetCached<List<MediaItem>>(cacheKey, out var cached) && cached != null)
            return cached;

        var items = await GetAsync<MediaItem[]>(
            $"/Users/{UserId}/Items/Latest?ParentId={libraryId}&Limit={limit}" +
            "&Fields=Overview,BackdropImageTags,UserData,SeriesName,ChildCount,RecursiveItemCount", ct);
        var result = items?.ToList() ?? new List<MediaItem>();
        if (result.Count > 0)
            SetCached(cacheKey, result, TimeSpan.FromMinutes(3));
        return result;
    }

    public async Task<List<MediaItem>> GetItemsAsync(
        string parentId, int limit = 40, int startIndex = 0,
        string sortBy = "SortName", string sortOrder = "Ascending",
        bool recursive = true,
        string? nameStartsWith = null,
        string? nameLessThan = null,
        bool? hasSubtitles = null,
        string? genres = null,
        string? filters = null,
        CancellationToken ct = default)
    {
        var letterParam = "";
        if (!string.IsNullOrEmpty(nameStartsWith))
            letterParam = $"&nameStartsWith={Uri.EscapeDataString(nameStartsWith)}";
        else if (!string.IsNullOrEmpty(nameLessThan))
            letterParam = $"&nameLessThan={Uri.EscapeDataString(nameLessThan)}";

        var subParam = hasSubtitles.HasValue ? $"&hasSubtitles={(hasSubtitles.Value ? "true" : "false")}" : "";

        var genreParam = string.IsNullOrEmpty(genres) ? "" : $"&Genres={Uri.EscapeDataString(genres)}";
        var filterParam = string.IsNullOrEmpty(filters) ? "" : $"&Filters={filters}";

        var cacheKey = $"items_{UserId}_{parentId}_{limit}_{startIndex}_{sortBy}_{sortOrder}_{recursive}_{nameStartsWith}_{nameLessThan}_{hasSubtitles}_{genres}_{filters}";
        if (startIndex == 0 && TryGetCached<List<MediaItem>>(cacheKey, out var cached) && cached != null)
            return cached;

        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Users/{UserId}/Items?ParentId={parentId}&Limit={limit}&StartIndex={startIndex}" +
            $"&SortBy={sortBy}&SortOrder={sortOrder}&Recursive={(recursive ? "true" : "false")}" +
            $"&Fields=Overview,BackdropImageTags,UserData,SeriesName,ChildCount,RecursiveItemCount,MediaStreams,Studios,PremiereDate,SortName{letterParam}{subParam}{genreParam}{filterParam}", ct);
        var result = r?.Items.ToList() ?? new List<MediaItem>();
        if (startIndex == 0 && result.Count > 0)
            SetCached(cacheKey, result, TimeSpan.FromMinutes(2));
        return result;
    }

    /// <summary>Genres used by the items in a library, for the filter list.</summary>
    public async Task<List<string>> GetGenresAsync(string parentId, CancellationToken ct = default)
    {
        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Genres?ParentId={parentId}&UserId={UserId}&SortBy=SortName&SortOrder=Ascending", ct);
        return r?.Items.Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();
    }

    /// <summary>Number of items in the library sorting before <paramref name="nameLessThan"/>, i.e. the index where a letter starts.</summary>
    public async Task<int> GetItemCountAsync(
        string parentId, string? nameLessThan = null, bool? hasSubtitles = null,
        string? genres = null, string? filters = null, CancellationToken ct = default)
    {
        var letterParam = string.IsNullOrEmpty(nameLessThan) ? "" : $"&nameLessThan={Uri.EscapeDataString(nameLessThan)}";
        var subParam = hasSubtitles.HasValue ? $"&hasSubtitles={(hasSubtitles.Value ? "true" : "false")}" : "";
        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Users/{UserId}/Items?ParentId={parentId}&Limit=1&Recursive=false&EnableTotalRecordCount=true{letterParam}{subParam}" +
            (string.IsNullOrEmpty(genres) ? "" : $"&Genres={Uri.EscapeDataString(genres)}") +
            (string.IsNullOrEmpty(filters) ? "" : $"&Filters={filters}"), ct);
        return r?.TotalRecordCount ?? 0;
    }

    public Task<MediaItem?> GetItemAsync(string itemId, CancellationToken ct = default)
        => GetItemAsync(itemId, false, ct);

    public async Task<MediaItem?> GetItemAsync(string itemId, bool forceRefresh, CancellationToken ct = default)
    {
        var cacheKey = $"item_{UserId}_{itemId}";
        if (!forceRefresh && TryGetCached<MediaItem>(cacheKey, out var cached) && cached != null)
            return cached;

        var result = await GetAsync<MediaItem>(
            $"/Users/{UserId}/Items/{itemId}?Fields=Overview,BackdropImageTags,Genres,MediaSources,UserData,SeriesName,People,MediaStreams,Studios,PremiereDate", ct);
        if (result != null)
            SetCached(cacheKey, result, TimeSpan.FromMinutes(2));
        return result;
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    public async Task<PlaybackInfoResult?> GetPlaybackInfoAsync(string itemId, CancellationToken ct = default)
    {
        var req = new PlaybackInfoRequest
        {
            DeviceProfile = new DeviceProfileDto
            {
                DirectPlayProfiles = new[] { new DirectPlayProfileDto() },
                TranscodingProfiles = new[] { new TranscodingProfileDto() }
            }
        };
        return await PostAsync<PlaybackInfoResult>(
            $"/Items/{itemId}/PlaybackInfo?UserId={UserId}&StartTimeTicks=0",
            Serialize(req), ct);
    }

    public string GetStreamUrl(
        MediaItem item, MediaSource source,
        int? audioStreamIndex = null,
        int? subtitleStreamIndex = null,
        long? maxBitrate = null,
        string? playSessionId = null,
        bool burnSubtitles = false)
    {
        var bitrate = maxBitrate ?? _settings.MaxStreamingBitrate;
        var bitrateParam = bitrate > 0 ? $"&MaxStreamingBitrate={bitrate}&VideoBitrate={bitrate}" : "";
        var audioParam = audioStreamIndex.HasValue ? $"&AudioStreamIndex={audioStreamIndex.Value}" : "";
        var subParam = "";
        var copyVideoParam = "";
        if (burnSubtitles && subtitleStreamIndex.HasValue && subtitleStreamIndex.Value >= 0)
        {
            subParam = $"&SubtitleStreamIndex={subtitleStreamIndex.Value}&SubtitleMethod=Encode";
            copyVideoParam = "&AllowVideoStreamCopy=false";
        }
        else if (burnSubtitles && subtitleStreamIndex.HasValue && subtitleStreamIndex.Value < 0)
        {
            subParam = "&SubtitleStreamIndex=-1";
        }
        var sessionParam = !string.IsNullOrEmpty(playSessionId) ? $"&PlaySessionId={playSessionId}" : "";

        // If audio track override, bitrate override, burn-in subtitle, or server transcoding is requested, use HLS with params
        if (audioStreamIndex.HasValue || (burnSubtitles && subtitleStreamIndex.HasValue && subtitleStreamIndex.Value >= 0) || bitrate > 0 || !source.SupportsDirectPlay)
        {
            return Url($"/Videos/{item.Id}/master.m3u8?ApiKey={Token}&api_key={Token}&MediaSourceId={source.Id}" +
                $"&DeviceId={_settings.DeviceId}&VideoCodec=h264&AudioCodec=aac{bitrateParam}{audioParam}{subParam}{copyVideoParam}{sessionParam}");
        }

        if (!string.IsNullOrEmpty(source.TranscodingUrl))
        {
            return Url(source.TranscodingUrl);
        }
        return GetDirectStreamUrl(item.Id, source.Id);
    }

    public string GetSubtitleStreamUrl(string itemId, string mediaSourceId, int subtitleIndex, string format = "vtt") =>
        Url($"/Videos/{itemId}/{mediaSourceId}/Subtitles/{subtitleIndex}/Stream.{format}?api_key={Token}&ApiKey={Token}");

    public string GetDirectStreamUrl(string itemId, string mediaSourceId) =>
        Url($"/Videos/{itemId}/stream?static=true&ApiKey={Token}&api_key={Token}&MediaSourceId={mediaSourceId}");

    public string GetHlsUrl(string itemId, string mediaSourceId)
    {
        var bitrate = _settings.MaxStreamingBitrate;
        var bitrateParam = bitrate > 0 ? $"&MaxStreamingBitrate={bitrate}&VideoBitrate={bitrate}" : "";
        return Url($"/Videos/{itemId}/master.m3u8?ApiKey={Token}&api_key={Token}&MediaSourceId={mediaSourceId}" +
            $"&DeviceId={_settings.DeviceId}&VideoCodec=h264&AudioCodec=aac{bitrateParam}");
    }

    public string GetImageUrl(string itemId, string imageType = "Primary", string? tag = null, int width = 320, int height = 0)
    {
        var targetWidth = width > 0 ? width : 320;
        var hParam = height > 0 ? $"&height={height}" : "";
        var q = tag != null ? $"&tag={tag}" : "";
        return Url($"/Items/{itemId}/Images/{imageType}?width={targetWidth}{hParam}&quality=80{q}");
    }

    // ── Live TV ───────────────────────────────────────────────────────────────

    public async Task<List<LiveTvChannel>> GetLiveTvChannelsAsync(int limit = 100, CancellationToken ct = default)
    {
        var cacheKey = $"livetv_channels_{UserId}_{limit}";
        if (TryGetCached<List<LiveTvChannel>>(cacheKey, out var cached) && cached != null)
            return cached;

        var r = await GetAsync<ItemsResult<LiveTvChannel>>(
            $"/LiveTv/Channels?UserId={UserId}&Limit={limit}&EnableFavoriteSorting=true", ct);
        var result = r?.Items.ToList() ?? new List<LiveTvChannel>();
        foreach (var ch in result)
        {
            if (ch.ImageTags?.Primary != null)
                ch.LogoUrl = GetImageUrl(ch.Id, "Primary", ch.ImageTags.Primary, 160);
        }
        if (result.Count > 0)
            SetCached(cacheKey, result, TimeSpan.FromSeconds(60));
        return result;
    }

    public async Task<List<ProgramItem>> GetChannelProgramsAsync(string channelId, int limit = 6, CancellationToken ct = default)
    {
        var r = await GetAsync<ItemsResult<ProgramItem>>(
            $"/LiveTv/Programs?UserId={UserId}&ChannelIds={channelId}&Limit={limit}", ct);
        return r?.Items.ToList() ?? new List<ProgramItem>();
    }

    // ── Scrobbling ────────────────────────────────────────────────────────────

    public async Task ReportStartAsync(string itemId, string mediaSourceId, string sessionId)
    {
        try { await PostRawAsync("/Sessions/Playing",
            J(("ItemId", Jstr(itemId)),
              ("MediaSourceId", Jstr(mediaSourceId)),
              ("PlaySessionId", Jstr(sessionId)),
              ("CanSeek", Jbool(true)))); }
        catch { }
    }

    public async Task ReportProgressAsync(string itemId, string mediaSourceId, long ticks, bool paused, string sessionId, string eventName = "timeupdate")
    {
        try { await PostRawAsync("/Sessions/Playing/Progress",
            J(("ItemId", Jstr(itemId)),
              ("MediaSourceId", Jstr(mediaSourceId)),
              ("PositionTicks", Jnum(ticks)),
              ("IsPaused", Jbool(paused)),
              ("PlaySessionId", Jstr(sessionId)),
              ("EventName", Jstr(eventName)),
              ("CanSeek", Jbool(true)))); }
        catch { }
    }

    public async Task ReportStopAsync(string itemId, string mediaSourceId, long ticks, string sessionId)
    {
        try { await PostRawAsync("/Sessions/Playing/Stopped",
            J(("ItemId", Jstr(itemId)),
              ("MediaSourceId", Jstr(mediaSourceId)),
              ("PositionTicks", Jnum(ticks)),
              ("PlaySessionId", Jstr(sessionId)))); }
        catch { }
        InvalidateCache($"item_{UserId}_{itemId}");
        InvalidateCache($"continue_{UserId}");
        InvalidateCache($"latest_{UserId}");
        InvalidateCache($"items_{UserId}");
    }

    public async Task ToggleWatchedAsync(string itemId, bool markWatched, CancellationToken ct = default)
    {
        if (markWatched)
            await _http.PostAsync(Url($"/Users/{UserId}/PlayedItems/{itemId}"), null, ct);
        else
            await _http.DeleteAsync(Url($"/Users/{UserId}/PlayedItems/{itemId}"), ct);

        InvalidateCache($"item_{UserId}_{itemId}");
        InvalidateCache($"continue_{UserId}");
        InvalidateCache($"latest_{UserId}");
    }

    public async Task RemoveFromContinueWatchingAsync(string itemId, CancellationToken ct = default)
    {
        // 1. Reset resume ticks on server via Playing/Stopped with PositionTicks: 0
        try
        {
            await PostRawAsync("/Sessions/Playing/Stopped",
                J(("ItemId", Jstr(itemId)),
                  ("PositionTicks", Jnum(0))), ct);
        }
        catch { }

        // 2. Playstate PlayingItems delete endpoint with positionTicks=0
        try
        {
            await _http.DeleteAsync(Url($"/Users/{UserId}/PlayingItems/{itemId}?positionTicks=0"), ct);
        }
        catch { }

        // 3. Discontinue-watching community plugin support
        try
        {
            await _http.PostAsync(Url($"/DiscontinueWatching/{itemId}"), null, ct);
        }
        catch { }

        InvalidateCache($"item_{UserId}_{itemId}");
        InvalidateCache($"continue_{UserId}");
        InvalidateCache($"latest_{UserId}");
    }

    public async Task<bool> ToggleFavoriteAsync(string itemId, bool markFavorite, CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage resp;
            if (markFavorite)
                resp = await _http.PostAsync(Url($"/Users/{UserId}/FavoriteItems/{itemId}"), null, ct);
            else
                resp = await _http.DeleteAsync(Url($"/Users/{UserId}/FavoriteItems/{itemId}"), ct);

            InvalidateCache($"item_{UserId}_{itemId}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<MediaItem>> GetCollectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await GetAsync<ItemsResult<MediaItem>>($"/Users/{UserId}/Items?IncludeItemTypes=BoxSet&Recursive=true", ct);
            return r?.Items.ToList() ?? new List<MediaItem>();
        }
        catch { return new List<MediaItem>(); }
    }

    public async Task<bool> AddToCollectionAsync(string collectionId, string itemId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync(Url($"/Collections/{collectionId}/Items?Ids={itemId}"), null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> CreateCollectionAsync(string name, string itemId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync(Url($"/Collections?Name={Uri.EscapeDataString(name)}&Ids={itemId}"), null, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                if (Windows.Data.Json.JsonObject.TryParse(json, out var obj) && obj.ContainsKey("Id"))
                    return obj.GetNamedString("Id");
            }
        }
        catch { }
        return null;
    }

    public async Task<List<MediaItem>> GetArtistAlbumsAsync(string artistId, CancellationToken ct = default)
    {
        try
        {
            var r = await GetAsync<ItemsResult<MediaItem>>(
                $"/Users/{UserId}/Items?ArtistIds={artistId}&IncludeItemTypes=MusicAlbum&Recursive=true&SortBy=ProductionYear,SortName&SortOrder=Descending", ct);
            return r?.Items.ToList() ?? new List<MediaItem>();
        }
        catch { return new List<MediaItem>(); }
    }

    public async Task<List<MediaItem>> GetArtistSongsAsync(string artistId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var r = await GetAsync<ItemsResult<MediaItem>>(
                $"/Users/{UserId}/Items?ArtistIds={artistId}&IncludeItemTypes=Audio&Recursive=true&Limit={limit}&SortBy=PlayCount,SortName&SortOrder=Descending", ct);
            return r?.Items.ToList() ?? new List<MediaItem>();
        }
        catch { return new List<MediaItem>(); }
    }

    // ── Search & Discovery ────────────────────────────────────────────────────

    public async Task<List<MediaItem>> SearchMediaAsync(
        string query, string? itemTypes = "Movie,Series,Episode",
        string? genre = null, string? year = null,
        string sortBy = "SortName", string sortOrder = "Ascending",
        CancellationToken ct = default)
    {
        var genreParam = !string.IsNullOrEmpty(genre) && genre != "All" ? $"&Genres={Uri.EscapeDataString(genre)}" : "";
        var yearParam = !string.IsNullOrEmpty(year) && year != "All" ? $"&Years={Uri.EscapeDataString(year)}" : "";

        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Users/{UserId}/Items?searchTerm={Uri.EscapeDataString(query)}&IncludeItemTypes={itemTypes}" +
            $"&Recursive=true&SortBy={sortBy}&SortOrder={sortOrder}&Limit=40" +
            $"{genreParam}{yearParam}&Fields=Overview,BackdropImageTags,UserData,SeriesName,ChildCount,RecursiveItemCount", ct);

        return r?.Items.ToList() ?? new List<MediaItem>();
    }

    public async Task<List<Person>> SearchPeopleAsync(string query, CancellationToken ct = default)
    {
        var r = await GetAsync<ItemsResult<Person>>(
            $"/Users/{UserId}/Items?searchTerm={Uri.EscapeDataString(query)}&IncludeItemTypes=Person" +
            "&Recursive=true&Limit=16", ct);

        var list = r?.Items.ToList() ?? new List<Person>();
        foreach (var p in list)
        {
            if (p.PrimaryImageTag != null)
                p.ImageUrl = GetImageUrl(p.Id, "Primary", p.PrimaryImageTag, 200);
        }
        return list;
    }

    public async Task<List<string>> GetGenresAsync(CancellationToken ct = default)
    {
        var cacheKey = "genres_list";
        if (TryGetCached<List<string>>(cacheKey, out var cached) && cached != null)
            return cached;

        var r = await GetAsync<ItemsResult<MediaItem>>($"/Genres?UserId={UserId}", ct);
        var result = r?.Items.Select(g => g.Name).Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n).ToList()
                     ?? new List<string>();
        if (result.Count > 0)
            SetCached(cacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    // ── Actor & Filmography ───────────────────────────────────────────────────

    public async Task<List<MediaItem>> GetPersonLibraryItemsAsync(string personName, CancellationToken ct = default)
    {
        var r = await GetAsync<ItemsResult<MediaItem>>(
            $"/Users/{UserId}/Items?Person={Uri.EscapeDataString(personName)}&Recursive=true" +
            "&IncludeItemTypes=Movie,Series&Fields=Overview,BackdropImageTags,UserData,SeriesName,People", ct);

        return r?.Items.ToList() ?? new List<MediaItem>();
    }

    public async Task<(string? bio, string? desc, string? imgUrl, string? wikibaseItem)> GetOnlinePersonBioAsync(string personName)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "JelliBox/1.0");
            var nameSlug = personName.Trim().Replace(' ', '_');
            var json = await client.GetStringAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(nameSlug)}");
            if (Windows.Data.Json.JsonObject.TryParse(json, out var obj))
            {
                var bio = obj.ContainsKey("extract") ? obj.GetNamedString("extract") : null;
                var desc = obj.ContainsKey("description") ? obj.GetNamedString("description") : null;
                var wikiId = obj.ContainsKey("wikibase_item") ? obj.GetNamedString("wikibase_item") : null;
                string? img = null;
                if (obj.ContainsKey("thumbnail") && obj.GetNamedObject("thumbnail").ContainsKey("source"))
                    img = obj.GetNamedObject("thumbnail").GetNamedString("source");

                return (bio, desc, img, wikiId);
            }
        }
        catch { /* Wikipedia is optional enrichment */ }
        return (null, null, null, null);
    }

    private readonly Dictionary<string, Dictionary<int, string>> _seasonNamesCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Dictionary<int, string>> GetOnlineSeasonNamesAsync(string showName)
    {
        if (string.IsNullOrWhiteSpace(showName)) return new Dictionary<int, string>();
        if (_seasonNamesCache.TryGetValue(showName, out var cached)) return cached;

        var result = new Dictionary<int, string>();
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "JelliBox/1.0");

            var slug = showName.Trim().Replace(' ', '_');
            var summaryJson = await client.GetStringAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(slug)}");
            if (!Windows.Data.Json.JsonObject.TryParse(summaryJson, out var sumObj) || !sumObj.ContainsKey("wikibase_item"))
                return result;

            var qid = sumObj.GetNamedString("wikibase_item");
            if (string.IsNullOrEmpty(qid)) return result;

            var sparql = "SELECT ?ordinal ?seasonLabel WHERE {" +
                         $"  ?season wdt:P179 wd:{qid};" +
                         "          wdt:P31 wd:Q3464665;" +
                         "          p:P179 ?statement." +
                         $"  ?statement ps:P179 wd:{qid}." +
                         "  OPTIONAL { ?statement pq:P1545 ?ordinal. }" +
                         "  SERVICE wikibase:label { bd:serviceParam wikibase:language 'en'. }" +
                         "}";
            var url = $"https://query.wikidata.org/sparql?query={Uri.EscapeDataString(sparql)}&format=json";
            var sparqlJson = await client.GetStringAsync(url);
            if (Windows.Data.Json.JsonObject.TryParse(sparqlJson, out var spObj) && spObj.ContainsKey("results"))
            {
                var bindings = spObj.GetNamedObject("results").GetNamedArray("bindings");
                foreach (var b in bindings)
                {
                    var bObj = b.GetObject();
                    if (bObj.ContainsKey("ordinal") && bObj.ContainsKey("seasonLabel"))
                    {
                        var ordStr = bObj.GetNamedObject("ordinal").GetNamedString("value");
                        var label = bObj.GetNamedObject("seasonLabel").GetNamedString("value");
                        if (int.TryParse(ordStr, out var num) && !string.IsNullOrWhiteSpace(label))
                        {
                            if (label.Contains(':'))
                                label = label.Substring(label.IndexOf(':') + 1).Trim();
                            result[num] = label;
                        }
                    }
                }
            }
        }
        catch { /* Optional online enrichment */ }

        _seasonNamesCache[showName] = result;
        return result;
    }

    public async Task<List<(string title, string year, string? role, string? mediaType)>> GetWikidataFilmographyAsync(string wikibaseItem)
    {
        var list = new List<(string title, string year, string? role, string? mediaType)>();
        if (string.IsNullOrEmpty(wikibaseItem)) return list;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "JelliBox/1.0");
            var sparql = "SELECT DISTINCT ?workLabel ?year ?typeLabel ?roleLabel ?charName WHERE {" +
                         "  ?work p:P161 ?statement." +
                         $" ?statement ps:P161 wd:{wikibaseItem}." +
                         "  OPTIONAL { ?statement pq:P453 ?character. ?character rdfs:label ?roleLabel. FILTER(LANG(?roleLabel) = 'en') }" +
                         "  OPTIONAL { ?statement pq:P4633 ?charName. }" +
                         "  OPTIONAL { ?work wdt:P577 ?date. BIND(YEAR(?date) AS ?year) }" +
                         "  OPTIONAL { ?work wdt:P31 ?typeItem. VALUES ?typeItem { wd:Q11424 wd:Q5398426 } BIND(IF(?typeItem = wd:Q5398426, 'TV Show', 'Movie') AS ?typeLabel) }" +
                         "  SERVICE wikibase:label { bd:serviceParam wikibase:language 'en'. }" +
                         "} ORDER BY DESC(?year) LIMIT 30";
            var url = $"https://query.wikidata.org/sparql?query={Uri.EscapeDataString(sparql)}&format=json";
            var json = await client.GetStringAsync(url);
            if (Windows.Data.Json.JsonObject.TryParse(json, out var obj) && obj.ContainsKey("results"))
            {
                var bindings = obj.GetNamedObject("results").GetNamedArray("bindings");
                foreach (var b in bindings)
                {
                    var bObj = b.GetObject();
                    if (bObj.ContainsKey("workLabel"))
                    {
                        var title = bObj.GetNamedObject("workLabel").GetNamedString("value");
                        var year = bObj.ContainsKey("year") ? bObj.GetNamedObject("year").GetNamedString("value") : "";
                        var role = bObj.ContainsKey("roleLabel") ? bObj.GetNamedObject("roleLabel").GetNamedString("value") :
                                   (bObj.ContainsKey("charName") ? bObj.GetNamedObject("charName").GetNamedString("value") : null);
                        var mType = bObj.ContainsKey("typeLabel") ? bObj.GetNamedObject("typeLabel").GetNamedString("value") : null;

                        if (!string.IsNullOrEmpty(title) && !title.StartsWith("Q", StringComparison.Ordinal) && !list.Any(x => x.title == title))
                        {
                            list.Add((title, year, role, mType));
                        }
                    }
                }
            }
        }
        catch { /* Wikidata is optional enrichment */ }
        return list;
    }

    public async Task<List<MediaSegment>> GetMediaSegmentsAsync(string itemId, MediaItem? item = null, CancellationToken ct = default)
    {
        var segments = new List<MediaSegment>();
        if (string.IsNullOrEmpty(itemId)) return segments;

        // 1. Jellyfin 10.10+ native MediaSegments API: GET /MediaSegments/{itemId}
        try
        {
            var resp = await _http.GetAsync(Url($"/MediaSegments/{itemId}"), ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                if (Windows.Data.Json.JsonObject.TryParse(json, out var root) && root.ContainsKey("Items"))
                {
                    var items = root.GetNamedArray("Items");
                    foreach (var val in items)
                    {
                        if (val.ValueType == Windows.Data.Json.JsonValueType.Object)
                        {
                            var obj = val.GetObject();
                            var type = obj.ContainsKey("Type") ? obj.GetNamedString("Type") : "";
                            long startTicks = 0;
                            long endTicks = 0;
                            if (obj.ContainsKey("StartTicks")) startTicks = (long)obj.GetNamedNumber("StartTicks");
                            if (obj.ContainsKey("EndTicks")) endTicks = (long)obj.GetNamedNumber("EndTicks");

                            if (endTicks > startTicks)
                            {
                                segments.Add(new MediaSegment
                                {
                                    Type = type,
                                    Start = TimeSpan.FromTicks(startTicks),
                                    End = TimeSpan.FromTicks(endTicks)
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { /* Fallback */ }

        if (segments.Count > 0) return segments;

        // 2. Jellyfin Intro Skipper plugin endpoint: GET /Episode/{itemId}/IntroTimestamps
        try
        {
            var epResp = await _http.GetAsync(Url($"/Episode/{itemId}/IntroTimestamps"), ct);
            if (epResp.IsSuccessStatusCode)
            {
                var json = await epResp.Content.ReadAsStringAsync();
                if (Windows.Data.Json.JsonObject.TryParse(json, out var root))
                {
                    bool valid = !root.ContainsKey("Valid") || root.GetNamedBoolean("Valid");
                    if (valid)
                    {
                        double introStart = -1, introEnd = -1;
                        if (root.ContainsKey("IntroStart")) introStart = root.GetNamedNumber("IntroStart");
                        else if (root.ContainsKey("Start")) introStart = root.GetNamedNumber("Start");

                        if (root.ContainsKey("IntroEnd")) introEnd = root.GetNamedNumber("IntroEnd");
                        else if (root.ContainsKey("End")) introEnd = root.GetNamedNumber("End");

                        if (introStart >= 0 && introEnd > introStart)
                        {
                            segments.Add(new MediaSegment
                            {
                                Type = "Intro",
                                Start = TimeSpan.FromSeconds(introStart),
                                End = TimeSpan.FromSeconds(introEnd)
                            });
                        }

                        double outroStart = -1, outroEnd = -1;
                        if (root.ContainsKey("OutroStart")) outroStart = root.GetNamedNumber("OutroStart");
                        if (root.ContainsKey("OutroEnd")) outroEnd = root.GetNamedNumber("OutroEnd");

                        if (outroStart >= 0 && outroEnd > outroStart)
                        {
                            segments.Add(new MediaSegment
                            {
                                Type = "Outro",
                                Start = TimeSpan.FromSeconds(outroStart),
                                End = TimeSpan.FromSeconds(outroEnd)
                            });
                        }
                    }
                }
            }
        }
        catch { /* Fallback */ }

        if (segments.Count > 0) return segments;

        // 3. Chapter-based fallback from Item.Chapters
        if (item?.Chapters != null && item.Chapters.Length > 0)
        {
            for (int i = 0; i < item.Chapters.Length; i++)
            {
                var ch = item.Chapters[i];
                var name = ch.Name ?? "";
                var isIntro = name.IndexOf("Intro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Opening", StringComparison.OrdinalIgnoreCase) >= 0;
                var isOutro = name.IndexOf("Outro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Credits", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.IndexOf("Ending", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isIntro || isOutro)
                {
                    var start = TimeSpan.FromTicks(ch.StartPositionTicks);
                    TimeSpan end;
                    if (i + 1 < item.Chapters.Length)
                    {
                        end = TimeSpan.FromTicks(item.Chapters[i + 1].StartPositionTicks);
                    }
                    else if (item.RunTimeTicks.HasValue)
                    {
                        end = TimeSpan.FromTicks(item.RunTimeTicks.Value);
                    }
                    else
                    {
                        end = start + TimeSpan.FromSeconds(90);
                    }

                    if (end > start)
                    {
                        segments.Add(new MediaSegment
                        {
                            Type = isIntro ? "Intro" : "Outro",
                            Start = start,
                            End = end
                        });
                    }
                }
            }
        }

        return segments;
    }
}

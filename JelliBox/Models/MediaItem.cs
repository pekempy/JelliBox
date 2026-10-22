using System.Runtime.Serialization;

namespace JelliBox.Models;

[DataContract]
public class MediaItem
{
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "SortName")] public string? SortName { get; set; }
    [DataMember(Name = "OriginalTitle")] public string? OriginalTitle { get; set; }
    [DataMember(Name = "Type")] public string Type { get; set; } = "";
    [DataMember(Name = "Overview")] public string? Overview { get; set; }
    [DataMember(Name = "ProductionYear")] public int? ProductionYear { get; set; }
    [DataMember(Name = "RunTimeTicks")] public long? RunTimeTicks { get; set; }
    [DataMember(Name = "OfficialRating")] public string? OfficialRating { get; set; }
    [DataMember(Name = "CommunityRating")] public double? CommunityRating { get; set; }
    [DataMember(Name = "Genres")] public string[]? Genres { get; set; }
    [DataMember(Name = "SeriesName")] public string? SeriesName { get; set; }
    [DataMember(Name = "SeriesId")] public string? SeriesId { get; set; }
    [DataMember(Name = "SeasonId")] public string? SeasonId { get; set; }
    [DataMember(Name = "AlbumId")] public string? AlbumId { get; set; }
    [DataMember(Name = "Album")] public string? Album { get; set; }
    [DataMember(Name = "AlbumArtist")] public string? AlbumArtist { get; set; }
    [DataMember(Name = "Artists")] public string[]? Artists { get; set; }
    [DataMember(Name = "ParentId")] public string? ParentId { get; set; }
    [DataMember(Name = "ParentBackdropItemId")] public string? ParentBackdropItemId { get; set; }
    [DataMember(Name = "BackdropImageTags")] public string[]? BackdropImageTags { get; set; }
    [DataMember(Name = "ImageTags")] public ImageTags? ImageTags { get; set; }
    [DataMember(Name = "UserData")] public UserData? UserData { get; set; }
    [DataMember(Name = "IndexNumber")] public int? IndexNumber { get; set; }
    [DataMember(Name = "ParentIndexNumber")] public int? ParentIndexNumber { get; set; }
    [DataMember(Name = "ChildCount")] public int? ChildCount { get; set; }
    [DataMember(Name = "RecursiveItemCount")] public int? RecursiveItemCount { get; set; }
    [DataMember(Name = "MediaSources")] public MediaSource[]? MediaSources { get; set; }
    [DataMember(Name = "MediaType")] public string? MediaType { get; set; }
    [DataMember(Name = "CollectionType")] public string? CollectionType { get; set; }
    [DataMember(Name = "People")] public Person[]? People { get; set; }
    [DataMember(Name = "PremiereDate")] public string? PremiereDate { get; set; }
    [DataMember(Name = "Studios")] public StudioItem[]? Studios { get; set; }
    [DataMember(Name = "MediaStreams")] public MediaStream[]? MediaStreams { get; set; }
    [DataMember(Name = "HasSubtitles")] public bool? ApiHasSubtitles { get; set; }
    [DataMember(Name = "Chapters")] public ChapterInfo[]? Chapters { get; set; }

    /// <summary>A Jellyfin collection (box set) that groups other titles.</summary>
    public bool IsCollection => Type == "BoxSet";

    public TimeSpan? Runtime => RunTimeTicks.HasValue ? TimeSpan.FromTicks(RunTimeTicks.Value) : null;

    public bool IsMusic =>
        string.Equals(MediaType, "Audio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(CollectionType, "music", StringComparison.OrdinalIgnoreCase)
        || Type is "MusicAlbum" or "MusicArtist" or "Artist" or "Audio" or "MusicVideo" or "MusicGenre" or "Soundtrack"
        || !string.IsNullOrEmpty(AlbumId)
        || !string.IsNullOrEmpty(AlbumArtist);

    public bool IsArtist =>
        Type is "MusicArtist" or "Artist";

    public bool IsAlbum =>
        Type is "MusicAlbum" or "Album" || (!string.IsNullOrEmpty(AlbumArtist) && Type != "Audio" && Type != "Episode" && Type != "Movie" && Type != "Series");

    public string? NetworkInfo => Studios?.Length > 0
        ? string.Join(", ", Studios.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(2))
        : null;

    public string? FirstAirDate
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PremiereDate) && DateTime.TryParse(PremiereDate, out var dt))
                return dt.ToString("MMM d, yyyy");
            if (ProductionYear.HasValue && ProductionYear.Value > 0)
                return ProductionYear.Value.ToString();
            return null;
        }
    }

    public bool HasSubtitles
    {
        get
        {
            if (ApiHasSubtitles.HasValue && ApiHasSubtitles.Value) return true;
            if (MediaStreams != null && MediaStreams.Any(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (MediaSources != null && MediaSources.Any(ms =>
                (ms.MediaStreams != null && ms.MediaStreams.Any(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)))
                || (ms.DefaultSubtitleStreamIndex.HasValue && ms.DefaultSubtitleStreamIndex.Value >= 0)))
                return true;
            return false;
        }
    }

    public bool HasExternalSubtitles =>
        (MediaStreams?.Any(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase) && s.IsExternal) ?? false)
        || (MediaSources?.Any(ms => ms.MediaStreams?.Any(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase) && s.IsExternal) ?? false) ?? false);

    public string SubtitleTypeDescription =>
        HasExternalSubtitles ? "Sidecar (External)" : "Embedded";

    public string? SubtitleLanguages
    {
        get
        {
            var subs = MediaStreams?.Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase))
                ?? MediaSources?.SelectMany(ms => ms.MediaStreams ?? Array.Empty<MediaStream>()).Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase));
            if (subs == null) return null;
            var langs = subs.Select(s => s.DisplayTitle ?? s.Language).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().Take(3);
            var list = string.Join(", ", langs);
            return string.IsNullOrEmpty(list) ? null : list;
        }
    }

    public string? CharacterRole { get; set; }
    public string? CustomSeasonTitle { get; set; }

    public string SeasonDisplayName
    {
        get
        {
            if (Type != "Season") return Name;
            if (!string.IsNullOrWhiteSpace(CustomSeasonTitle))
            {
                if (IndexNumber.HasValue && IndexNumber.Value > 0)
                    return $"{IndexNumber.Value}. {CustomSeasonTitle}";
                return CustomSeasonTitle;
            }

            if (IndexNumber.HasValue)
            {
                if (IndexNumber.Value == 0)
                {
                    return string.IsNullOrWhiteSpace(Name) || Name == "0" ? "Specials" : Name;
                }

                if (string.IsNullOrWhiteSpace(Name) || Name == IndexNumber.Value.ToString() || string.Equals(Name, $"Season {IndexNumber.Value}", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Season {IndexNumber.Value}";
                }

                if (Name.StartsWith("Season", StringComparison.OrdinalIgnoreCase))
                {
                    var sepIdx = Name.IndexOfAny(new[] { ':', '-' });
                    if (sepIdx > 0 && sepIdx < Name.Length - 1)
                    {
                        var sub = Name.Substring(sepIdx + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(sub))
                            return $"{IndexNumber.Value}. {sub}";
                    }
                    return Name;
                }

                return $"{IndexNumber.Value}. {Name}";
            }
            return !string.IsNullOrWhiteSpace(Name) ? Name : "Season";
        }
    }

    public static string CleanTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var title = raw.Trim();

        // 1. If it's a file path, extract filename
        if (title.Contains('/') || title.Contains('\\'))
        {
            var lastSlash = Math.Max(title.LastIndexOf('/'), title.LastIndexOf('\\'));
            title = title.Substring(lastSlash + 1);
        }

        // 2. Strip video file extensions
        var extensions = new[] { ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".webm", ".flv" };
        foreach (var ext in extensions)
        {
            if (title.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                title = title.Substring(0, title.Length - ext.Length);
                break;
            }
        }

        // 3. Strip leading/inline S01E01 / 1x01 / E01 / S01 / E2018110000 / Show - S01E01 - Title patterns
        var sXexMatch = System.Text.RegularExpressions.Regex.Match(
            title,
            @"^(?:.*[\s\.\-_]+)?(?:S\d{1,4}[\s\.\-_]*E\d{1,10}|\d{1,2}x\d{1,4}|E\d{1,10}|S\d{1,4})[\s\.\-_:]*(.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (sXexMatch.Success && sXexMatch.Groups.Count > 1)
        {
            var extracted = sXexMatch.Groups[1].Value.Trim(' ', '-', '.', '_');
            if (!string.IsNullOrWhiteSpace(extracted))
                title = extracted;
        }

        // 4. Strip common quality/scene tags at the end
        title = System.Text.RegularExpressions.Regex.Replace(
            title,
            @"\b(2160p|1080p|720p|480p|4k|uhd|bluray|blu-ray|web-?dl|webrip|hdtv|remux|x264|x265|hevc|avc|dts|ddp?|aac|hdr\d*|sdr|proper|repack)\b.*$",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim(' ', '-', '.', '_');

        // 5. Replace dot separators or underscores with spaces
        if (title.Contains('.') && !title.Contains(' '))
            title = title.Replace('.', ' ');
        if (title.Contains('_'))
            title = title.Replace('_', ' ');

        return title.Trim(' ', '-');
    }

    public string CardTitle
    {
        get
        {
            if (Type == "Episode")
            {
                var clean = CleanTitle(Name);
                if (string.IsNullOrWhiteSpace(clean) || (IndexNumber.HasValue && string.Equals(clean, IndexNumber.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    return IndexNumber.HasValue && IndexNumber.Value < 1000 ? $"Episode {IndexNumber.Value}" : "Episode";
                }
                return clean;
            }
            if (Type == "Season")
            {
                return SeasonDisplayName;
            }
            return CleanTitle(Name);
        }
    }

    public string? CardSubtitle
    {
        get
        {
            if (!string.IsNullOrEmpty(CharacterRole))
            {
                return ProductionYear.HasValue && ProductionYear.Value > 0
                    ? $"as {CharacterRole} · {ProductionYear}"
                    : $"as {CharacterRole}";
            }
            if (Type == "Episode")
            {
                var epCode = "";
                if (ParentIndexNumber.HasValue && IndexNumber.HasValue)
                    epCode = $"S{ParentIndexNumber.Value}E{IndexNumber.Value}";
                else if (IndexNumber.HasValue)
                    epCode = $"E{IndexNumber.Value}";

                if (!string.IsNullOrEmpty(SeriesName) && !string.IsNullOrEmpty(epCode))
                    return $"{SeriesName} - {epCode}";
                if (!string.IsNullOrEmpty(SeriesName)) return SeriesName;
                if (!string.IsNullOrEmpty(epCode)) return epCode;
                return null;
            }
            if (Type == "Season")
            {
                return SeriesName;
            }
            if (IsMusic)
            {
                if (IsArtist)
                {
                    return "Artist";
                }
                var artist = AlbumArtist ?? (Artists?.Length > 0 ? string.Join(", ", Artists) : null);
                if (!string.IsNullOrEmpty(artist) && ProductionYear.HasValue && ProductionYear.Value > 0)
                    return $"{artist} · {ProductionYear.Value}";
                if (!string.IsNullOrEmpty(artist))
                    return artist;
                if (ProductionYear.HasValue && ProductionYear.Value > 0)
                    return ProductionYear.Value.ToString();
                return null;
            }
            if (IsCollection)
            {
                var count = ChildCount ?? RecursiveItemCount;
                if (count.HasValue) return count.Value == 1 ? "1 Item" : $"{count.Value} Items";
                return "Collection";
            }
            if (Type == "Series" || Type == "Movie")
            {
                if (ProductionYear.HasValue && ProductionYear.Value > 0)
                    return ProductionYear.Value.ToString();
                return null;
            }
            return null;
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (Type == "Episode")
            {
                var epName = CleanTitle(Name);
                if (string.IsNullOrWhiteSpace(epName) || (IndexNumber.HasValue && string.Equals(epName, IndexNumber.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    epName = IndexNumber.HasValue && IndexNumber.Value < 1000 ? $"Episode {IndexNumber.Value}" : "Episode";
                }

                var epCode = "";
                if (ParentIndexNumber.HasValue && IndexNumber.HasValue)
                    epCode = $"S{ParentIndexNumber.Value}E{IndexNumber.Value}";
                else if (IndexNumber.HasValue)
                    epCode = $"E{IndexNumber.Value}";

                if (!string.IsNullOrEmpty(SeriesName) && !string.IsNullOrEmpty(epCode))
                {
                    return $"{SeriesName} - {epCode} - {epName}";
                }
                if (!string.IsNullOrEmpty(SeriesName))
                {
                    return $"{SeriesName} - {epName}";
                }
                return !string.IsNullOrEmpty(epCode) ? $"{epCode} - {epName}" : epName;
            }
            if (Type == "Season")
            {
                if (!string.IsNullOrEmpty(SeriesName))
                    return $"{SeriesName} · {SeasonDisplayName}";
                return SeasonDisplayName;
            }
            return CleanTitle(Name);
        }
    }

    public bool HasProgress =>
        UserData is { PlaybackPositionTicks: > 0 } && !(UserData?.Played ?? false);
}

[DataContract]
public class ImageTags
{
    [DataMember(Name = "Primary")] public string? Primary { get; set; }
    [DataMember(Name = "Thumb")] public string? Thumb { get; set; }
    [DataMember(Name = "Logo")] public string? Logo { get; set; }
    [DataMember(Name = "Backdrop")] public string? Backdrop { get; set; }
}

[DataContract]
public class UserData
{
    [DataMember(Name = "PlaybackPositionTicks")] public long PlaybackPositionTicks { get; set; }
    [DataMember(Name = "PlayCount")] public int PlayCount { get; set; }
    [DataMember(Name = "IsFavorite")] public bool IsFavorite { get; set; }
    [DataMember(Name = "Played")] public bool Played { get; set; }
    [DataMember(Name = "Key")] public string Key { get; set; } = "";
}

[DataContract]
public class StudioItem
{
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "Id")] public string? Id { get; set; }
}

[DataContract]
public class MediaStream
{
    [DataMember(Name = "Index")] public int Index { get; set; }
    [DataMember(Name = "Type")] public string? Type { get; set; }
    [DataMember(Name = "Language")] public string? Language { get; set; }
    [DataMember(Name = "DisplayTitle")] public string? DisplayTitle { get; set; }
    [DataMember(Name = "Codec")] public string? Codec { get; set; }
    [DataMember(Name = "IsDefault")] public bool IsDefault { get; set; }
    [DataMember(Name = "Channels")] public int? Channels { get; set; }
    [DataMember(Name = "Width")] public int? Width { get; set; }
    [DataMember(Name = "Height")] public int? Height { get; set; }
    [DataMember(Name = "VideoRange")] public string? VideoRange { get; set; }
    [DataMember(Name = "IsExternal")] public bool IsExternal { get; set; }
    [DataMember(Name = "IsTextSubtitleStream")] public bool IsTextSubtitleStream { get; set; }
}

[DataContract]
public class MediaSource
{
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "TranscodingUrl")] public string? TranscodingUrl { get; set; }
    [DataMember(Name = "SupportsDirectPlay")] public bool SupportsDirectPlay { get; set; }
    [DataMember(Name = "SupportsDirectStream")] public bool SupportsDirectStream { get; set; }
    [DataMember(Name = "SupportsTranscoding")] public bool SupportsTranscoding { get; set; }
    [DataMember(Name = "DefaultAudioStreamIndex")] public int? DefaultAudioStreamIndex { get; set; }
    [DataMember(Name = "DefaultSubtitleStreamIndex")] public int? DefaultSubtitleStreamIndex { get; set; }
    [DataMember(Name = "MediaStreams")] public MediaStream[]? MediaStreams { get; set; }
}

[DataContract]
public class Person
{
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "Role")] public string? Role { get; set; }
    [DataMember(Name = "Type")] public string? Type { get; set; }
    [DataMember(Name = "PrimaryImageTag")] public string? PrimaryImageTag { get; set; }
    public string? ImageUrl { get; set; }
}

public class ActorCreditItem
{
    public string Title { get; set; } = "";
    public string? Year { get; set; }
    public string? Role { get; set; }
    public string? MediaType { get; set; }
    public bool IsInLibrary { get; set; }
    public MediaItem? MediaItem { get; set; }
}

[DataContract]
public class ChapterInfo
{
    [DataMember(Name = "StartPositionTicks")] public long StartPositionTicks { get; set; }
    [DataMember(Name = "Name")] public string? Name { get; set; }
}

public class MediaSegment
{
    public string Type { get; set; } = "";
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
}

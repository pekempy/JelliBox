using System.Runtime.Serialization;

namespace JelliBox.Models;

[DataContract]
public class PlaybackInfoResult
{
    [DataMember(Name = "MediaSources")] public MediaSource[] MediaSources { get; set; } = Array.Empty<MediaSource>();
    [DataMember(Name = "PlaySessionId")] public string PlaySessionId { get; set; } = "";
}

[DataContract]
public class AuthResult
{
    [DataMember(Name = "AccessToken")] public string AccessToken { get; set; } = "";
    [DataMember(Name = "User")] public AuthUser User { get; set; } = new AuthUser();
}

[DataContract]
public class AuthUser
{
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
}

[DataContract]
public class QuickConnectResult
{
    [DataMember(Name = "Code")] public string Code { get; set; } = "";
    [DataMember(Name = "Secret")] public string Secret { get; set; } = "";
    [DataMember(Name = "Authenticated")] public bool Authenticated { get; set; }
    [DataMember(Name = "AuthenticationToken")] public string? AuthenticationToken { get; set; }
    [DataMember(Name = "UserId")] public string? UserId { get; set; }
}

[DataContract]
public class ItemsResult<T>
{
    [DataMember(Name = "Items")] public T[] Items { get; set; } = Array.Empty<T>();
    [DataMember(Name = "TotalRecordCount")] public int TotalRecordCount { get; set; }
}

// Request body contracts
[DataContract] public class PlaybackInfoRequest { [DataMember(Name = "DeviceProfile")] public DeviceProfileDto DeviceProfile { get; set; } = new DeviceProfileDto(); }
[DataContract]
public class DeviceProfileDto
{
    [DataMember(Name = "DirectPlayProfiles")] public DirectPlayProfileDto[] DirectPlayProfiles { get; set; } = Array.Empty<DirectPlayProfileDto>();
    [DataMember(Name = "TranscodingProfiles")] public TranscodingProfileDto[] TranscodingProfiles { get; set; } = Array.Empty<TranscodingProfileDto>();
    [DataMember(Name = "SubtitleProfiles")] public SubtitleProfileDto[] SubtitleProfiles { get; set; } = new[]
    {
        new SubtitleProfileDto { Format = "srt", Method = "Encode" },
        new SubtitleProfileDto { Format = "subrip", Method = "Encode" },
        new SubtitleProfileDto { Format = "ass", Method = "Encode" },
        new SubtitleProfileDto { Format = "ssa", Method = "Encode" },
        new SubtitleProfileDto { Format = "pgs", Method = "Encode" },
        new SubtitleProfileDto { Format = "pgssub", Method = "Encode" },
        new SubtitleProfileDto { Format = "sub", Method = "Encode" },
        new SubtitleProfileDto { Format = "vtt", Method = "Encode" },
        new SubtitleProfileDto { Format = "idx", Method = "Encode" },
        new SubtitleProfileDto { Format = "dvd_subtitle", Method = "Encode" }
    };
}
[DataContract] public class SubtitleProfileDto { [DataMember(Name = "Format")] public string Format { get; set; } = ""; [DataMember(Name = "Method")] public string Method { get; set; } = "Encode"; }
[DataContract] public class DirectPlayProfileDto { [DataMember(Name = "Type")] public string Type { get; set; } = "Video"; [DataMember(Name = "Container")] public string Container { get; set; } = "mp4,mkv,avi,mov,ts"; }
[DataContract] public class TranscodingProfileDto { [DataMember(Name = "Container")] public string Container { get; set; } = "ts"; [DataMember(Name = "Type")] public string Type { get; set; } = "Video"; [DataMember(Name = "Protocol")] public string Protocol { get; set; } = "hls"; [DataMember(Name = "AudioCodec")] public string AudioCodec { get; set; } = "aac,mp3"; [DataMember(Name = "VideoCodec")] public string VideoCodec { get; set; } = "h264"; [DataMember(Name = "Context")] public string Context { get; set; } = "Streaming"; }

public record PlayerArgs(MediaItem Item, MediaSource? MediaSource, string StreamUrl, string MediaSourceId, string SessionId, long StartTicks);

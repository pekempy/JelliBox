using System;
using System.Runtime.Serialization;

namespace JelliBox.Models;

[DataContract]
public class LiveTvChannel
{
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "Number")] public string? Number { get; set; }
    [DataMember(Name = "ChannelNumber")] public string? ChannelNumber { get; set; }
    [DataMember(Name = "ChannelType")] public string? ChannelType { get; set; }
    [DataMember(Name = "ImageTags")] public ImageTags? ImageTags { get; set; }
    [DataMember(Name = "CurrentProgram")] public ProgramItem? CurrentProgram { get; set; }

    public string DisplayNumber => !string.IsNullOrEmpty(ChannelNumber) ? ChannelNumber : (Number ?? "");
    public string? LogoUrl { get; set; }
}

[DataContract]
public class ProgramItem
{
    [DataMember(Name = "Id")] public string Id { get; set; } = "";
    [DataMember(Name = "Name")] public string Name { get; set; } = "";
    [DataMember(Name = "ChannelId")] public string? ChannelId { get; set; }
    [DataMember(Name = "Overview")] public string? Overview { get; set; }
    [DataMember(Name = "StartDate")] public string? StartDate { get; set; }
    [DataMember(Name = "EndDate")] public string? EndDate { get; set; }
    [DataMember(Name = "OfficialRating")] public string? OfficialRating { get; set; }
    [DataMember(Name = "EpisodeTitle")] public string? EpisodeTitle { get; set; }
    [DataMember(Name = "IsSeries")] public bool IsSeries { get; set; }
    [DataMember(Name = "ImageTags")] public ImageTags? ImageTags { get; set; }

    public string TimeRange
    {
        get
        {
            if (DateTime.TryParse(StartDate, out var s) && DateTime.TryParse(EndDate, out var e))
            {
                return $"{s.ToLocalTime():HH:mm} – {e.ToLocalTime():HH:mm}";
            }
            return "";
        }
    }

    public double ProgressFraction
    {
        get
        {
            if (DateTime.TryParse(StartDate, out var s) && DateTime.TryParse(EndDate, out var e))
            {
                var now = DateTime.UtcNow;
                if (now < s) return 0;
                if (now > e) return 1.0;
                var total = (e - s).TotalSeconds;
                return total > 0 ? Math.Clamp((now - s).TotalSeconds / total, 0.0, 1.0) : 0;
            }
            return 0;
        }
    }
}

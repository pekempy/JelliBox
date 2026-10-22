using JelliBox.Models;
using JelliBox.Views;
using System.Linq;
using System.Threading.Tasks;

namespace JelliBox.Services;

public static class PlaybackHelper
{
    public static async Task PlayAsync(MediaItem item)
    {
        if (item == null) return;
        try
        {
            var api = App.Api;
            var target = item;
            if (item.Type == "Series")
            {
                var seasons = await api.GetItemsAsync(item.Id, limit: 100, sortBy: "SortName", recursive: false);
                var firstSeason = seasons.FirstOrDefault(s => s.IndexNumber > 0) ?? seasons.FirstOrDefault();
                if (firstSeason != null)
                {
                    var eps = await api.GetItemsAsync(firstSeason.Id, limit: 100, sortBy: "SortName", recursive: false);
                    target = eps.FirstOrDefault(e => !(e.UserData?.Played ?? false)) ?? eps.FirstOrDefault() ?? item;
                }
            }
            else if (item.Type == "Season")
            {
                var eps = await api.GetItemsAsync(item.Id, limit: 100, sortBy: "SortName", recursive: false);
                target = eps.FirstOrDefault(e => !(e.UserData?.Played ?? false)) ?? eps.FirstOrDefault() ?? item;
            }

            var info = await api.GetPlaybackInfoAsync(target.Id);
            var source = info?.MediaSources.FirstOrDefault();
            if (source == null) return;

            int? defaultSub = source.DefaultSubtitleStreamIndex;
            bool burnSub = App.Settings.BurnInSubtitles;
            var url = api.GetStreamUrl(target, source, subtitleStreamIndex: defaultSub, playSessionId: info!.PlaySessionId, burnSubtitles: burnSub);
            App.Navigation.Navigate<PlayerPage>(
                new PlayerArgs(target, source, url, source.Id, info!.PlaySessionId, target.UserData?.PlaybackPositionTicks ?? 0));
        }
        catch { /* non-fatal */ }
    }
}

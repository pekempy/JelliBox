using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace JelliBox.ViewModels;

public class LiveTvViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _isLoadingUpcoming;
    public bool IsLoadingUpcoming { get => _isLoadingUpcoming; set => Set(ref _isLoadingUpcoming, value); }

    private LiveTvChannel? _selectedChannel;
    public LiveTvChannel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (Set(ref _selectedChannel, value) && value != null)
            {
                _ = LoadUpcomingProgramsAsync(value.Id);
            }
        }
    }

    public ObservableCollection<LiveTvChannel> Channels { get; } = new();
    public ObservableCollection<ProgramItem> UpcomingPrograms { get; } = new();

    public ICommand TuneChannelCommand { get; }
    public ICommand SelectChannelCommand { get; }
    public ICommand GoBackCommand { get; }

    public LiveTvViewModel()
    {
        _api = App.Api;
        TuneChannelCommand   = new AsyncRelayCommand<LiveTvChannel>(TuneChannelAsync);
        SelectChannelCommand = new RelayCommand<LiveTvChannel>(ch => SelectedChannel = ch);
        GoBackCommand        = new RelayCommand(() => App.Navigation.GoBack());
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        Channels.Clear();
        try
        {
            var channels = await _api.GetLiveTvChannelsAsync(150, ct);
            foreach (var ch in channels)
            {
                Channels.Add(ch);
            }
            if (Channels.Count > 0)
            {
                SelectedChannel = Channels[0];
            }
        }
        catch { /* non-fatal */ }
        finally { IsLoading = false; }
    }

    private async Task LoadUpcomingProgramsAsync(string channelId)
    {
        IsLoadingUpcoming = true;
        UpcomingPrograms.Clear();
        try
        {
            var progs = await _api.GetChannelProgramsAsync(channelId, 5);
            // Skip the first program if it's currently airing
            var now = DateTime.UtcNow;
            foreach (var p in progs.Where(p => DateTime.TryParse(p.EndDate, out var end) && end > now))
            {
                UpcomingPrograms.Add(p);
            }
        }
        catch { /* non-fatal */ }
        finally { IsLoadingUpcoming = false; }
    }

    public async Task TuneChannelAsync(LiveTvChannel? channel)
    {
        channel ??= SelectedChannel;
        if (channel == null) return;

        try
        {
            var info = await _api.GetPlaybackInfoAsync(channel.Id);
            var source = info?.MediaSources.FirstOrDefault();
            if (source == null) return;

            var mediaItem = new MediaItem
            {
                Id = channel.Id,
                Name = channel.CurrentProgram?.Name ?? channel.Name,
                SeriesName = channel.Name,
                Type = "TvChannel",
                Overview = channel.CurrentProgram?.Overview
            };

            var streamUrl = _api.GetStreamUrl(mediaItem, source);

            App.Navigation.Navigate<PlayerPage>(
                new PlayerArgs(mediaItem, source, streamUrl, source.Id, info!.PlaySessionId, 0));
        }
        catch { /* non-fatal */ }
    }
}

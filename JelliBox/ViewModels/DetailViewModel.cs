using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace JelliBox.ViewModels;

public class DetailViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;

    private MediaItem? _item;
    public MediaItem? Item { get => _item; set => Set(ref _item, value); }

    private MediaItem? _parentSeries;
    public MediaItem? ParentSeries { get => _parentSeries; set => Set(ref _parentSeries, value); }

    private string? _backdropUrl;
    public string? BackdropUrl { get => _backdropUrl; set => Set(ref _backdropUrl, value); }

    private string? _logoUrl;
    public string? LogoUrl { get => _logoUrl; set => Set(ref _logoUrl, value); }

    private string? _posterUrl;
    public string? PosterUrl { get => _posterUrl; set => Set(ref _posterUrl, value); }

    private double _posterWidth = 260.0;
    public double PosterWidth { get => _posterWidth; set => Set(ref _posterWidth, value); }

    private double _posterHeight = 390.0;
    public double PosterHeight { get => _posterHeight; set => Set(ref _posterHeight, value); }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private string _headerTitle = "";
    public string HeaderTitle { get => _headerTitle; set => Set(ref _headerTitle, value); }

    private string _headerSubtitle = "";
    public string HeaderSubtitle { get => _headerSubtitle; set => Set(ref _headerSubtitle, value); }

    private string _metaText = "";
    public string MetaText { get => _metaText; set => Set(ref _metaText, value); }

    private string _genreText = "";
    public string GenreText { get => _genreText; set => Set(ref _genreText, value); }

    private string _overviewText = "";
    public string OverviewText { get => _overviewText; set => Set(ref _overviewText, value); }

    private bool _hasSubtitles;
    public bool HasSubtitles { get => _hasSubtitles; set => Set(ref _hasSubtitles, value); }

    private string _subtitleSummary = "Subtitles Available";
    public string SubtitleSummary { get => _subtitleSummary; set => Set(ref _subtitleSummary, value); }

    public ObservableCollection<Person> Cast { get; } = new();

    private bool _hasCast;
    public bool HasCast { get => _hasCast; set => Set(ref _hasCast, value); }

    public ObservableCollection<MediaItem> Seasons { get; } = new();

    private bool _hasSeasons;
    public bool HasSeasons { get => _hasSeasons; set => Set(ref _hasSeasons, value); }

    private string _seasonsSectionTitle = "Seasons";
    public string SeasonsSectionTitle { get => _seasonsSectionTitle; set => Set(ref _seasonsSectionTitle, value); }

    public ObservableCollection<MediaItem> Episodes { get; } = new();

    private bool _hasEpisodes;
    public bool HasEpisodes
    {
        get => _hasEpisodes;
        set
        {
            if (Set(ref _hasEpisodes, value))
            {
                OnPropertyChanged(nameof(IsTvEpisodeSection));
                OnPropertyChanged(nameof(IsMusicTrackSection));
            }
        }
    }

    private bool _isMusic;
    public bool IsMusic
    {
        get => _isMusic;
        set
        {
            if (Set(ref _isMusic, value))
            {
                OnPropertyChanged(nameof(IsTvEpisodeSection));
                OnPropertyChanged(nameof(IsMusicTrackSection));
            }
        }
    }

    public bool IsTvEpisodeSection => HasEpisodes && !IsMusic;
    public bool IsMusicTrackSection => HasEpisodes && IsMusic;

    private string _episodesSectionTitle = "Episodes";
    public string EpisodesSectionTitle { get => _episodesSectionTitle; set => Set(ref _episodesSectionTitle, value); }

    private bool _isSeries;
    public bool IsSeries { get => _isSeries; set => Set(ref _isSeries, value); }

    private bool _isSeason;
    public bool IsSeason { get => _isSeason; set => Set(ref _isSeason, value); }

    public ICommand PlayCommand          { get; }
    public ICommand ToggleWatchedCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand GoBackCommand        { get; }
    public ICommand OpenPersonCommand    { get; }
    public ICommand OpenItemCommand      { get; }

    public DetailViewModel()
    {
        _api = App.Api;
        PlayCommand           = new AsyncRelayCommand(PlayAsync);
        ToggleWatchedCommand  = new AsyncRelayCommand(ToggleWatchedAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);
        GoBackCommand         = new RelayCommand(() => App.Navigation.GoBack());
        OpenPersonCommand     = new RelayCommand<Person>(p => {
            if (p != null) App.Navigation.Navigate<PersonPage>(p);
        });
        OpenItemCommand       = new RelayCommand<MediaItem>(OpenItem);
    }

    private void OpenItem(MediaItem? item)
    {
        if (item == null) return;
        if (item.Type == "Audio")
            _ = PlaybackHelper.PlayAsync(item);
        else
            App.Navigation.Navigate<DetailPage>(item.Id);
    }

    public async Task LoadAsync(string itemId, CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Item = await _api.GetItemAsync(itemId, ct);
            if (Item == null) return;

            IsSeries = Item.Type == "Series";
            IsSeason = Item.Type == "Season";
            bool isEpisode = Item.Type == "Episode";
            bool isMusic = Item.IsMusic;
            bool isArtist = Item.IsArtist;
            bool isAlbum = Item.IsAlbum;
            IsMusic = isMusic;

            // If it's a Season or Episode, try fetching parent Series for backdrop/show info/cast
            ParentSeries = null;
            var seriesId = !string.IsNullOrEmpty(Item.SeriesId) ? Item.SeriesId : (IsSeries ? Item.Id : null);
            if ((IsSeason || isEpisode) && !string.IsNullOrEmpty(seriesId))
            {
                try { ParentSeries = await _api.GetItemAsync(seriesId, ct); } catch { }
            }

            // Backdrop
            var bdId = Item.BackdropImageTags?.Length > 0 ? Item.Id :
                       (Item.ParentBackdropItemId ?? ParentSeries?.Id ?? Item.Id);
            BackdropUrl = _api.GetImageUrl(bdId, "Backdrop", null, 1920);

            // Poster dimensions & URL
            if (isEpisode)
            {
                PosterWidth = 300.0;
                PosterHeight = 169.0;
                if (Item.ImageTags?.Primary != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Primary", Item.ImageTags.Primary, 600);
                else if (Item.ImageTags?.Thumb != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Thumb", Item.ImageTags.Thumb, 600);
                else if (Item.ParentBackdropItemId != null)
                    PosterUrl = _api.GetImageUrl(Item.ParentBackdropItemId, "Backdrop", null, 600);
                else
                    PosterUrl = null;
            }
            else if (isMusic)
            {
                // 1:1 Square cover art for Music
                PosterWidth = 240.0;
                PosterHeight = 240.0;
                if (Item.ImageTags?.Primary != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Primary", Item.ImageTags.Primary, 600, 600);
                else if (!string.IsNullOrEmpty(Item.AlbumId))
                    PosterUrl = _api.GetImageUrl(Item.AlbumId, "Primary", null, 600, 600);
                else if (Item.ImageTags?.Thumb != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Thumb", Item.ImageTags.Thumb, 600, 600);
                else if (!string.IsNullOrEmpty(Item.ParentId))
                    PosterUrl = _api.GetImageUrl(Item.ParentId, "Primary", null, 600, 600);
                else
                    PosterUrl = null;
            }
            else
            {
                PosterWidth = 200.0;
                PosterHeight = 300.0;
                if (Item.ImageTags?.Primary != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Primary", Item.ImageTags.Primary, 500);
                else if (ParentSeries?.ImageTags?.Primary != null)
                    PosterUrl = _api.GetImageUrl(ParentSeries.Id, "Primary", ParentSeries.ImageTags.Primary, 500);
                else if (Item.ImageTags?.Thumb != null)
                    PosterUrl = _api.GetImageUrl(Item.Id, "Thumb", Item.ImageTags.Thumb, 500);
                else
                    PosterUrl = null;
            }

            LogoUrl = (IsSeries || (!IsSeason && !isEpisode)) && Item.ImageTags?.Logo != null
                ? _api.GetImageUrl(Item.Id, "Logo", Item.ImageTags.Logo, 500)
                : null;

            // Titles
            if (IsSeason)
            {
                var showName = Item.SeriesName ?? ParentSeries?.Name ?? "";
                if (!string.IsNullOrEmpty(showName))
                {
                    var onlineSeasonNames = await _api.GetOnlineSeasonNamesAsync(showName);
                    if (Item.IndexNumber.HasValue && onlineSeasonNames.TryGetValue(Item.IndexNumber.Value, out var title))
                    {
                        Item.CustomSeasonTitle = title;
                    }
                }

                HeaderSubtitle = showName;
                HeaderTitle = Item.SeasonDisplayName;
            }
            else if (isEpisode)
            {
                var show = Item.SeriesName ?? ParentSeries?.Name ?? "";
                var epCode = "";
                if (Item.ParentIndexNumber.HasValue && Item.IndexNumber.HasValue)
                    epCode = $"S{Item.ParentIndexNumber.Value}E{Item.IndexNumber.Value}";
                else if (Item.IndexNumber.HasValue)
                    epCode = $"E{Item.IndexNumber.Value}";
                else if (Item.ParentIndexNumber.HasValue)
                    epCode = $"S{Item.ParentIndexNumber.Value}";

                if (!string.IsNullOrEmpty(show) && !string.IsNullOrEmpty(epCode))
                    HeaderSubtitle = $"{show} - {epCode}";
                else if (!string.IsNullOrEmpty(show))
                    HeaderSubtitle = show;
                else
                    HeaderSubtitle = epCode;

                HeaderTitle = MediaItem.CleanTitle(Item.Name);
            }
            else if (isArtist)
            {
                HeaderSubtitle = "Artist Profile";
                HeaderTitle = Item.Name;
            }
            else if (isAlbum)
            {
                HeaderSubtitle = Item.AlbumArtist ?? (Item.Artists?.Length > 0 ? string.Join(", ", Item.Artists) : "Music Album");
                HeaderTitle = Item.Name;
            }
            else
            {
                HeaderSubtitle = "";
                HeaderTitle = MediaItem.CleanTitle(Item.Name);
            }

            // Overview
            OverviewText = !string.IsNullOrWhiteSpace(Item.Overview)
                ? Item.Overview
                : (ParentSeries?.Overview ?? "");

            // Runtime
            var runtime = "";
            if (Item.Runtime.HasValue && !IsSeries && !IsSeason)
            {
                runtime = Item.Runtime.Value.TotalHours >= 1
                    ? $"{(int)Item.Runtime.Value.TotalHours}h {Item.Runtime.Value.Minutes}m"
                    : $"{Item.Runtime.Value.Minutes}m";
            }

            // Metadata parts (Year / First Air Date, Official Rating, Community Rating, Runtime, Network)
            var meta = new List<string>();
            var airDate = Item.FirstAirDate ?? ParentSeries?.FirstAirDate;
            if (!string.IsNullOrEmpty(airDate)) meta.Add(airDate);

            var rating = !string.IsNullOrEmpty(Item.OfficialRating) ? Item.OfficialRating : ParentSeries?.OfficialRating;
            if (!string.IsNullOrEmpty(rating)) meta.Add(rating);

            var commRating = Item.CommunityRating ?? ParentSeries?.CommunityRating;
            if (commRating.HasValue && commRating.Value > 0) meta.Add($"{commRating:F1}/10");

            if (!string.IsNullOrEmpty(runtime)) meta.Add(runtime);

            var network = Item.NetworkInfo ?? ParentSeries?.NetworkInfo;
            if (!string.IsNullOrEmpty(network)) meta.Add(network);

            MetaText = string.Join("  ·  ", meta);

            var genres = (Item.Genres?.Length > 0 ? Item.Genres : ParentSeries?.Genres) ?? Array.Empty<string>();
            GenreText = genres.Length > 0 ? string.Join("  ·  ", genres.Take(4)) : "";

            // Subtitles
            HasSubtitles = Item.HasSubtitles || (ParentSeries?.HasSubtitles ?? false);
            var subLangs = Item.SubtitleLanguages ?? ParentSeries?.SubtitleLanguages;
            var subType = Item.SubtitleTypeDescription;
            SubtitleSummary = !string.IsNullOrEmpty(subLangs) ? $"Subtitles: {subLangs} ({subType})" : $"Subtitles: {subType}";

            // Cast (Merge duplicate actors and combine multiple roles)
            Cast.Clear();
            var people = (Item.People?.Length > 0 ? Item.People : ParentSeries?.People) ?? Array.Empty<Person>();
            if (people.Length > 0)
            {
                var actors = people.Where(p => p.Type == "Actor" || string.IsNullOrEmpty(p.Type));
                var merged = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);

                foreach (var person in actors)
                {
                    var key = !string.IsNullOrEmpty(person.Id) ? person.Id : person.Name;
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    if (merged.TryGetValue(key, out var existing))
                    {
                        if (!string.IsNullOrWhiteSpace(person.Role) && !string.IsNullOrWhiteSpace(existing.Role))
                        {
                            var existingRoles = existing.Role.Split(new[] { ", ", "/" }, StringSplitOptions.RemoveEmptyEntries);
                            if (!existingRoles.Any(r => r.Trim().Equals(person.Role.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                existing.Role = $"{existing.Role}, {person.Role.Trim()}";
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(person.Role))
                        {
                            existing.Role = person.Role.Trim();
                        }

                        if (string.IsNullOrEmpty(existing.PrimaryImageTag) && !string.IsNullOrEmpty(person.PrimaryImageTag))
                        {
                            existing.PrimaryImageTag = person.PrimaryImageTag;
                            existing.ImageUrl = _api.GetImageUrl(person.Id, "Primary", person.PrimaryImageTag, 200);
                        }
                    }
                    else
                    {
                        if (person.PrimaryImageTag != null)
                            person.ImageUrl = _api.GetImageUrl(person.Id, "Primary", person.PrimaryImageTag, 200);
                        merged[key] = person;
                    }
                }

                foreach (var person in merged.Values)
                {
                    Cast.Add(person);
                }
            }
            HasCast = Cast.Count > 0;

            // Load Seasons / Albums / Episodes / Tracks
            Seasons.Clear();
            Episodes.Clear();

            if (isArtist)
            {
                SeasonsSectionTitle = "Discography & Albums";
                EpisodesSectionTitle = "Top Songs";

                var albums = await _api.GetArtistAlbumsAsync(Item.Id, ct);
                foreach (var a in albums) Seasons.Add(a);
                HasSeasons = Seasons.Count > 0;

                var songs = await _api.GetArtistSongsAsync(Item.Id, limit: 30, ct: ct);
                foreach (var s in songs) Episodes.Add(s);
                HasEpisodes = Episodes.Count > 0;
            }
            else if (isAlbum)
            {
                HasSeasons = false;
                EpisodesSectionTitle = "Album Tracklist";

                var tracks = await _api.GetItemsAsync(Item.Id, limit: 100, sortBy: "IndexNumber", sortOrder: "Ascending", recursive: false, ct: ct);
                foreach (var t in tracks) Episodes.Add(t);
                HasEpisodes = Episodes.Count > 0;
            }
            else if (IsSeries)
            {
                SeasonsSectionTitle = "Seasons";
                EpisodesSectionTitle = "Episodes";

                var seasonItems = await _api.GetItemsAsync(Item.Id, limit: 100, sortBy: "SortName", sortOrder: "Ascending", recursive: false, ct: ct);
                var onlineSeasonNames = await _api.GetOnlineSeasonNamesAsync(Item.Name);

                foreach (var s in seasonItems)
                {
                    if (string.IsNullOrEmpty(s.SeriesName)) s.SeriesName = Item.Name;
                    if (string.IsNullOrEmpty(s.SeriesId)) s.SeriesId = Item.Id;

                    if (s.IndexNumber.HasValue && onlineSeasonNames.TryGetValue(s.IndexNumber.Value, out var title))
                    {
                        s.CustomSeasonTitle = title;
                    }

                    Seasons.Add(s);
                }
                HasSeasons = Seasons.Count > 0;
            }
            else if (IsSeason)
            {
                HasSeasons = false;
                EpisodesSectionTitle = "Episodes";

                var epItems = await _api.GetItemsAsync(Item.Id, limit: 200, sortBy: "SortName", sortOrder: "Ascending", recursive: false, ct: ct);
                foreach (var ep in epItems)
                {
                    if (string.IsNullOrEmpty(ep.SeriesName)) ep.SeriesName = Item.SeriesName ?? ParentSeries?.Name;
                    Episodes.Add(ep);
                }
                HasEpisodes = Episodes.Count > 0;
            }
            else
            {
                HasSeasons = false;
                HasEpisodes = false;
            }
        }
        finally { IsLoading = false; }
    }

    private async Task PlayAsync()
    {
        if (Item == null) return;
        await PlaybackHelper.PlayAsync(Item);
    }

    private async Task ToggleWatchedAsync()
    {
        if (Item == null) return;
        await _api.ToggleWatchedAsync(Item.Id, !(Item.UserData?.Played ?? false));
        await LoadAsync(Item.Id);
    }

    private async Task ToggleFavoriteAsync()
    {
        if (Item == null) return;
        bool current = Item.UserData?.IsFavorite ?? false;
        await _api.ToggleFavoriteAsync(Item.Id, !current);
        await LoadAsync(Item.Id);
    }
}

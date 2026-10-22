using JelliBox.Controls;
using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.UI.Xaml;

namespace JelliBox.ViewModels;

public class HomeViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer? _heroTimer;
    private DispatcherTimer? _bgRefreshTimer;
    private bool _bgTimerStarted;

    private MediaItem? _heroItem;
    public MediaItem? HeroItem { get => _heroItem; set => Set(ref _heroItem, value); }

    private string? _heroBackdropUrl;
    public string? HeroBackdropUrl { get => _heroBackdropUrl; set => Set(ref _heroBackdropUrl, value); }

    private string? _heroLogoUrl;
    public string? HeroLogoUrl { get => _heroLogoUrl; set => Set(ref _heroLogoUrl, value); }

    private string? _heroMeta;
    public string? HeroMeta { get => _heroMeta; set => Set(ref _heroMeta, value); }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _showHero = true;
    public bool ShowHero { get => _showHero; set => Set(ref _showHero, value); }

    private int _heroIndex;
    public int HeroIndex { get => _heroIndex; set => Set(ref _heroIndex, value); }

    public ObservableCollection<MediaItem> Libraries { get; } = new();
    public ObservableCollection<HomeSectionRow> HomeRows { get; } = new();
    public ObservableCollection<MediaItem> HeroItems { get; } = new();
    public ObservableCollection<HeroIndicatorItem> HeroIndicators { get; } = new();

    private ObservableCollection<MediaItem> ContinueWatchingItems { get; } = new();

    public ICommand OpenItemCommand { get; }
    public ICommand OpenLibraryCommand { get; }
    public ICommand OpenSearchCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand PlayHeroCommand { get; }
    public ICommand MoreInfoHeroCommand { get; }
    public ICommand NextHeroCommand { get; }
    public ICommand PrevHeroCommand { get; }
    public ICommand SelectHeroCommand { get; }
    public ICommand RemoveFromContinueWatchingCommand { get; }

    public HomeViewModel()
    {
        _api = App.Api;
        _settings = App.Settings;
        OpenItemCommand     = new RelayCommand<MediaItem>(OpenItem);
        OpenLibraryCommand  = new RelayCommand<MediaItem>(OpenLibrary);
        OpenSearchCommand   = new RelayCommand(() => App.Navigation.Navigate<SearchPage>());
        OpenSettingsCommand = new RelayCommand(() => App.Navigation.Navigate<SettingsPage>());
        PlayHeroCommand     = new RelayCommand(PlayHero);
        MoreInfoHeroCommand = new RelayCommand(MoreInfoHero);
        NextHeroCommand     = new RelayCommand(NextHero);
        PrevHeroCommand     = new RelayCommand(PrevHero);
        SelectHeroCommand   = new RelayCommand<int>(SelectHeroIndex);
        AccountSession.Switched += OnAccountSwitched;
        RemoveFromContinueWatchingCommand = new RelayCommand<MediaItem>(async item => await RemoveFromContinueWatchingAsync(item));
        MediaCard.ItemRemovedFromContinueWatching += OnMediaCardItemRemovedFromContinueWatching;

        _heroTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _heroTimer.Tick += (_, _) => { if (HeroItems.Count > 1) NextHero(); };
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken ct = default)
    {
        ShowHero = _settings.ShowHeroBanner;

        // 1. Try stale cache — show content immediately without spinner
        bool hasStale = TryLoadFromStaleCache();

        if (hasStale)
        {
            IsLoading = false;
            _ = BackgroundRefreshAsync(ct);
        }
        else
        {
            // No cache at all — must wait for fresh data
            IsLoading = true;
            Libraries.Clear();
            HomeRows.Clear();
            ContinueWatchingItems.Clear();
            try { await LoadFreshAsync(ct); }
            finally { IsLoading = false; }
        }

        if (!_bgTimerStarted) StartBackgroundRefreshTimer();
    }

    private bool TryLoadFromStaleCache()
    {
        var cachedLibs = _api.GetLibrariesCached();
        if (cachedLibs == null || cachedLibs.Count == 0) return false;

        Libraries.Clear();
        HomeRows.Clear();
        ContinueWatchingItems.Clear();

        var ordered = ApplyLibraryOrder(cachedLibs.Where(l => l.CollectionType != null).ToList());

        foreach (var lib in ordered) Libraries.Add(lib);

        if (_settings.ShowContinueWatching)
        {
            var cachedCw = _api.GetContinueWatchingCached(10);
            if (cachedCw != null)
                foreach (var item in cachedCw) ContinueWatchingItems.Add(item);
        }

        var visible = ordered.Where(l => !_settings.IsLibraryHidden(l.Id)).ToList();
        var sectionMap = new Dictionary<string, HomeSectionRow>();
        foreach (var lib in visible)
        {
            var items = _api.GetLatestCached(lib.Id, 16);
            if (items == null || items.Count == 0) continue;
            var captured = lib;
            sectionMap[lib.Id] = new HomeSectionRow
            {
                Id = lib.Id, Title = lib.Name,
                Items = new ObservableCollection<MediaItem>(items),
                OpenLibraryCommand = new RelayCommand(() => OpenLibrary(captured))
            };
        }

        if (sectionMap.Count == 0 && ContinueWatchingItems.Count == 0) return false;

        BuildHomeRowsFromMap(sectionMap);
        return true;
    }

    private async Task LoadFreshAsync(CancellationToken ct)
    {
        var cwTask = _settings.ShowContinueWatching
            ? _api.GetContinueWatchingAsync(10, ct)
            : Task.FromResult(new List<MediaItem>());
        var libsTask = _api.GetLibrariesAsync(ct);
        await Task.WhenAll(cwTask, libsTask);

        var ordered = ApplyLibraryOrder(libsTask.Result.Where(l => l.CollectionType != null).ToList());

        foreach (var lib in ordered) Libraries.Add(lib);

        if (_settings.ShowContinueWatching)
            foreach (var item in cwTask.Result) ContinueWatchingItems.Add(item);

        var visible = ordered.Where(l => !_settings.IsLibraryHidden(l.Id)).ToList();

        var tasks = visible.Select(async lib =>
        {
            var items = await _api.GetLatestAsync(lib.Id, 16, ct);
            return (lib, items);
        }).ToList();
        var results = await Task.WhenAll(tasks);

        var sectionMap = new Dictionary<string, HomeSectionRow>();
        foreach (var (lib, items) in results.OrderBy(r => visible.IndexOf(r.lib)))
        {
            if (items.Count == 0) continue;
            var captured = lib;
            sectionMap[lib.Id] = new HomeSectionRow
            {
                Id = lib.Id, Title = lib.Name,
                Items = new ObservableCollection<MediaItem>(items),
                OpenLibraryCommand = new RelayCommand(() => OpenLibrary(captured))
            };
        }

        BuildHomeRowsFromMap(sectionMap);

        if (ShowHero) await LoadHeroCarouselAsync(visible, ct);
        else { HeroItem = null; HeroItems.Clear(); HeroIndicators.Clear(); try { _heroTimer?.Stop(); } catch { } }
    }

    private void BuildHomeRowsFromMap(Dictionary<string, HomeSectionRow> sectionMap)
    {
        var order = _settings.GetHomeSectionOrder();
        if (order.Count > 0)
        {
            foreach (var id in order)
            {
                if (id == "continue_watching")
                {
                    if (_settings.ShowContinueWatching && ContinueWatchingItems.Count > 0)
                        HomeRows.Add(MakeCwRow());
                }
                else if (sectionMap.TryGetValue(id, out var row))
                {
                    HomeRows.Add(row);
                    sectionMap.Remove(id);
                }
            }
            foreach (var row in sectionMap.Values) HomeRows.Add(row);
        }
        else
        {
            if (_settings.ShowContinueWatching && ContinueWatchingItems.Count > 0)
                HomeRows.Add(MakeCwRow());
            foreach (var row in sectionMap.Values) HomeRows.Add(row);
        }
    }

    private HomeSectionRow MakeCwRow() => new()
    {
        Id = "continue_watching",
        Title = "Continue Watching",
        IsContinueWatching = true,
        Items = ContinueWatchingItems
    };

    // ── Background refresh ────────────────────────────────────────────────────

    private void StartBackgroundRefreshTimer()
    {
        _bgTimerStarted = true;
        _bgRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _bgRefreshTimer.Tick += async (_, _) =>
        {
            if (HomeRows.Count == 0) return;
            await BackgroundRefreshAsync(CancellationToken.None);
        };
        _bgRefreshTimer.Start();
    }

    private async Task BackgroundRefreshAsync(CancellationToken ct)
    {
        try
        {
            var freshLibs = await _api.GetLibrariesAsync(forceRefresh: true, ct);
            var ordered   = ApplyLibraryOrder(freshLibs.Where(l => l.CollectionType != null).ToList());
            var visible   = ordered.Where(l => !_settings.IsLibraryHidden(l.Id)).ToList();

            var tasks = visible.Select(async lib =>
            {
                var items = await _api.GetLatestAsync(lib.Id, 16, forceRefresh: true, ct);
                return (lib, items);
            }).ToList();
            var results = await Task.WhenAll(tasks);

            await DispatchAsync(() =>
            {
                foreach (var (lib, fresh) in results)
                {
                    if (fresh.Count == 0) continue;
                    var existing = HomeRows.FirstOrDefault(r => !r.IsContinueWatching && r.Id == lib.Id);
                    if (existing != null)
                        PatchSectionRow(existing, fresh);
                    else
                    {
                        var captured = lib;
                        HomeRows.Add(new HomeSectionRow
                        {
                            Id = lib.Id, Title = lib.Name,
                            Items = new ObservableCollection<MediaItem>(fresh),
                            OpenLibraryCommand = new RelayCommand(() => OpenLibrary(captured))
                        });
                    }
                }
            });

            if (_settings.ShowContinueWatching)
            {
                var freshCw = await _api.GetContinueWatchingAsync(10, forceRefresh: true, ct);
                await DispatchAsync(() => PatchContinueWatching(freshCw));
            }

            if (ShowHero && HeroItems.Count == 0)
                await LoadHeroCarouselAsync(visible, ct);
        }
        catch { /* non-fatal */ }
    }

    private static void PatchSectionRow(HomeSectionRow row, List<MediaItem> fresh)
    {
        var existingIds = row.Items.Select(i => i.Id).ToHashSet();
        // Insert new items at front, preserving fresh order
        var toInsert = fresh.Where(i => !existingIds.Contains(i.Id)).ToList();
        for (int i = toInsert.Count - 1; i >= 0; i--)
            row.Items.Insert(0, toInsert[i]);
    }

    private void PatchContinueWatching(List<MediaItem> fresh)
    {
        var freshSet = fresh.Select(i => i.Id).ToHashSet();

        // Remove items no longer in-progress
        foreach (var item in ContinueWatchingItems.Where(i => !freshSet.Contains(i.Id)).ToList())
            ContinueWatchingItems.Remove(item);

        // Insert newly in-progress items at front
        var existingSet = ContinueWatchingItems.Select(i => i.Id).ToHashSet();
        var toAdd = fresh.Where(i => !existingSet.Contains(i.Id)).ToList();
        for (int i = toAdd.Count - 1; i >= 0; i--)
            ContinueWatchingItems.Insert(0, toAdd[i]);

        // Keep CW row in HomeRows in sync
        var cwRow = HomeRows.FirstOrDefault(r => r.IsContinueWatching);
        if (ContinueWatchingItems.Count > 0 && cwRow == null)
            HomeRows.Insert(0, MakeCwRow());
        else if (ContinueWatchingItems.Count == 0 && cwRow != null)
            HomeRows.Remove(cwRow);
    }

    // ── Quick refresh (on back-nav) ───────────────────────────────────────────

    public async Task RefreshQuickAsync(CancellationToken ct = default)
    {
        ShowHero = _settings.ShowHeroBanner;
        if (!ShowHero) HeroItem = null;

        if (_settings.ShowContinueWatching)
        {
            try
            {
                var fresh = await _api.GetContinueWatchingAsync(10, forceRefresh: true, ct);
                PatchContinueWatching(fresh);
            }
            catch { }
        }
        else
        {
            ContinueWatchingItems.Clear();
            var cwRow = HomeRows.FirstOrDefault(r => r.IsContinueWatching);
            if (cwRow != null) HomeRows.Remove(cwRow);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<MediaItem> ApplyLibraryOrder(List<MediaItem> list)
    {
        var customOrder = _settings.GetLibraryOrder();
        if (customOrder.Count == 0) return list;
        return list.OrderBy(lib =>
        {
            var idx = customOrder.IndexOf(lib.Id);
            return idx >= 0 ? idx : 999;
        }).ToList();
    }

    private void OpenLibrary(MediaItem? lib)
    {
        if (lib == null) return;
        if (lib.CollectionType == "livetv" || string.Equals(lib.Name, "Live TV", StringComparison.OrdinalIgnoreCase))
            App.Navigation.Navigate<LiveTvPage>();
        else
            App.Navigation.Navigate<LibraryPage>(new LibraryArgs(lib.Id, lib.Name, lib.CollectionType));
    }

    private void SetHero(MediaItem? item)
    {
        if (item == null) return;
        HeroItem = item;
        AmbientService.SetItem(item);
        var bdId = item.BackdropImageTags?.Length > 0 ? item.Id : item.ParentBackdropItemId ?? item.Id;
        HeroBackdropUrl = _api.GetImageUrl(bdId, "Backdrop", null, 1920);
        HeroLogoUrl = item.ImageTags?.Logo != null
            ? _api.GetImageUrl(item.Id, "Logo", item.ImageTags.Logo, 400) : null;

        var parts = new List<string>();
        if (item.ProductionYear.HasValue)     parts.Add(item.ProductionYear.Value.ToString());
        if (!string.IsNullOrEmpty(item.OfficialRating)) parts.Add(item.OfficialRating);
        if (item.CommunityRating.HasValue)    parts.Add($"{item.CommunityRating:F1}/10");
        if (item.Runtime.HasValue)            parts.Add($"{(int)item.Runtime.Value.TotalHours}h {item.Runtime.Value.Minutes}m");
        HeroMeta = string.Join("  ·  ", parts);
    }

    private void OpenItem(MediaItem? item)
    {
        if (item == null) return;
        if (item.Type is "Movie" or "Episode" or "Series" or "Season" or "MusicAlbum" or "MusicArtist" or "Artist")
            App.Navigation.Navigate<DetailPage>(item.Id);
        else if (item.Type == "Audio")
            _ = PlaybackHelper.PlayAsync(item);
        else
            App.Navigation.Navigate<LibraryPage>(new LibraryArgs(item.Id, item.Name, item.CollectionType));
    }

    private void OnMediaCardItemRemovedFromContinueWatching(object? sender, MediaItem item)
    {
        var match = ContinueWatchingItems.FirstOrDefault(i => i.Id == item.Id);
        if (match != null)
        {
            ContinueWatchingItems.Remove(match);
            if (ContinueWatchingItems.Count == 0)
            {
                var row = HomeRows.FirstOrDefault(r => r.IsContinueWatching);
                if (row != null) HomeRows.Remove(row);
            }
            if (HeroItem?.Id == item.Id)
                SetHero(ContinueWatchingItems.FirstOrDefault(i => i.BackdropImageTags?.Length > 0)
                    ?? HomeRows.Where(r => !r.IsContinueWatching).SelectMany(s => s.Items)
                               .FirstOrDefault(i => i.BackdropImageTags?.Length > 0));
        }
    }

    private async Task RemoveFromContinueWatchingAsync(MediaItem? item)
    {
        if (item == null) return;
        var match = ContinueWatchingItems.FirstOrDefault(i => i.Id == item.Id) ?? item;
        ContinueWatchingItems.Remove(match);
        if (ContinueWatchingItems.Count == 0)
        {
            var row = HomeRows.FirstOrDefault(r => r.IsContinueWatching);
            if (row != null) HomeRows.Remove(row);
        }
        if (HeroItem?.Id == item.Id)
            SetHero(ContinueWatchingItems.FirstOrDefault(i => i.BackdropImageTags?.Length > 0)
                ?? HomeRows.Where(r => !r.IsContinueWatching).SelectMany(s => s.Items)
                           .FirstOrDefault(i => i.BackdropImageTags?.Length > 0));
        await _api.RemoveFromContinueWatchingAsync(item.Id);
    }

    // ── Hero carousel ─────────────────────────────────────────────────────────

    private async Task LoadHeroCarouselAsync(List<MediaItem> visibleLibraries, CancellationToken ct)
    {
        HeroItems.Clear();
        HeroIndicators.Clear();

        var heroLibs = visibleLibraries.Where(lib => _settings.IsLibraryIncludedInHero(lib.Id)).ToList();
        if (heroLibs.Count == 0) heroLibs = visibleLibraries;

        var candidates = new List<MediaItem>();
        try
        {
            var recentTasks = heroLibs.Select(lib => _api.GetLatestAsync(lib.Id, 8, ct)).ToList();
            var randomTasks = heroLibs.Select(lib => _api.GetItemsAsync(lib.Id, limit: 8, sortBy: "Random", ct: ct)).ToList();
            await Task.WhenAll(recentTasks.Concat(randomTasks));

            var allRecent = recentTasks.SelectMany(t => t.Result).Where(i => i.BackdropImageTags?.Length > 0).ToList();
            var allRandom = randomTasks.SelectMany(t => t.Result).Where(i => i.BackdropImageTags?.Length > 0).ToList();

            var seen = new HashSet<string>();
            int ri = 0, rdi = 0;
            while (candidates.Count < 10 && (ri < allRecent.Count || rdi < allRandom.Count))
            {
                if (ri < allRecent.Count)  { var it = allRecent[ri++];  if (seen.Add(it.Id)) candidates.Add(it); }
                if (candidates.Count >= 10) break;
                if (rdi < allRandom.Count) { var it = allRandom[rdi++]; if (seen.Add(it.Id)) candidates.Add(it); }
            }
        }
        catch { }

        if (candidates.Count == 0)
            candidates.AddRange(HomeRows.Where(r => !r.IsContinueWatching).SelectMany(s => s.Items)
                .Where(i => i.BackdropImageTags?.Length > 0).Take(6));

        foreach (var item in candidates) HeroItems.Add(item);
        for (int i = 0; i < HeroItems.Count; i++)
            HeroIndicators.Add(new HeroIndicatorItem { Index = i, IsActive = i == 0 });

        if (HeroItems.Count > 0) { SelectHeroIndex(0); try { _heroTimer?.Start(); } catch { } }
        else HeroItem = null;
    }

    public void SelectHeroIndex(int index)
    {
        if (HeroItems.Count == 0) return;
        if (index < 0) index = HeroItems.Count - 1;
        if (index >= HeroItems.Count) index = 0;
        HeroIndex = index;
        SetHero(HeroItems[index]);
        for (int i = 0; i < HeroIndicators.Count; i++) HeroIndicators[i].IsActive = i == index;
        ResetHeroTimer();
    }

    public void NextHero() { if (HeroItems.Count > 1) SelectHeroIndex((HeroIndex + 1) % HeroItems.Count); }
    public void PrevHero() { if (HeroItems.Count > 1) SelectHeroIndex((HeroIndex - 1 + HeroItems.Count) % HeroItems.Count); }

    private void ResetHeroTimer() { try { _heroTimer?.Stop(); _heroTimer?.Start(); } catch { } }

    private async void PlayHero()   { if (HeroItem != null) await PlaybackHelper.PlayAsync(HeroItem); }
    private void MoreInfoHero()     { if (HeroItem != null) App.Navigation.Navigate<DetailPage>(HeroItem.Id); }

    // The signed-in user changed (from the shared top bar): drop the old user's content and load the new one's.
    private async void OnAccountSwitched()
    {
        _bgRefreshTimer?.Stop();
        _bgTimerStarted = false;
        Libraries.Clear();
        HomeRows.Clear();
        ContinueWatchingItems.Clear();
        HeroItems.Clear();
        HeroItem = null;

        // Elsewhere the bar navigates Home afterwards, and arriving here loads it.
        if (App.Navigation.CurrentPageType == typeof(Views.HomePage)) await LoadAsync();
    }

    private static System.Threading.Tasks.Task DispatchAsync(Action action)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher
            .RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
        return tcs.Task;
    }
}

public class HomeSectionRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsContinueWatching { get; set; }
    public ObservableCollection<MediaItem> Items { get; set; } = new();
    public ICommand? OpenLibraryCommand { get; set; }
}

// Alias kept for any external x:DataType references
public class MediaSection : HomeSectionRow { }

public class HeroIndicatorItem : ObservableBase
{
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (Set(ref _isActive, value))
            {
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Opacity));
            }
        }
    }

    public double Width => _isActive ? 24.0 : 8.0;
    public double Opacity => _isActive ? 1.0 : 0.35;
    public int Index { get; set; }
}

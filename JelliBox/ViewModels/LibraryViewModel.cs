using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace JelliBox.ViewModels;

public class LibraryViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;
    private string _parentId = "";
    private int _startIndex;
    private int PageSize => App.Settings.PageSize > 0 ? App.Settings.PageSize : 40;

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _showTitle = true;
    /// <summary>Show the title only when this isn't one of the top-level libraries (a collection or folder).</summary>
    public bool ShowTitle { get => _showTitle; set => Set(ref _showTitle, value); }

    private bool _hasMore;
    public bool HasMore { get => _hasMore; set => Set(ref _hasMore, value); }

    // Index (within the current query) of the first loaded item; > 0 after jumping to a letter.
    private int _windowStart;
    private string? _pendingJumpLetter;

    private bool _hasMoreAbove;
    public bool HasMoreAbove { get => _hasMoreAbove; set => Set(ref _hasMoreAbove, value); }

    /// <summary>Raised when the view should bring this item into view and focus it (letter jump).</summary>
    public event Action<MediaItem>? ScrollToItemRequested;

    // Letters only mean "position in the alphabet" when sorted A-Z; other sorts fall back to filtering.
    private bool CanJump => SortBy == "SortName" && SortOrder == "Ascending" && !HasLanguageFilter;

    private bool _isEpisodeView;
    public bool IsEpisodeView { get => _isEpisodeView; set => Set(ref _isEpisodeView, value); }

    private bool _isMusicView;
    public bool IsMusicView { get => _isMusicView; set => Set(ref _isMusicView, value); }

    private bool _isPosterView = true;
    public bool IsPosterView { get => _isPosterView; set => Set(ref _isPosterView, value); }

    private string _sortBy = "SortName";
    public string SortBy { get => _sortBy; set => Set(ref _sortBy, value); }
    private string _sortOrder = "Ascending";
    public string SortOrder { get => _sortOrder; set => Set(ref _sortOrder, value); }

    // ── Sort & filter ────────────────────────────────────────────────────────
    // Changing several of these at once (Reset) must reload only once.
    private bool _suppressRequery;

    public ObservableCollection<SortChoice> SortOptions { get; } = new()
    {
        new SortChoice("Title", "SortName", defaultAscending: true),
        new SortChoice("Year", "ProductionYear,SortName", defaultAscending: false),
        new SortChoice("Duration", "Runtime,SortName", defaultAscending: false),
        new SortChoice("Rating", "CommunityRating,SortName", defaultAscending: false),
        new SortChoice("Date added", "DateCreated,SortName", defaultAscending: false),
        new SortChoice("Random", "Random", defaultAscending: true),
    };

    private SortChoice? _selectedSort;
    public SortChoice? SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value == null || !Set(ref _selectedSort, value)) return;
            SortBy = value.Key;
            _isAscending = value.DefaultAscending;
            SortOrder = _isAscending ? "Ascending" : "Descending";
            OnPropertyChanged(nameof(IsAscending));
            OnPropertyChanged(nameof(IsDescending));
            OnPropertyChanged(nameof(CanChooseOrder));
            OnFiltersChanged();
        }
    }

    private bool _isAscending = true;
    public bool IsAscending => _isAscending;
    public bool IsDescending => !_isAscending;
    /// <summary>Random has no direction.</summary>
    public bool CanChooseOrder => SelectedSort is not { IsRandom: true };

    private void SetAscending(bool ascending)
    {
        if (_isAscending == ascending) return;
        _isAscending = ascending;
        SortOrder = ascending ? "Ascending" : "Descending";
        OnPropertyChanged(nameof(IsAscending));
        OnPropertyChanged(nameof(IsDescending));
        OnFiltersChanged();
    }

    // Watched state: any, only played, or only unplayed.
    private string _playedFilter = "Any";
    public string PlayedFilter => _playedFilter;
    public bool IsPlayedAny => _playedFilter == "Any";
    public bool IsPlayedOnly => _playedFilter == "Played";
    public bool IsUnplayedOnly => _playedFilter == "Unplayed";

    private void SetPlayedFilter(string? value)
    {
        value = value is "Played" or "Unplayed" ? value : "Any";
        if (_playedFilter == value) return;
        _playedFilter = value;
        OnPropertyChanged(nameof(PlayedFilter));
        OnPropertyChanged(nameof(IsPlayedAny));
        OnPropertyChanged(nameof(IsPlayedOnly));
        OnPropertyChanged(nameof(IsUnplayedOnly));
        OnFiltersChanged();
    }

    public ObservableCollection<NamedOption> GenreOptions { get; } = new() { new NamedOption("Any genre", "") };

    private NamedOption? _selectedGenre;
    public NamedOption? SelectedGenre
    {
        get => _selectedGenre;
        set
        {
            if (value == null || !Set(ref _selectedGenre, value)) return;
            OnFiltersChanged();
        }
    }

    // Audio / subtitle language: the server can't filter on these, so they are applied to what it returns.
    public ObservableCollection<NamedOption> AudioLanguageOptions { get; } = LanguageChoices("Any audio language");
    public ObservableCollection<NamedOption> SubtitleLanguageOptions { get; } = LanguageChoices("Any subtitle language");

    private NamedOption? _selectedAudioLanguage;
    public NamedOption? SelectedAudioLanguage
    {
        get => _selectedAudioLanguage;
        set
        {
            if (value == null || !Set(ref _selectedAudioLanguage, value)) return;
            OnFiltersChanged();
        }
    }

    private NamedOption? _selectedSubtitleLanguage;
    public NamedOption? SelectedSubtitleLanguage
    {
        get => _selectedSubtitleLanguage;
        set
        {
            if (value == null || !Set(ref _selectedSubtitleLanguage, value)) return;
            OnFiltersChanged();
        }
    }

    private bool HasLanguageFilter =>
        !string.IsNullOrEmpty(_selectedAudioLanguage?.Value) || !string.IsNullOrEmpty(_selectedSubtitleLanguage?.Value);

    // ISO 639 codes (the 3-letter forms Jellyfin reports, plus the 2-letter ones some files use).
    private static ObservableCollection<NamedOption> LanguageChoices(string anyLabel) => new()
    {
        new NamedOption(anyLabel, ""),
        new NamedOption("English", "eng,en"),
        new NamedOption("French", "fre,fra,fr"),
        new NamedOption("German", "ger,deu,de"),
        new NamedOption("Spanish", "spa,es"),
        new NamedOption("Italian", "ita,it"),
        new NamedOption("Portuguese", "por,pt"),
        new NamedOption("Dutch", "dut,nld,nl"),
        new NamedOption("Swedish", "swe,sv"),
        new NamedOption("Danish", "dan,da"),
        new NamedOption("Norwegian", "nor,nob,no"),
        new NamedOption("Finnish", "fin,fi"),
        new NamedOption("Polish", "pol,pl"),
        new NamedOption("Russian", "rus,ru"),
        new NamedOption("Turkish", "tur,tr"),
        new NamedOption("Arabic", "ara,ar"),
        new NamedOption("Hindi", "hin,hi"),
        new NamedOption("Japanese", "jpn,ja"),
        new NamedOption("Korean", "kor,ko"),
        new NamedOption("Chinese", "chi,zho,zh"),
    };

    private static HashSet<string>? LanguageCodes(NamedOption? option)
        => string.IsNullOrEmpty(option?.Value) ? null : new HashSet<string>(option!.Value.Split(','));

    private static bool IsPlayableVideo(MediaItem item) => item.Type is "Movie" or "Episode" or "Video" or "MusicVideo";

    private bool MatchesLanguages(MediaItem item)
    {
        var audio = LanguageCodes(_selectedAudioLanguage);
        var subtitle = LanguageCodes(_selectedSubtitleLanguage);
        if (audio == null && subtitle == null) return true;
        if (!IsPlayableVideo(item)) return true; // series, collections and folders have no streams to inspect

        var streams = item.MediaStreams ?? Array.Empty<MediaStream>();
        if (audio != null && !streams.Any(s => s.Type == "Audio" && audio.Contains((s.Language ?? "").ToLowerInvariant())))
            return false;
        if (subtitle != null && !streams.Any(s => s.Type == "Subtitle" && subtitle.Contains((s.Language ?? "").ToLowerInvariant())))
            return false;
        return true;
    }

    private string? ServerFilters => _playedFilter switch { "Played" => "IsPlayed", "Unplayed" => "IsUnplayed", _ => null };
    private string? ServerGenres => string.IsNullOrEmpty(_selectedGenre?.Value) ? null : _selectedGenre!.Value;

    public int ActiveFilterCount =>
        (_playedFilter != "Any" ? 1 : 0)
        + (SubtitlesOnly ? 1 : 0)
        + (ServerGenres != null ? 1 : 0)
        + (!string.IsNullOrEmpty(_selectedAudioLanguage?.Value) ? 1 : 0)
        + (!string.IsNullOrEmpty(_selectedSubtitleLanguage?.Value) ? 1 : 0);

    private void OnFiltersChanged()
    {
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(ActiveFilterCount));
        if (!_suppressRequery) _ = RequeryAsync();
    }

    /// <summary>Start the list again from the top with the current sort and filters.</summary>
    private async Task RequeryAsync()
    {
        _continuousCts?.Cancel();
        SelectedLetter = null;
        ResetWindow();
        Items.Clear();
        await LoadPageAsync();
    }

    private void ResetFilters()
    {
        _suppressRequery = true;
        try
        {
            SelectedSort = SortOptions[0];
            SetPlayedFilter("Any");
            if (SubtitlesOnly) SubtitlesOnly = false;
            SelectedGenre = GenreOptions[0];
            SelectedAudioLanguage = AudioLanguageOptions[0];
            SelectedSubtitleLanguage = SubtitleLanguageOptions[0];
        }
        finally { _suppressRequery = false; }
        OnFiltersChanged();
    }

    private async Task LoadGenresAsync()
    {
        try
        {
            var genres = await _api.GetGenresAsync(_parentId);
            _suppressRequery = true;
            try
            {
                var keep = _selectedGenre?.Value;
                GenreOptions.Clear();
                GenreOptions.Add(new NamedOption("Any genre", ""));
                foreach (var genre in genres) GenreOptions.Add(new NamedOption(genre, genre));
                SelectedGenre = GenreOptions.FirstOrDefault(o => o.Value == keep) ?? GenreOptions[0];
            }
            finally { _suppressRequery = false; }
        }
        catch { /* the genre list is optional */ }
    }

    private string? _selectedLetter;
    public string? SelectedLetter { get => _selectedLetter; set => Set(ref _selectedLetter, value); }

    private bool _showAll;
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (Set(ref _showAll, value))
            {
                OnPropertyChanged(nameof(ShowAllLabel));
                OnPropertyChanged(nameof(IsPagedMode));
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public bool IsPagedMode => !ShowAll;

    public string ShowAllLabel => ShowAll ? "PAGED VIEW" : "SHOW ALL";

    private bool _subtitlesOnly;
    public bool SubtitlesOnly
    {
        get => _subtitlesOnly;
        set
        {
            if (Set(ref _subtitlesOnly, value))
            {
                OnPropertyChanged(nameof(SubtitlesFilterLabel));
                OnPropertyChanged(nameof(IsAllSubtitles));
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public string SubtitlesFilterLabel => SubtitlesOnly ? "CC ONLY" : "CC ALL";
    public bool IsAllSubtitles => !SubtitlesOnly;

    private bool _isFilterMenuOpen;
    public bool IsFilterMenuOpen { get => _isFilterMenuOpen; set => Set(ref _isFilterMenuOpen, value); }

    /// <summary>Short description of the active sort and filters, shown on the Sort &amp; Filter button.</summary>
    public string FilterSummary
    {
        get
        {
            var sort = SelectedSort?.Label ?? "Title";
            var direction = SelectedSort is { IsRandom: true } ? "" : IsAscending ? " ↑" : " ↓";
            var count = ActiveFilterCount;
            return sort + direction + (ShowAll ? " · All" : "") + (count > 0 ? $" · {count} filter{(count == 1 ? "" : "s")}" : "");
        }
    }

    public ObservableCollection<string> Letters { get; } = new()
    {
        "ALL", "#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
        "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"
    };

    public ObservableCollection<MediaItem> Items { get; } = new();

    public ICommand LoadMoreCommand              { get; }
    public ICommand LoadMoreAboveCommand         { get; }
    public ICommand OpenItemCommand              { get; }
    public ICommand GoBackCommand                { get; }
    public ICommand SortAscendingCommand         { get; }
    public ICommand SortDescendingCommand        { get; }
    public ICommand SetPlayedFilterCommand       { get; }
    public ICommand ResetFiltersCommand          { get; }
    public ICommand SelectLetterCommand          { get; }
    public ICommand ToggleShowAllCommand         { get; }
    public ICommand ToggleSubtitlesFilterCommand { get; }
    public ICommand ToggleFilterMenuCommand      { get; }
    public ICommand ShowPagedCommand             { get; }
    public ICommand ShowAllCommand               { get; }
    public ICommand SubtitlesAllCommand          { get; }
    public ICommand SubtitlesOnlyCommand         { get; }

    public LibraryViewModel()
    {
        _api = App.Api;
        LoadMoreCommand              = new AsyncRelayCommand(() => LoadPageAsync());
        LoadMoreAboveCommand         = new AsyncRelayCommand(LoadMoreAboveAsync);
        OpenItemCommand              = new RelayCommand<MediaItem>(OpenItem);
        GoBackCommand                = new RelayCommand(() => App.Navigation.GoBack());
        SortAscendingCommand         = new RelayCommand(() => SetAscending(true));
        SortDescendingCommand        = new RelayCommand(() => SetAscending(false));
        SetPlayedFilterCommand       = new RelayCommand<string>(SetPlayedFilter);
        ResetFiltersCommand          = new RelayCommand(ResetFilters);
        _suppressRequery = true;
        SelectedSort = SortOptions[0];
        SelectedGenre = GenreOptions[0];
        SelectedAudioLanguage = AudioLanguageOptions[0];
        SelectedSubtitleLanguage = SubtitleLanguageOptions[0];
        _suppressRequery = false;
        SelectLetterCommand          = new AsyncRelayCommand<string>(SelectLetterAsync);
        ToggleShowAllCommand         = new AsyncRelayCommand(ToggleShowAllAsync);
        ToggleSubtitlesFilterCommand = new AsyncRelayCommand(ToggleSubtitlesFilterAsync);
        ToggleFilterMenuCommand      = new RelayCommand(() => IsFilterMenuOpen = !IsFilterMenuOpen);
        ShowPagedCommand             = new AsyncRelayCommand(() => ShowAll ? ToggleShowAllAsync() : Task.CompletedTask);
        ShowAllCommand               = new AsyncRelayCommand(() => ShowAll ? Task.CompletedTask : ToggleShowAllAsync());
        SubtitlesAllCommand          = new AsyncRelayCommand(() => SubtitlesOnly ? ToggleSubtitlesFilterAsync() : Task.CompletedTask);
        SubtitlesOnlyCommand         = new AsyncRelayCommand(() => SubtitlesOnly ? Task.CompletedTask : ToggleSubtitlesFilterAsync());
    }

    private CancellationTokenSource? _continuousCts;

    private void ResetWindow()
    {
        _windowStart = 0;
        _startIndex = 0;
        _pendingJumpLetter = null;
        HasMoreAbove = false;
    }

    private static string LetterOf(MediaItem item)
    {
        var key = (string.IsNullOrEmpty(item.SortName) ? item.Name : item.SortName).TrimStart();
        if (key.Length == 0) return "#";
        var c = char.ToUpperInvariant(key[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }

    // First loaded item at or after the letter (a missing letter lands on the next one that exists).
    private MediaItem? FindLoaded(string? letter)
    {
        if (letter == null || letter == "#") return Items.FirstOrDefault();
        return Items.FirstOrDefault(i =>
        {
            var l = LetterOf(i);
            return l != "#" && string.CompareOrdinal(l, letter) >= 0;
        });
    }

    private async Task SelectLetterAsync(string? letter)
    {
        var clear = string.IsNullOrEmpty(letter) || letter == "ALL" || (!CanJump && letter == SelectedLetter);
        SelectedLetter = clear ? null : letter;

        if (!CanJump)
        {
            // Date / rating sorts: a letter filters the list instead.
            _continuousCts?.Cancel();
            ResetWindow();
            Items.Clear();
            await LoadPageAsync();
            return;
        }

        var target = clear ? null : letter;

        if (ShowAll)
        {
            // Everything is (being) loaded: scroll to the letter, no reload and no paging buttons.
            if (FindLoaded(target) is { } loaded)
                ScrollToItemRequested?.Invoke(loaded);
            else if (target != null)
                _pendingJumpLetter = target; // still streaming in; jump as soon as it arrives
            return;
        }

        _continuousCts?.Cancel();
        var offset = 0;
        if (target != null && target != "#")
        {
            try { offset = await _api.GetItemCountAsync(_parentId, target, SubtitlesOnly ? true : null, ServerGenres, ServerFilters); }
            catch { offset = 0; }
        }

        Items.Clear();
        ResetWindow();
        _windowStart = offset;
        _startIndex = offset;
        HasMoreAbove = offset > 0;
        await LoadPageAsync();
        if (Items.Count > 0) ScrollToItemRequested?.Invoke(Items[0]);
    }

    private async Task LoadMoreAboveAsync()
    {
        if (_windowStart <= 0)
        {
            HasMoreAbove = false;
            return;
        }

        var newStart = Math.Max(0, _windowStart - PageSize);
        var count = _windowStart - newStart;
        try
        {
            var items = await _api.GetItemsAsync(
                _parentId, count, newStart, SortBy, SortOrder,
                recursive: false,
                hasSubtitles: SubtitlesOnly ? true : null,
                genres: ServerGenres, filters: ServerFilters);

            for (var i = items.Count - 1; i >= 0; i--) Items.Insert(0, items[i]);
            _windowStart = newStart;
            HasMoreAbove = _windowStart > 0;
        }
        catch { /* non-fatal */ }
    }

    private async Task ToggleShowAllAsync()
    {
        _continuousCts?.Cancel();
        ShowAll = !ShowAll;
        SelectedLetter = null;
        ResetWindow();
        Items.Clear();
        await LoadPageAsync();
    }

    private async Task ToggleSubtitlesFilterAsync()
    {
        _continuousCts?.Cancel();
        SubtitlesOnly = !SubtitlesOnly;
        SelectedLetter = null;
        ResetWindow();
        Items.Clear();
        await LoadPageAsync();
    }

    private string? _collectionType;

    private void UpdateViewMode(IReadOnlyList<MediaItem> items, string? collectionType = null)
    {
        if (collectionType == "music")
        {
            IsMusicView = true;
            IsEpisodeView = false;
            IsPosterView = false;
            return;
        }

        if (items.Count > 0)
        {
            if (items.Any(i => i.Type == "Episode"))
            {
                IsEpisodeView = true;
                IsMusicView = false;
                IsPosterView = false;
            }
            else if (items.Any(i => i.IsMusic))
            {
                IsMusicView = true;
                IsEpisodeView = false;
                IsPosterView = false;
            }
            else
            {
                IsPosterView = true;
                IsEpisodeView = false;
                IsMusicView = false;
            }
        }
    }

    private async Task UpdateTitleVisibilityAsync()
    {
        try
        {
            var libraries = App.Api.GetLibrariesCached() ?? await _api.GetLibrariesAsync();
            ShowTitle = !libraries.Any(l => l.CollectionType != null && l.Id == _parentId);
        }
        catch { /* the title simply stays visible */ }
    }

    public async Task LoadAsync(LibraryArgs args, CancellationToken ct = default)
    {
        _continuousCts?.Cancel();
        _parentId = args.ParentId;
        Title = args.Title;
        _startIndex = 0;
        _collectionType = args.CollectionType;
        _ = UpdateTitleVisibilityAsync();
        _ = LoadGenresAsync();
        UpdateViewMode(Array.Empty<MediaItem>(), _collectionType);
        SelectedLetter = null;
        ResetWindow();
        ShowAll = App.Settings.PageSize == 0;
        Items.Clear();

        // Try stale cache — show content instantly, refresh in background
        var cached = App.Api.GetItemsCached(_parentId, PageSize, SortBy, SortOrder);
        if (cached != null && cached.Count > 0)
        {
            UpdateViewMode(cached, _collectionType);
            foreach (var item in cached) Items.Add(item);
            _startIndex = cached.Count;
            HasMore = cached.Count >= PageSize;
            IsLoading = false;
            _ = BackgroundRefreshLibraryAsync(ct);
            return;
        }

        // No cache — normal blocking load
        await LoadPageAsync(ct);
    }

    private async Task BackgroundRefreshLibraryAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _api.GetItemsAsync(
                _parentId, PageSize, 0, SortBy, SortOrder,
                recursive: false, ct: ct);

            if (ct.IsCancellationRequested || fresh.Count == 0) return;

            var existingIds = Items.Select(i => i.Id).ToHashSet();
            var toInsert = fresh.Where(i => !existingIds.Contains(i.Id)).ToList();

            if (toInsert.Count > 0)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher
                    .RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        if (ct.IsCancellationRequested) return;
                        // New items at front (recently added sort order)
                        for (int i = toInsert.Count - 1; i >= 0; i--)
                            Items.Insert(0, toInsert[i]);
                        HasMore = fresh.Count >= PageSize;
                    });
            }
        }
        catch { /* non-fatal */ }
    }

    // The server applies sort, genre, played and subtitle filters; language filters are applied here, so keep reading
    // until a page fills (or the library ends) rather than showing a sparse page.
    private async Task<(List<MediaItem> Items, int NextIndex, bool Exhausted)> FetchAsync(
        int startIndex, int batchSize, string? startsWith, string? lessThan, CancellationToken token)
    {
        var collected = new List<MediaItem>();
        var index = startIndex;
        var exhausted = false;

        for (var guard = 0; guard < 40; guard++)
        {
            var raw = await _api.GetItemsAsync(
                _parentId, batchSize, index, SortBy, SortOrder,
                recursive: false,
                nameStartsWith: startsWith,
                nameLessThan: lessThan,
                hasSubtitles: SubtitlesOnly ? true : null,
                genres: ServerGenres,
                filters: ServerFilters,
                ct: token);

            index += raw.Count;
            exhausted = raw.Count < batchSize;
            collected.AddRange(HasLanguageFilter ? raw.Where(MatchesLanguages) : raw);

            if (token.IsCancellationRequested || exhausted || !HasLanguageFilter || collected.Count >= batchSize) break;
        }

        return (collected, index, exhausted);
    }

    private async Task LoadPageAsync(CancellationToken ct = default)
    {
        _continuousCts?.Cancel();
        _continuousCts?.Dispose();
        _continuousCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _continuousCts.Token;

        IsLoading = true;
        try
        {
            bool fetchAll = ShowAll || App.Settings.PageSize == 0;
            int batchSize = fetchAll ? 60 : PageSize;

            string? startsWith = null;
            string? lessThan = null;
            if (!CanJump)
            {
                if (SelectedLetter == "#")
                    lessThan = "A";
                else if (!string.IsNullOrEmpty(SelectedLetter) && SelectedLetter != "ALL")
                    startsWith = SelectedLetter;
            }

            var first = await FetchAsync(_startIndex, batchSize, startsWith, lessThan, token);
            if (token.IsCancellationRequested) return;

            if (first.Items.Count > 0)
            {
                UpdateViewMode(first.Items, _collectionType);
            }
            foreach (var item in first.Items) Items.Add(item);
            _startIndex = first.NextIndex;
            HasMore = !fetchAll && !first.Exhausted;

            // Make posters visible immediately
            IsLoading = false;

            // If fetchAll is enabled and more items remain, stream next chunks in background
            if (fetchAll && !first.Exhausted)
            {
                var nextIndex = first.NextIndex;
                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var chunk = await FetchAsync(nextIndex, batchSize, startsWith, lessThan, token);
                            if (token.IsCancellationRequested) break;
                            nextIndex = chunk.NextIndex;

                            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                                {
                                    if (token.IsCancellationRequested) return;
                                    foreach (var it in chunk.Items) Items.Add(it);
                                    _startIndex = chunk.NextIndex;
                                    if (_pendingJumpLetter != null && FindLoaded(_pendingJumpLetter) is { } hit)
                                    {
                                        _pendingJumpLetter = null;
                                        ScrollToItemRequested?.Invoke(hit);
                                    }
                                });

                            if (chunk.Exhausted) break;
                        }
                        catch
                        {
                            break;
                        }
                    }
                }, token);
            }
        }
        catch
        {
            // non-fatal
        }
        finally
        {
            IsLoading = false;
        }
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
}

public record LibraryArgs(string ParentId, string Title, string? CollectionType = null);

/// <summary>One way to order a library (Title, Year, ...), with the direction that suits it by default.</summary>
public sealed class SortChoice
{
    public string Label { get; }
    public string Key { get; }
    public bool DefaultAscending { get; }
    public bool IsRandom => Key == "Random";

    public SortChoice(string label, string key, bool defaultAscending)
    {
        Label = label;
        Key = key;
        DefaultAscending = defaultAscending;
    }
}

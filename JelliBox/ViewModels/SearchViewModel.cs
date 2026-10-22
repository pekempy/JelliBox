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

public class SearchViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;
    private CancellationTokenSource? _searchCts;
    private System.Threading.Timer? _debounceTimer;

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                OnSearchQueryChanged();
            }
        }
    }

    private string _selectedType = "All";
    public string SelectedType
    {
        get => _selectedType;
        set
        {
            if (Set(ref _selectedType, value))
            {
                _ = ExecuteSearchAsync();
            }
        }
    }

    private string _selectedGenre = "All Genres";
    public string SelectedGenre
    {
        get => _selectedGenre;
        set
        {
            if (Set(ref _selectedGenre, value))
            {
                _ = ExecuteSearchAsync();
            }
        }
    }

    private string _selectedYear = "All Years";
    public string SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (Set(ref _selectedYear, value))
            {
                _ = ExecuteSearchAsync();
            }
        }
    }

    private string _selectedSort = "Rating";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (Set(ref _selectedSort, value))
            {
                _ = ExecuteSearchAsync();
            }
        }
    }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _hasMediaResults;
    public bool HasMediaResults { get => _hasMediaResults; set => Set(ref _hasMediaResults, value); }

    private bool _hasPeopleResults;
    public bool HasPeopleResults { get => _hasPeopleResults; set => Set(ref _hasPeopleResults, value); }

    private bool _isMusicView;
    public bool IsMusicView { get => _isMusicView; set => Set(ref _isMusicView, value); }

    public ObservableCollection<string> Types { get; } = new() { "All", "Movies", "TV Shows", "Music", "People" };
    public ObservableCollection<string> Genres { get; } = new() { "All Genres" };
    public ObservableCollection<string> Years { get; } = new(
        new[] { "All Years" }.Concat(
            Enumerable.Range(2010, DateTime.Now.Year - 2010 + 1)
                .OrderByDescending(y => y)
                .Select(y => y.ToString())
        )
    );
    public ObservableCollection<string> SortOptions { get; } = new() { "Rating", "Name (A-Z)", "Recent", "Release Year" };

    public ObservableCollection<MediaItem> MediaResults { get; } = new();
    public ObservableCollection<Person> PeopleResults { get; } = new();

    public ICommand OpenItemCommand { get; }
    public ICommand OpenPersonCommand { get; }
    public ICommand GoBackCommand { get; }

    public SearchViewModel()
    {
        _api = App.Api;
        OpenItemCommand   = new RelayCommand<MediaItem>(OpenItem);
        OpenPersonCommand = new RelayCommand<Person>(OpenPerson);
        GoBackCommand     = new RelayCommand(() => App.Navigation.GoBack());

        _ = LoadGenresAsync();
    }

    private async Task LoadGenresAsync()
    {
        try
        {
            var genres = await _api.GetGenresAsync();
            foreach (var g in genres.Take(30))
            {
                if (!Genres.Contains(g)) Genres.Add(g);
            }
        }
        catch { }
    }

    private void OnSearchQueryChanged()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ =>
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher
                .RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => _ = ExecuteSearchAsync());
        }, null, 300, Timeout.Infinite);
    }

    public async Task ExecuteSearchAsync()
    {
        var query = SearchText?.Trim() ?? "";
        if (query.Length == 0 && SelectedGenre == "All Genres" && SelectedYear == "All Years")
        {
            MediaResults.Clear();
            PeopleResults.Clear();
            HasMediaResults = false;
            HasPeopleResults = false;
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsLoading = true;
        try
        {
            var itemTypes = SelectedType switch
            {
                "Movies" => "Movie",
                "TV Shows" => "Series",
                "Music" => "MusicAlbum,MusicArtist,Audio",
                "People" => "Person",
                _ => "Movie,Series,Episode,MusicAlbum,MusicArtist"
            };

            var genreFilter = SelectedGenre == "All Genres" ? null : SelectedGenre;
            var yearFilter  = SelectedYear == "All Years" ? null : SelectedYear;

            var (sortBy, sortOrder) = SelectedSort switch
            {
                "Name (A-Z)" => ("SortName", "Ascending"),
                "Recent" => ("DateCreated", "Descending"),
                "Release Year" => ("PremiereDate", "Descending"),
                _ => ("CommunityRating", "Descending")
            };

            Task<List<MediaItem>>? mediaTask = null;
            Task<List<Person>>? peopleTask = null;

            if (SelectedType != "People")
            {
                mediaTask = _api.SearchMediaAsync(query, itemTypes, genreFilter, yearFilter, sortBy, sortOrder, ct);
            }
            if ((SelectedType == "All" || SelectedType == "People") && query.Length >= 2 && genreFilter == null && yearFilter == null)
            {
                peopleTask = _api.SearchPeopleAsync(query, ct);
            }

            if (mediaTask != null) await mediaTask;
            if (peopleTask != null) await peopleTask;

            MediaResults.Clear();
            if (mediaTask != null && mediaTask.Result.Count > 0)
            {
                foreach (var item in mediaTask.Result)
                    MediaResults.Add(item);
            }
            HasMediaResults = MediaResults.Count > 0;
            IsMusicView = SelectedType == "Music" || (MediaResults.Count > 0 && MediaResults.All(i => i.IsMusic));

            PeopleResults.Clear();
            if (peopleTask != null && peopleTask.Result.Count > 0)
            {
                foreach (var p in peopleTask.Result)
                    PeopleResults.Add(p);
            }
            HasPeopleResults = PeopleResults.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch { /* non-fatal */ }
        finally { IsLoading = false; }
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

    private void OpenPerson(Person? person)
    {
        if (person == null) return;
        App.Navigation.Navigate<PersonPage>(person);
    }
}

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

public class PersonViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;

    private Person? _person;
    public Person? Person { get => _person; set => Set(ref _person, value); }

    private string? _photoUrl;
    public string? PhotoUrl { get => _photoUrl; set => Set(ref _photoUrl, value); }

    private string? _bio;
    public string? Bio { get => _bio; set => Set(ref _bio, value); }

    private string? _description;
    public string? Description { get => _description; set => Set(ref _description, value); }

    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _hasFilmography;
    public bool HasFilmography { get => _hasFilmography; set => Set(ref _hasFilmography, value); }

    public ObservableCollection<MediaItem> LibraryItems { get; } = new();
    public ObservableCollection<ActorCreditItem> Filmography { get; } = new();

    public ICommand OpenMediaItemCommand { get; }
    public ICommand GoBackCommand { get; }

    public PersonViewModel()
    {
        _api = App.Api;
        OpenMediaItemCommand = new RelayCommand<MediaItem>(OpenMediaItem);
        GoBackCommand        = new RelayCommand(() => App.Navigation.GoBack());
    }

    public async Task LoadAsync(Person person, CancellationToken ct = default)
    {
        IsLoading = true;
        Person = person;
        PhotoUrl = person.ImageUrl;
        LibraryItems.Clear();
        Filmography.Clear();

        try
        {
            var libraryItemsTask = _api.GetPersonLibraryItemsAsync(person.Name, ct);
            var onlineInfoTask   = _api.GetOnlinePersonBioAsync(person.Name);

            await Task.WhenAll(libraryItemsTask, onlineInfoTask);

            foreach (var item in libraryItemsTask.Result)
            {
                // Extract character role for this person from item.People
                var matched = item.People?.FirstOrDefault(p => string.Equals(p.Name, person.Name, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(matched?.Role))
                {
                    item.CharacterRole = matched.Role;
                }
                LibraryItems.Add(item);
            }

            // If any item is missing its character role, try fetching full item details
            foreach (var item in LibraryItems.Where(i => string.IsNullOrEmpty(i.CharacterRole)).Take(5))
            {
                try
                {
                    var full = await _api.GetItemAsync(item.Id, ct);
                    var matched = full?.People?.FirstOrDefault(p => string.Equals(p.Name, person.Name, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(matched?.Role))
                    {
                        item.CharacterRole = matched.Role;
                    }
                }
                catch { }
            }

            var (bio, desc, img, wikiId) = onlineInfoTask.Result;
            Bio = bio;
            Description = desc;
            if (string.IsNullOrEmpty(PhotoUrl) && !string.IsNullOrEmpty(img))
            {
                PhotoUrl = img;
            }

            // Filmography cross-reference
            if (!string.IsNullOrEmpty(wikiId))
            {
                var films = await _api.GetWikidataFilmographyAsync(wikiId);
                foreach (var (title, year, role, mType) in films)
                {
                    var match = LibraryItems.FirstOrDefault(
                        i => string.Equals(i.Name, title, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(i.SeriesName, title, StringComparison.OrdinalIgnoreCase));

                    var effectiveRole = role;
                    var effectiveType = mType;

                    if (match != null)
                    {
                        effectiveType = match.Type == "Series" ? "TV Show" : "Movie";
                        if (!string.IsNullOrEmpty(match.CharacterRole))
                        {
                            effectiveRole = match.CharacterRole;
                        }
                    }

                    Filmography.Add(new ActorCreditItem
                    {
                        Title = title,
                        Year = year,
                        Role = effectiveRole,
                        MediaType = effectiveType,
                        IsInLibrary = match != null,
                        MediaItem = match
                    });
                }
                HasFilmography = Filmography.Count > 0;
            }
        }
        catch { /* non-fatal */ }
        finally { IsLoading = false; }
    }

    private void OpenMediaItem(MediaItem? item)
    {
        if (item == null) return;
        if (item.Type is "Movie" or "Episode" or "Series" or "Season")
            App.Navigation.Navigate<DetailPage>(item.Id);
        else
            App.Navigation.Navigate<LibraryPage>(new LibraryArgs(item.Id, item.Name));
    }
}

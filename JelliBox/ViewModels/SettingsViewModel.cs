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

public class SettingsViewModel : ObservableBase
{
    private readonly SettingsService _settings;
    private readonly JellyfinApiService _api;

    public string ServerUrl => _settings.ServerUrl ?? "Not connected";
    public string Username => _settings.Username ?? "Unknown";

    // ── Categories & Navigation ─────────────────────────────────────────────
    public ObservableCollection<SettingsCategoryItem> Categories { get; } = new()
    {
        new SettingsCategoryItem { Index = 0, Title = "Accounts & Profiles", Subtitle = "Users & server connection", Icon = "\uE77B", IsSelected = true },
        new SettingsCategoryItem { Index = 1, Title = "Playback & Streaming", Subtitle = "Bitrate, direct play & auto-skip", Icon = "\uE768" },
        new SettingsCategoryItem { Index = 2, Title = "Subtitles & Audio", Subtitle = "Subtitle engine, sizing & audio tracks", Icon = "\uE7F0" },
        new SettingsCategoryItem { Index = 3, Title = "Interface & Display", Subtitle = "Home spotlight, continue watching & page size", Icon = "\uEB9F" },
        new SettingsCategoryItem { Index = 4, Title = "Libraries & Ordering", Subtitle = "Reorder and hide home sections", Icon = "\uE8B7" },
        new SettingsCategoryItem { Index = 5, Title = "Diagnostics & About", Subtitle = "Cache flush, latency & system info", Icon = "\uE946" }
    };

    private int _selectedCategoryIndex = 0;
    public int SelectedCategoryIndex
    {
        get => _selectedCategoryIndex;
        set
        {
            if (Set(ref _selectedCategoryIndex, value))
            {
                foreach (var c in Categories) c.IsSelected = c.Index == value;
                OnPropertyChanged(nameof(IsAccountCategory));
                OnPropertyChanged(nameof(IsPlaybackCategory));
                OnPropertyChanged(nameof(IsSubtitlesCategory));
                OnPropertyChanged(nameof(IsDisplayCategory));
                OnPropertyChanged(nameof(IsLibrariesCategory));
                OnPropertyChanged(nameof(IsSystemCategory));
                OnPropertyChanged(nameof(ActiveCategoryTitle));
                OnPropertyChanged(nameof(ActiveCategorySubtitle));
            }
        }
    }

    public bool IsAccountCategory => SelectedCategoryIndex == 0;
    public bool IsPlaybackCategory => SelectedCategoryIndex == 1;
    public bool IsSubtitlesCategory => SelectedCategoryIndex == 2;
    public bool IsDisplayCategory => SelectedCategoryIndex == 3;
    public bool IsLibrariesCategory => SelectedCategoryIndex == 4;
    public bool IsSystemCategory => SelectedCategoryIndex == 5;

    public string ActiveCategoryTitle => Categories.FirstOrDefault(c => c.Index == SelectedCategoryIndex)?.Title ?? "Settings";
    public string ActiveCategorySubtitle => Categories.FirstOrDefault(c => c.Index == SelectedCategoryIndex)?.Subtitle ?? "";

    // ── 1. Playback & Automation ─────────────────────────────────────────────
    public bool AutoSkipIntro
    {
        get => _settings.AutoSkipIntro;
        set
        {
            _settings.AutoSkipIntro = value;
            OnPropertyChanged();
        }
    }

    public bool AutoSkipOutro
    {
        get => _settings.AutoSkipOutro;
        set
        {
            _settings.AutoSkipOutro = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<BitrateOption> BitrateOptions { get; } = new()
    {
        new BitrateOption("Direct Play (Maximum Quality)", 0),
        new BitrateOption("4K UHD — 60 Mbps", 60000000),
        new BitrateOption("1080p — 20 Mbps (Recommended)", 20000000),
        new BitrateOption("720p — 8 Mbps", 8000000),
        new BitrateOption("480p — 3 Mbps", 3000000)
    };

    private BitrateOption? _selectedBitrate;
    public BitrateOption? SelectedBitrate
    {
        get => _selectedBitrate;
        set
        {
            if (Set(ref _selectedBitrate, value) && value != null)
            {
                _settings.MaxStreamingBitrate = value.Bitrate;
            }
        }
    }

    // ── 2. Subtitles & Audio ─────────────────────────────────────────────────
    public ObservableCollection<SubtitleDeliveryOption> SubtitleDeliveryOptions { get; } = new()
    {
        new SubtitleDeliveryOption("Direct Play Native WebVTT (Zero Transcode - Fast)", false),
        new SubtitleDeliveryOption("Force Server Burn-in Transcode (Burn into video)", true)
    };

    private SubtitleDeliveryOption? _selectedSubtitleDelivery;
    public SubtitleDeliveryOption? SelectedSubtitleDelivery
    {
        get => _selectedSubtitleDelivery;
        set
        {
            if (Set(ref _selectedSubtitleDelivery, value) && value != null)
            {
                _settings.BurnInSubtitles = value.BurnIn;
            }
        }
    }

    public ObservableCollection<NamedOption> SubtitleFontSizes { get; } = new()
    {
        new NamedOption("Standard (100%)", "Normal"),
        new NamedOption("Large (125%)", "Large"),
        new NamedOption("Extra Large (150%)", "ExtraLarge")
    };

    private NamedOption? _selectedSubtitleFontSize;
    public NamedOption? SelectedSubtitleFontSize
    {
        get => _selectedSubtitleFontSize;
        set
        {
            if (Set(ref _selectedSubtitleFontSize, value) && value != null)
            {
                _settings.SubtitleFontSize = value.Value;
            }
        }
    }

    public ObservableCollection<NamedOption> PreferredSubtitleLanguages { get; } = new()
    {
        new NamedOption("Default (Server Default)", "Default"),
        new NamedOption("English", "English"),
        new NamedOption("Spanish (Español)", "Spanish"),
        new NamedOption("French (Français)", "French"),
        new NamedOption("German (Deutsch)", "German"),
        new NamedOption("Italian (Italiano)", "Italian"),
        new NamedOption("Japanese (日本語)", "Japanese"),
        new NamedOption("None (Off by default)", "None")
    };

    private NamedOption? _selectedPreferredSubtitleLanguage;
    public NamedOption? SelectedPreferredSubtitleLanguage
    {
        get => _selectedPreferredSubtitleLanguage;
        set
        {
            if (Set(ref _selectedPreferredSubtitleLanguage, value) && value != null)
            {
                _settings.PreferredSubtitleLanguage = value.Value;
            }
        }
    }

    public ObservableCollection<NamedOption> PreferredAudioLanguages { get; } = new()
    {
        new NamedOption("Default (Original Audio Track)", "Default"),
        new NamedOption("English", "English"),
        new NamedOption("Spanish (Español)", "Spanish"),
        new NamedOption("French (Français)", "French"),
        new NamedOption("German (Deutsch)", "German"),
        new NamedOption("Japanese (日本語)", "Japanese")
    };

    private NamedOption? _selectedPreferredAudioLanguage;
    public NamedOption? SelectedPreferredAudioLanguage
    {
        get => _selectedPreferredAudioLanguage;
        set
        {
            if (Set(ref _selectedPreferredAudioLanguage, value) && value != null)
            {
                _settings.PreferredAudioLanguage = value.Value;
            }
        }
    }

    // ── 3. Interface & Display ───────────────────────────────────────────────
    public bool ShowHeroBanner
    {
        get => _settings.ShowHeroBanner;
        set
        {
            _settings.ShowHeroBanner = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AccentSwatch> AccentSwatches { get; } = new();
    public ICommand SelectAccentCommand { get; }

    public bool AmbientBackground
    {
        get => _settings.AmbientBackground;
        set
        {
            _settings.AmbientBackground = value;
            OnPropertyChanged();
            AmbientService.Refresh();
        }
    }

    private void SelectAccent(AccentSwatch? swatch)
    {
        if (swatch == null) return;
        _settings.AccentColorHex = swatch.Hex;
        AccentService.Apply(swatch.Hex);
        foreach (var other in AccentSwatches) other.IsSelected = ReferenceEquals(other, swatch);
    }

    public bool ShowContinueWatching
    {
        get => _settings.ShowContinueWatching;
        set
        {
            _settings.ShowContinueWatching = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<PageSizeOption> PageSizeOptions { get; } = new()
    {
        new PageSizeOption("24 items", 24),
        new PageSizeOption("36 items", 36),
        new PageSizeOption("48 items", 48),
        new PageSizeOption("60 items", 60),
        new PageSizeOption("80 items", 80),
        new PageSizeOption("100 items", 100),
        new PageSizeOption("All Items (One Page)", 0)
    };

    private PageSizeOption? _selectedPageSizeOption;
    public PageSizeOption? SelectedPageSizeOption
    {
        get => _selectedPageSizeOption;
        set
        {
            if (Set(ref _selectedPageSizeOption, value) && value != null)
            {
                _settings.PageSize = value.Size;
            }
        }
    }

    public ObservableCollection<NamedOption> EpisodeTitleStyles { get; } = new()
    {
        new NamedOption("Compact (e.g. 1. Murder House / S1E3)", "Compact"),
        new NamedOption("Full (e.g. American Horror Story Season 1, Episode 3)", "Verbose")
    };

    private NamedOption? _selectedEpisodeTitleStyle;
    public NamedOption? SelectedEpisodeTitleStyle
    {
        get => _selectedEpisodeTitleStyle;
        set
        {
            if (Set(ref _selectedEpisodeTitleStyle, value) && value != null)
            {
                _settings.EpisodeTitleStyle = value.Value;
            }
        }
    }

    // ── 4. Libraries & Multi-User ────────────────────────────────────────────
    public ObservableCollection<LibraryOrderItem> Libraries { get; } = new();
    public ObservableCollection<SavedAccount> SavedAccounts { get; } = new();

    // ── 5. Diagnostics & System ──────────────────────────────────────────────
    private string _cacheStatus = "";
    public string CacheStatus { get => _cacheStatus; set => Set(ref _cacheStatus, value); }

    private string _diagnosticsPingStatus = "Click 'Test Connection' to check round-trip server latency";
    public string DiagnosticsPingStatus { get => _diagnosticsPingStatus; set => Set(ref _diagnosticsPingStatus, value); }

    private bool _isTestingConnection;
    public bool IsTestingConnection { get => _isTestingConnection; set => Set(ref _isTestingConnection, value); }

    // ── Commands ─────────────────────────────────────────────────────────────
    public ICommand GoBackCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand RequestSignOutCommand { get; }
    public ICommand CancelSignOutCommand { get; }

    private bool _isSignOutConfirmOpen;
    public bool IsSignOutConfirmOpen { get => _isSignOutConfirmOpen; set => Set(ref _isSignOutConfirmOpen, value); }
    public ICommand SwitchAccountCommand { get; }
    public ICommand AddAccountCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand MoveLibraryUpCommand { get; }
    public ICommand MoveLibraryDownCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public SettingsViewModel()
    {
        _settings = App.Settings;
        _api = App.Api;

        var currentAccent = _settings.AccentColorHex;
        foreach (var (name, hex) in AccentService.Presets)
        {
            AccentSwatches.Add(new AccentSwatch(name, hex)
            {
                IsSelected = string.Equals(hex, currentAccent, StringComparison.OrdinalIgnoreCase),
            });
        }
        SelectAccentCommand = new RelayCommand<AccentSwatch>(SelectAccent);

        int currentSize = _settings.PageSize;
        _selectedPageSizeOption = PageSizeOptions.FirstOrDefault(p => p.Size == currentSize)
                                  ?? PageSizeOptions.FirstOrDefault(p => p.Size == 48)
                                  ?? PageSizeOptions.First();

        _selectedBitrate = BitrateOptions.FirstOrDefault(b => b.Bitrate == _settings.MaxStreamingBitrate)
                           ?? BitrateOptions[2];

        _selectedSubtitleDelivery = SubtitleDeliveryOptions.FirstOrDefault(s => s.BurnIn == _settings.BurnInSubtitles)
                                    ?? SubtitleDeliveryOptions[0];

        _selectedSubtitleFontSize = SubtitleFontSizes.FirstOrDefault(s => s.Value == _settings.SubtitleFontSize)
                                    ?? SubtitleFontSizes[0];

        _selectedPreferredSubtitleLanguage = PreferredSubtitleLanguages.FirstOrDefault(s => s.Value == _settings.PreferredSubtitleLanguage)
                                            ?? PreferredSubtitleLanguages[0];

        _selectedPreferredAudioLanguage = PreferredAudioLanguages.FirstOrDefault(s => s.Value == _settings.PreferredAudioLanguage)
                                          ?? PreferredAudioLanguages[0];

        _selectedEpisodeTitleStyle = EpisodeTitleStyles.FirstOrDefault(s => s.Value == _settings.EpisodeTitleStyle)
                                     ?? EpisodeTitleStyles[0];

        GoBackCommand          = new RelayCommand(() => App.Navigation.GoBack());
        SelectCategoryCommand  = new RelayCommand<int>(idx => SelectedCategoryIndex = idx);
        SignOutCommand         = new RelayCommand(() => { IsSignOutConfirmOpen = false; SignOut(); });
        RequestSignOutCommand  = new RelayCommand(() => IsSignOutConfirmOpen = true);
        CancelSignOutCommand   = new RelayCommand(() => IsSignOutConfirmOpen = false);
        SwitchAccountCommand   = new RelayCommand<SavedAccount>(SwitchAccount);
        AddAccountCommand      = new RelayCommand(AddAccount);
        RemoveAccountCommand   = new RelayCommand<SavedAccount>(RemoveAccount);
        MoveLibraryUpCommand   = new RelayCommand<LibraryOrderItem>(MoveLibraryUp);
        MoveLibraryDownCommand = new RelayCommand<LibraryOrderItem>(MoveLibraryDown);
        ClearCacheCommand      = new RelayCommand(ClearCache);
        TestConnectionCommand  = new RelayCommand(TestConnection);
    }

    public void NextCategory()
    {
        int next = SelectedCategoryIndex + 1;
        if (next >= Categories.Count) next = 0;
        SelectedCategoryIndex = next;
    }

    public void PreviousCategory()
    {
        int prev = SelectedCategoryIndex - 1;
        if (prev < 0) prev = Categories.Count - 1;
        SelectedCategoryIndex = prev;
    }

    private void ClearCache()
    {
        _api.InvalidateCache();
        CacheStatus = "Cache cleared successfully!";
    }

    private async void TestConnection()
    {
        if (IsTestingConnection) return;
        IsTestingConnection = true;
        DiagnosticsPingStatus = "Testing connection to Jellyfin server...";
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var libs = await _api.GetLibrariesAsync(forceRefresh: true);
            sw.Stop();
            DiagnosticsPingStatus = $"Connected: {sw.ElapsedMilliseconds}ms round-trip latency ({libs.Count} libraries verified)";
        }
        catch (Exception ex)
        {
            DiagnosticsPingStatus = $"Ping error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        RefreshAccounts();
        Libraries.Clear();
        try
        {
            var libs = await _api.GetLibrariesAsync(forceRefresh: false, ct);
            var customOrder = _settings.GetHomeSectionOrder();
            var allIds = new List<string> { "continue_watching" }
                .Concat(libs.Where(l => l.CollectionType != null).Select(l => l.Id))
                .ToList();

            // Sort by custom order
            var orderedFinal = allIds.OrderBy(id => {
                var idx = customOrder.IndexOf(id);
                return idx >= 0 ? idx : 999;
            }).ToList();

            foreach (var id in orderedFinal)
            {
                if (id == "continue_watching")
                {
                    Libraries.Add(new LibraryOrderItem("continue_watching", "Continue Watching", true, false,
                        OnLibraryVisibilityChanged, OnHeroLibraryToggled) { IsSpecial = true });
                }
                else
                {
                    var lib = libs.FirstOrDefault(l => l.Id == id && l.CollectionType != null);
                    if (lib != null)
                    {
                        var isHidden = _settings.IsLibraryHidden(lib.Id);
                        var isHeroIncluded = _settings.IsLibraryIncludedInHero(lib.Id);
                        Libraries.Add(new LibraryOrderItem(lib.Id, lib.Name, !isHidden, isHeroIncluded,
                            OnLibraryVisibilityChanged, OnHeroLibraryToggled));
                    }
                }
            }
        }
        catch { /* non-fatal */ }
    }

    private void RefreshAccounts()
    {
        _settings.SaveCurrentAccount();
        SavedAccounts.Clear();
        foreach (var acc in _settings.GetSavedAccounts())
        {
            SavedAccounts.Add(acc);
        }
    }

    private void SwitchAccount(SavedAccount? account)
    {
        if (account == null || account.UserId == _settings.UserId) return;

        _api.InvalidateCache();
        _settings.UserId    = account.UserId;
        _settings.Username  = account.Username;
        _settings.ServerUrl = account.ServerUrl;
        _settings.Token     = account.Token;
        _api.RefreshAuthHeader();

        App.Navigation.Navigate<HomePage>();
        App.Navigation.ClearBackStack();
    }

    private void AddAccount()
    {
        App.Navigation.Navigate<LoginPage>();
    }

    private void RemoveAccount(SavedAccount? account)
    {
        if (account == null) return;
        _settings.RemoveAccount(account.UserId);
        SavedAccounts.Remove(account);
    }

    private void MoveLibraryUp(LibraryOrderItem? item)
    {
        if (item == null) return;
        var idx = Libraries.IndexOf(item);
        if (idx > 0)
        {
            Libraries.Move(idx, idx - 1);
            SaveLibraryOrder();
        }
    }

    private void MoveLibraryDown(LibraryOrderItem? item)
    {
        if (item == null) return;
        var idx = Libraries.IndexOf(item);
        if (idx >= 0 && idx < Libraries.Count - 1)
        {
            Libraries.Move(idx, idx + 1);
            SaveLibraryOrder();
        }
    }

    private void SaveLibraryOrder()
    {
        var allIds = Libraries.Select(l => l.Id);
        _settings.SetHomeSectionOrder(allIds);
        _settings.SetLibraryOrder(Libraries.Where(l => !l.IsSpecial).Select(l => l.Id));
    }

    private void OnLibraryVisibilityChanged(string libraryId, bool isVisible)
    {
        _settings.SetLibraryHidden(libraryId, !isVisible);
    }

    private void OnHeroLibraryToggled(string libraryId, bool isIncluded)
    {
        _settings.SetLibraryIncludedInHero(libraryId, isIncluded);
    }

    private void SignOut()
    {
        _api.InvalidateCache();
        _settings.ClearCredentials();
        App.Navigation.Navigate<LoginPage>();
        App.Navigation.ClearBackStack();
    }
}

public class SettingsCategoryItem : ObservableBase
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public class NamedOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    public NamedOption() { }
    public NamedOption(string label, string value)
    {
        Label = label;
        Value = value;
    }
}

public class SubtitleDeliveryOption
{
    public string Label { get; set; } = "";
    public bool BurnIn { get; set; }

    public SubtitleDeliveryOption() { }
    public SubtitleDeliveryOption(string label, bool burnIn)
    {
        Label = label;
        BurnIn = burnIn;
    }
}

public class LibraryOrderItem : ObservableBase
{
    private readonly Action<string, bool> _onChanged;
    private readonly Action<string, bool>? _onHeroChanged;
    public string Id { get; }
    public string Name { get; }

    public bool IsSpecial { get; set; }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (Set(ref _isVisible, value))
            {
                _onChanged(Id, value);
            }
        }
    }

    private bool _isHeroIncluded;
    public bool IsHeroIncluded
    {
        get => _isHeroIncluded;
        set
        {
            if (Set(ref _isHeroIncluded, value))
            {
                _onHeroChanged?.Invoke(Id, value);
            }
        }
    }

    public LibraryOrderItem(string id, string name, bool isVisible, bool isHeroIncluded, Action<string, bool> onChanged, Action<string, bool>? onHeroChanged = null)
    {
        Id = id;
        Name = name;
        _isVisible = isVisible;
        _isHeroIncluded = isHeroIncluded;
        _onChanged = onChanged;
        _onHeroChanged = onHeroChanged;
    }
}

public class PageSizeOption
{
    public string Label { get; set; } = "";
    public int Size { get; set; }

    public PageSizeOption() { }

    public PageSizeOption(string label, int size)
    {
        Label = label;
        Size = size;
    }
}

public class BitrateOption
{
    public string Label { get; set; } = "";
    public long Bitrate { get; set; }

    public BitrateOption() { }

    public BitrateOption(string label, long bitrate)
    {
        Label = label;
        Bitrate = bitrate;
    }
}

/// <summary>One choice in the accent colour picker.</summary>
public class AccentSwatch : ObservableBase
{
    public string Name { get; }
    public string Hex { get; }
    public Windows.UI.Xaml.Media.SolidColorBrush Brush { get; }

    public AccentSwatch(string name, string hex)
    {
        Name = name;
        Hex = hex;
        Brush = new Windows.UI.Xaml.Media.SolidColorBrush(AccentService.Parse(hex));
    }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

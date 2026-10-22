using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace JelliBox.ViewModels;

/// <summary>State behind the top bar that every page shares.</summary>
public sealed class TopNavViewModel : ObservableBase
{
    private string _current = "";

    public ObservableCollection<NavTab> Tabs { get; } = new();
    public ObservableCollection<AccountItem> Accounts { get; } = new();

    public ICommand OpenTabCommand { get; }
    public ICommand OpenSearchCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleAccountMenuCommand { get; }
    public ICommand SwitchAccountCommand { get; }
    public ICommand AddAccountCommand { get; }

    private bool _isAccountMenuOpen;
    public bool IsAccountMenuOpen
    {
        get => _isAccountMenuOpen;
        set
        {
            if (Set(ref _isAccountMenuOpen, value) && value) RefreshAccounts();
        }
    }

    public string CurrentUsername => string.IsNullOrEmpty(App.Settings.Username) ? "User" : App.Settings.Username;

    /// <summary>The signed-in user's Jellyfin profile picture (blank image if they haven't set one).</summary>
    public string CurrentAvatarUrl => AvatarUrl(App.Settings.ServerUrl, App.Settings.UserId);

    public static string AvatarUrl(string? serverUrl, string? userId)
        => string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(userId)
            ? ""
            : $"{serverUrl.TrimEnd('/')}/Users/{userId}/Images/Primary?maxHeight=96&quality=90";

    public TopNavViewModel()
    {
        OpenTabCommand           = new RelayCommand<NavTab>(OpenTab);
        OpenSearchCommand        = new RelayCommand(() => OpenPage<SearchPage>());
        OpenSettingsCommand      = new RelayCommand(() => OpenPage<SettingsPage>());
        ToggleAccountMenuCommand = new RelayCommand(() => IsAccountMenuOpen = !IsAccountMenuOpen);
        SwitchAccountCommand     = new RelayCommand<AccountItem>(SwitchAccount);
        AddAccountCommand        = new RelayCommand(AddAccount);
    }

    /// <summary>Where the page is: "home", "livetv", or a library id. Decides which tab is highlighted.</summary>
    public void SetCurrent(string? current)
    {
        _current = current ?? "";
        foreach (var tab in Tabs) tab.IsCurrent = Matches(tab);
    }

    private bool Matches(NavTab tab)
    {
        if (tab.IsHome) return _current == "home";
        var lib = tab.Library!;
        return _current == lib.Id || (_current == "livetv" && IsLiveTv(lib));
    }

    private static bool IsLiveTv(MediaItem lib)
        => lib.CollectionType == "livetv" || string.Equals(lib.Name, "Live TV", StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuild the tabs from the libraries the user has chosen to show, in their chosen order.</summary>
    public async Task ReloadAsync()
    {
        OnPropertyChanged(nameof(CurrentUsername));
        OnPropertyChanged(nameof(CurrentAvatarUrl));
        try
        {
            var libraries = App.Api.GetLibrariesCached() ?? await App.Api.GetLibrariesAsync();
            var order = App.Settings.GetLibraryOrder();
            var visible = libraries
                .Where(l => l.CollectionType != null && !App.Settings.IsLibraryHidden(l.Id))
                .OrderBy(l => { var i = order.IndexOf(l.Id); return i >= 0 ? i : 999; })
                .ToList();

            Tabs.Clear();
            Tabs.Add(new NavTab(null, "HOME"));
            foreach (var lib in visible) Tabs.Add(new NavTab(lib, lib.Name));
            SetCurrent(_current);
        }
        catch
        {
            // The tabs are navigation sugar; the page works without them.
        }
    }

    private void OpenTab(NavTab? tab)
    {
        if (tab == null || tab.IsCurrent) return;
        var nav = App.Navigation;

        // Slide in the direction of travel along the tabs (from the right when going to a tab further right).
        var currentIndex = Tabs.ToList().FindIndex(t => t.IsCurrent);
        var fromRight = currentIndex < 0 || Tabs.IndexOf(tab) > currentIndex;

        // The new page should leave focus on the tab you just chose, not jump into its content.
        nav.RequestBarFocus();

        if (tab.IsHome)
        {
            nav.NavigateSlide<HomePage>(null, fromRight, replace: false);
            nav.ClearBackStack();
            return;
        }

        // From Home a library opens on top (Back returns Home). From anywhere else it takes the place of the
        // current page, so hopping Movies -> TV -> Music doesn't leave a trail to walk back through.
        var replace = nav.CurrentPageType != typeof(HomePage);
        var lib = tab.Library!;
        if (IsLiveTv(lib))
            nav.NavigateSlide<LiveTvPage>(null, fromRight, replace);
        else
            nav.NavigateSlide<LibraryPage>(new LibraryArgs(lib.Id, lib.Name, lib.CollectionType), fromRight, replace);
    }

    private void OpenPage<T>() where T : Windows.UI.Xaml.Controls.Page
    {
        if (App.Navigation.CurrentPageType == typeof(T)) return;
        App.Navigation.Navigate<T>();
    }

    private void RefreshAccounts()
    {
        App.Settings.SaveCurrentAccount();
        Accounts.Clear();
        foreach (var acc in App.Settings.GetSavedAccounts())
            Accounts.Add(new AccountItem(acc, acc.UserId == App.Settings.UserId));
        OnPropertyChanged(nameof(CurrentUsername));
    }

    private void SwitchAccount(AccountItem? item)
    {
        IsAccountMenuOpen = false;
        if (item == null || item.IsCurrent) return;

        AccountSession.SwitchTo(item.Account);
        OnPropertyChanged(nameof(CurrentUsername));
        OnPropertyChanged(nameof(CurrentAvatarUrl));

        // Everything now belongs to the other user: land on their Home, with no way back into the last user's pages.
        if (App.Navigation.CurrentPageType != typeof(HomePage)) App.Navigation.Navigate<HomePage>();
        App.Navigation.ClearBackStack();
    }

    private void AddAccount()
    {
        App.Settings.SaveCurrentAccount();
        IsAccountMenuOpen = false;
        App.Navigation.Navigate<LoginPage>();
    }
}

/// <summary>A tab in the top bar: Home, or one of the user's libraries.</summary>
public sealed class NavTab : ObservableBase
{
    public MediaItem? Library { get; }
    public string Name { get; }
    public bool IsHome => Library == null;

    public NavTab(MediaItem? library, string name)
    {
        Library = library;
        Name = name;
    }

    private bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

/// <summary>A saved account as shown in the user switcher.</summary>
public sealed class AccountItem
{
    public SavedAccount Account { get; }
    public bool IsCurrent { get; }
    public string Username => Account.Username;
    public string ServerUrl => Account.ServerUrl;
    public string AvatarUrl => TopNavViewModel.AvatarUrl(Account.ServerUrl, Account.UserId);

    public AccountItem(SavedAccount account, bool isCurrent)
    {
        Account = account;
        IsCurrent = isCurrent;
    }
}

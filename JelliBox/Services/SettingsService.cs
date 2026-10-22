using Windows.Security.Credentials;
using Windows.Storage;

namespace JelliBox.Services;

public class SettingsService
{
    private readonly ApplicationDataContainer _local = ApplicationData.Current.LocalSettings;
    private const string VaultResource = "JelliBox";

    public string? ServerUrl
    {
        get => _local.Values["ServerUrl"] as string;
        set { if (value != null) _local.Values["ServerUrl"] = value; else _local.Values.Remove("ServerUrl"); }
    }

    public string? UserId
    {
        get => _local.Values["UserId"] as string;
        set { if (value != null) _local.Values["UserId"] = value; else _local.Values.Remove("UserId"); }
    }

    public string? Username
    {
        get => _local.Values["Username"] as string;
        set { if (value != null) _local.Values["Username"] = value; else _local.Values.Remove("Username"); }
    }

    public string? Token
    {
        get
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(VaultResource, "token");
                cred.RetrievePassword();
                return cred.Password;
            }
            catch { return null; }
        }
        set
        {
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve(VaultResource, "token")); } catch { }
            if (value != null)
                vault.Add(new PasswordCredential(VaultResource, "token", value));
        }
    }

    public string DeviceId
    {
        get
        {
            if (_local.Values["DeviceId"] is string id) return id;
            id = Guid.NewGuid().ToString();
            _local.Values["DeviceId"] = id;
            return id;
        }
    }

    public bool HasValidServer =>
        !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);

    // ── Appearance & Layout Preferences ────────────────────────────────────────

    public int PageSize
    {
        get => _local.Values["PageSize"] as int? ?? 40;
        set => _local.Values["PageSize"] = value;
    }

    public string CustomLibraryOrder
    {
        get => _local.Values["CustomLibraryOrder"] as string ?? "";
        set => _local.Values["CustomLibraryOrder"] = value ?? "";
    }

    public List<string> GetLibraryOrder() =>
        CustomLibraryOrder.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    public void SetLibraryOrder(IEnumerable<string> ids) =>
        CustomLibraryOrder = string.Join(",", ids);

    // Combined home section order (includes "continue_watching" + library IDs)
    public string HomeSectionOrderData
    {
        get => _local.Values["HomeSectionOrder"] as string ?? "";
        set => _local.Values["HomeSectionOrder"] = value ?? "";
    }

    public List<string> GetHomeSectionOrder() =>
        HomeSectionOrderData.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    public void SetHomeSectionOrder(IEnumerable<string> ids) =>
        HomeSectionOrderData = string.Join(",", ids);

    public bool ShowHeroBanner
    {
        get => _local.Values["ShowHeroBanner"] as bool? ?? true;
        set => _local.Values["ShowHeroBanner"] = value;
    }

    /// <summary>Highlight colour used across the app (hex, e.g. #00A4DC).</summary>
    public string AccentColorHex
    {
        get => _local.Values["AccentColorHex"] as string ?? "#00A4DC";
        set => _local.Values["AccentColorHex"] = value;
    }

    /// <summary>Show a soft, blurred version of the focused item's artwork behind pages.</summary>
    public bool AmbientBackground
    {
        get => _local.Values["AmbientBackground"] as bool? ?? false;
        set => _local.Values["AmbientBackground"] = value;
    }

    public bool ShowContinueWatching
    {
        get => _local.Values["ShowContinueWatching"] as bool? ?? true;
        set => _local.Values["ShowContinueWatching"] = value;
    }

    public string HiddenLibraryIds
    {
        get => _local.Values["HiddenLibraryIds"] as string ?? "";
        set => _local.Values["HiddenLibraryIds"] = value ?? "";
    }

    public bool IsLibraryHidden(string libraryId)
    {
        if (string.IsNullOrEmpty(libraryId)) return false;
        var ids = HiddenLibraryIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return ids.Contains(libraryId);
    }

    public void SetLibraryHidden(string libraryId, bool hidden)
    {
        var ids = new HashSet<string>(HiddenLibraryIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (hidden) ids.Add(libraryId);
        else ids.Remove(libraryId);
        HiddenLibraryIds = string.Join(",", ids);
    }

    public string HeroExcludedLibraryIds
    {
        get => _local.Values["HeroExcludedLibraryIds"] as string ?? "";
        set => _local.Values["HeroExcludedLibraryIds"] = value ?? "";
    }

    public bool IsLibraryIncludedInHero(string libraryId)
    {
        if (string.IsNullOrEmpty(libraryId)) return true;
        var ids = HeroExcludedLibraryIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return !ids.Contains(libraryId);
    }

    public void SetLibraryIncludedInHero(string libraryId, bool included)
    {
        var ids = new HashSet<string>(HeroExcludedLibraryIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (!included) ids.Add(libraryId);
        else ids.Remove(libraryId);
        HeroExcludedLibraryIds = string.Join(",", ids);
    }

    // ── Playback Preferences ───────────────────────────────────────────────────

    public long MaxStreamingBitrate
    {
        get => _local.Values["MaxStreamingBitrate"] as long? ?? 20000000;
        set => _local.Values["MaxStreamingBitrate"] = value;
    }

    public bool AutoSkipIntro
    {
        get => _local.Values["AutoSkipIntro"] as bool? ?? false;
        set => _local.Values["AutoSkipIntro"] = value;
    }

    public bool AutoSkipOutro
    {
        get => _local.Values["AutoSkipOutro"] as bool? ?? false;
        set => _local.Values["AutoSkipOutro"] = value;
    }

    public bool BurnInSubtitles
    {
        get => _local.Values["BurnInSubtitles"] as bool? ?? false;
        set => _local.Values["BurnInSubtitles"] = value;
    }

    public string SubtitleFontSize
    {
        get => _local.Values["SubtitleFontSize"] as string ?? "Normal";
        set => _local.Values["SubtitleFontSize"] = value;
    }

    public string PreferredSubtitleLanguage
    {
        get => _local.Values["PreferredSubtitleLanguage"] as string ?? "Default";
        set => _local.Values["PreferredSubtitleLanguage"] = value;
    }

    public string PreferredAudioLanguage
    {
        get => _local.Values["PreferredAudioLanguage"] as string ?? "Default";
        set => _local.Values["PreferredAudioLanguage"] = value;
    }

    public string EpisodeTitleStyle
    {
        get => _local.Values["EpisodeTitleStyle"] as string ?? "Compact";
        set => _local.Values["EpisodeTitleStyle"] = value;
    }

    // ── Multi-User Accounts ────────────────────────────────────────────────────

    public string SavedAccountsData
    {
        get => _local.Values["SavedAccounts"] as string ?? "";
        set => _local.Values["SavedAccounts"] = value ?? "";
    }

    public List<SavedAccount> GetSavedAccounts()
    {
        if (string.IsNullOrWhiteSpace(SavedAccountsData)) return new List<SavedAccount>();
        try
        {
            var parts = SavedAccountsData.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<SavedAccount>();
            foreach (var part in parts)
            {
                var f = part.Split(new[] { ":::" }, StringSplitOptions.None);
                if (f.Length >= 4)
                {
                    list.Add(new SavedAccount { UserId = f[0], Username = f[1], ServerUrl = f[2], Token = f[3] });
                }
            }
            return list;
        }
        catch { return new List<SavedAccount>(); }
    }

    public void SaveCurrentAccount()
    {
        if (string.IsNullOrEmpty(UserId) || string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ServerUrl)) return;
        var list = GetSavedAccounts().Where(a => a.UserId != UserId).ToList();
        list.Insert(0, new SavedAccount
        {
            UserId = UserId,
            Username = Username ?? "User",
            ServerUrl = ServerUrl,
            Token = Token
        });
        SavedAccountsData = string.Join("|||", list.Select(a => $"{a.UserId}:::{a.Username}:::{a.ServerUrl}:::{a.Token}"));
    }

    public void RemoveAccount(string userId)
    {
        var list = GetSavedAccounts().Where(a => a.UserId != userId).ToList();
        SavedAccountsData = string.Join("|||", list.Select(a => $"{a.UserId}:::{a.Username}:::{a.ServerUrl}:::{a.Token}"));
    }

    public void ClearCredentials()
    {
        Token = null;
        UserId = null;
        Username = null;
    }
}

public class SavedAccount
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string Token { get; set; } = "";
}

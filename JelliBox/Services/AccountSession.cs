namespace JelliBox.Services;

/// <summary>Switching the signed-in account, and telling the rest of the app it happened.</summary>
public static class AccountSession
{
    /// <summary>Raised after the active account changes, so open pages can reload for the new user.</summary>
    public static event Action? Switched;

    public static void SwitchTo(SavedAccount account)
    {
        App.Api.InvalidateCache();
        var settings = App.Settings;
        settings.UserId    = account.UserId;
        settings.Username  = account.Username;
        settings.ServerUrl = account.ServerUrl;
        settings.Token     = account.Token;
        App.Api.RefreshAuthHeader();
        Switched?.Invoke();
    }
}

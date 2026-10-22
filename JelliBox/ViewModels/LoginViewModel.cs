using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System.Threading;
using System.Windows.Input;

namespace JelliBox.ViewModels;

public class LoginViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _pollingCts;

    private string _serverUrl = "";
    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (Set(ref _serverUrl, value))
            {
                if (HasQuickConnectCode)
                {
                    CancelQuickConnect();
                }
            }
        }
    }

    private string _username = "";
    public string Username { get => _username; set => Set(ref _username, value); }

    private string _password = "";
    public string Password { get => _password; set => Set(ref _password, value); }

    private string _errorMessage = "";
    public string ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private bool _isQuickConnectMode = true;
    public bool IsQuickConnectMode
    {
        get => _isQuickConnectMode;
        set
        {
            if (Set(ref _isQuickConnectMode, value))
            {
                ErrorMessage = "";
            }
        }
    }

    private string _quickConnectCode = "";
    public string QuickConnectCode { get => _quickConnectCode; set => Set(ref _quickConnectCode, value); }

    private bool _hasQuickConnectCode;
    public bool HasQuickConnectCode { get => _hasQuickConnectCode; set => Set(ref _hasQuickConnectCode, value); }

    private bool _isQuickConnectPolling;
    public bool IsQuickConnectPolling { get => _isQuickConnectPolling; set => Set(ref _isQuickConnectPolling, value); }

    private string _quickConnectStatus = "";
    public string QuickConnectStatus { get => _quickConnectStatus; set => Set(ref _quickConnectStatus, value); }

    public ICommand LoginCommand { get; }
    public ICommand SwitchToQuickConnectCommand { get; }
    public ICommand SwitchToPasswordCommand { get; }
    public ICommand StartQuickConnectCommand { get; }
    public ICommand CancelQuickConnectCommand { get; }
    public ICommand CancelCommand { get; }

    // Set when the login screen was opened on top of another page (adding an account), so it can be backed out of.
    private bool _canCancel;
    public bool CanCancel { get => _canCancel; set => Set(ref _canCancel, value); }

    // Starting a login writes the typed server into the active session before it succeeds; keep the original to restore.
    private readonly string? _originalServerUrl;
    public ICommand RefreshQuickConnectCommand { get; }

    public LoginViewModel()
    {
        _api = App.Api;
        _settings = App.Settings;
        _originalServerUrl = _settings.ServerUrl;
        ServerUrl = _settings.ServerUrl ?? "http://";
        Username = _settings.Username ?? "";

        LoginCommand = new AsyncRelayCommand(LoginAsync);
        SwitchToQuickConnectCommand = new RelayCommand(() => IsQuickConnectMode = true);
        SwitchToPasswordCommand = new RelayCommand(() => IsQuickConnectMode = false);
        StartQuickConnectCommand = new AsyncRelayCommand(StartQuickConnectAsync);
        CancelQuickConnectCommand = new RelayCommand(CancelQuickConnect);
        CancelCommand = new RelayCommand(CancelAddAccount);
        RefreshQuickConnectCommand = new AsyncRelayCommand(StartQuickConnectAsync);
    }

    /// <summary>Back out of adding an account: leave the current account exactly as it was.</summary>
    public void CancelAddAccount()
    {
        CancelQuickConnect();
        if (_originalServerUrl != null) _settings.ServerUrl = _originalServerUrl;
        _api.RefreshAuthHeader();
        App.Navigation.GoBack();
    }

    private static string NormalizeServerUrl(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(url) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "http://" + url;
        }
        return url;
    }

    public void CancelQuickConnect()
    {
        CancelPolling();
        QuickConnectCode = "";
        HasQuickConnectCode = false;
        IsQuickConnectPolling = false;
        QuickConnectStatus = "";
    }

    private void CancelPolling()
    {
        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
        }
        catch { }
        finally
        {
            _pollingCts = null;
        }
    }

    private async Task StartQuickConnectAsync()
    {
        ErrorMessage = "";
        CancelPolling();

        var url = NormalizeServerUrl(ServerUrl);
        if (string.IsNullOrWhiteSpace(url) || url == "http://" || url == "https://")
        {
            ErrorMessage = "Please enter your Jellyfin server URL first.";
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ErrorMessage = "Invalid server URL. Please include http:// or https://.";
            return;
        }

        IsLoading = true;
        try
        {
            _settings.ServerUrl = url;
            _api.RefreshAuthHeader();

            var qc = await _api.InitiateQuickConnectAsync();
            if (qc == null || string.IsNullOrWhiteSpace(qc.Code) || string.IsNullOrWhiteSpace(qc.Secret))
            {
                ErrorMessage = "Quick Connect could not be initiated. Make sure Quick Connect is enabled on your Jellyfin server, or use manual login.";
                return;
            }

            QuickConnectCode = qc.Code;
            HasQuickConnectCode = true;
            IsQuickConnectPolling = true;
            QuickConnectStatus = "Waiting for authorisation from your other device...";

            StartPolling(qc.Secret);
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Cannot reach server: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error initiating Quick Connect: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void StartPolling(string secret)
    {
        _pollingCts = new CancellationTokenSource();
        var ct = _pollingCts.Token;

        _ = Task.Run(async () =>
        {
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromMinutes(10);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2500, ct);
                    if (ct.IsCancellationRequested) break;

                    if (DateTime.UtcNow - startTime > timeout)
                    {
                        await RunOnUIThreadAsync(() =>
                        {
                            QuickConnectStatus = "Code expired. Please request a new code.";
                            IsQuickConnectPolling = false;
                            ErrorMessage = "Quick Connect code expired. Click 'Refresh Code' to try again.";
                        });
                        break;
                    }

                    var check = await _api.CheckQuickConnectAsync(secret, ct);
                    if (check != null && check.Authenticated)
                    {
                        await RunOnUIThreadAsync(() =>
                        {
                            QuickConnectStatus = "Authorised! Signing in...";
                            IsQuickConnectPolling = false;
                        });

                        var auth = await _api.CompleteQuickConnectAsync(check, ct);
                        if (auth != null)
                        {
                            await RunOnUIThreadAsync(() =>
                            {
                                App.Navigation.Navigate<HomePage>();
                                App.Navigation.ClearBackStack();
                            });
                            break;
                        }
                        else
                        {
                            await RunOnUIThreadAsync(() =>
                            {
                                ErrorMessage = "Authorisation succeeded but session could not be established. Please try again.";
                                HasQuickConnectCode = false;
                                QuickConnectCode = "";
                            });
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("400"))
                {
                    await RunOnUIThreadAsync(() =>
                    {
                        QuickConnectStatus = "Code expired. Please request a new code.";
                        IsQuickConnectPolling = false;
                        ErrorMessage = "Quick Connect session expired. Click 'Refresh Code' to try again.";
                    });
                    break;
                }
                catch (Exception)
                {
                    // Transient error, continue polling
                }
            }
        }, ct);
    }

    private async Task LoginAsync()
    {
        ErrorMessage = "";
        var url = NormalizeServerUrl(ServerUrl);
        if (string.IsNullOrWhiteSpace(url) || url == "http://" || url == "https://" ||
            string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please fill in all fields.";
            return;
        }

        IsLoading = true;
        try
        {
            _settings.ServerUrl = url;
            var result = await _api.AuthenticateAsync(Username, Password);
            if (result != null)
            {
                App.Navigation.Navigate<HomePage>();
                App.Navigation.ClearBackStack();
            }
            else
            {
                ErrorMessage = "Login failed. Check your credentials.";
            }
        }
        catch (HttpRequestException ex) { ErrorMessage = $"Cannot reach server: {ex.Message}"; }
        catch (Exception ex)             { ErrorMessage = $"Error: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private static async Task RunOnUIThreadAsync(Action action)
    {
        var dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView?.CoreWindow?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            await dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => action());
        }
        else
        {
            action();
        }
    }
}

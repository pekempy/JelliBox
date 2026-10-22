using JelliBox.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml.Input;

namespace JelliBox.Views;

public sealed partial class LoginPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private LoginViewModel VM => (LoginViewModel)DataContext;

    public LoginPage()
    {
        InitializeComponent();
        Unloaded += (s, e) => VM.CancelQuickConnect();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        VM.CanCancel = App.Navigation.CanGoBack;

        // A server that is already filled in (adding another user) needs no typing: land on the main button
        // instead of popping the on-screen keyboard. First run starts in the server box.
        var serverKnown = !string.IsNullOrWhiteSpace(VM.ServerUrl)
                          && VM.ServerUrl.Trim() is not ("http://" or "https://");
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
        {
            Control target = serverKnown ? (VM.IsQuickConnectMode ? QuickConnectStartButton : SignInButton) : ServerUrlTextBox;
            target.Focus(FocusState.Keyboard);
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        VM.CancelQuickConnect();
    }

    // PasswordBox doesn't support two-way binding — bridge it manually
    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        => VM.Password = ((PasswordBox)sender).Password;

    // Enter on the on-screen keyboard moves to the next step instead of doing nothing.
    private void ServerUrlTextBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        Windows.UI.Xaml.Input.FocusManager.TryMoveFocus(FocusNavigationDirection.Down);
        e.Handled = true;
    }

    private void UsernameTextBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        PasswordInput.Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private void PasswordInput_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        VM.Password = PasswordInput.Password;
        if (VM.LoginCommand.CanExecute(null)) VM.LoginCommand.Execute(null);
        e.Handled = true;
    }

    private void Page_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var args = new JelliBox.Services.KeyEvent(e.Key);
        HandleKey(args);
        if (args.Handled) e.Handled = true;
    }

    public void HandleKey(JelliBox.Services.KeyEvent e)
    {
        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            // B cancels Quick Connect polling if active
            if (VM.IsQuickConnectPolling)
                VM.CancelQuickConnectCommand?.Execute(null);
            // Adding an account: back out to where we came from. As the first screen there is nowhere to go.
            else if (VM.CanCancel)
                VM.CancelAddAccount();
            e.Handled = true;
        }
    }
}

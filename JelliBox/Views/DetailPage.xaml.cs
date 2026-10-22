using JelliBox.ViewModels;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace JelliBox.Views;

public sealed partial class DetailPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private DetailViewModel VM => (DetailViewModel)DataContext;

    public DetailPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            if (e.Parameter is string itemId)
            {
                await VM.LoadAsync(itemId);
                // Default A = Play — move focus to Play button after content loads
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    var focused = false;
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                    {
                        PlayButton.UpdateLayout();
                        focused = PlayButton.Focus(Windows.UI.Xaml.FocusState.Keyboard);
                    });
                    if (focused) break;
                    await Task.Delay(100);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DETAILPAGE LOAD ERROR] {ex}");
        }
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
            VM.GoBackCommand.Execute(null);
            e.Handled = true;
        }
    }
}

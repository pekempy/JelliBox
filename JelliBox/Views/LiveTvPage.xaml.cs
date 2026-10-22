using JelliBox.Models;
using JelliBox.ViewModels;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;

namespace JelliBox.Views;

public sealed partial class LiveTvPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private LiveTvViewModel VM => (LiveTvViewModel)DataContext;

    public LiveTvPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var focusBar = App.Navigation.TakeBarFocusRequest();
        if (App.Navigation.TakeSlide() is bool slideFromRight)
            JelliBox.Services.PageTransition.SlideIn(ContentArea, slideFromRight);
        if (e.NavigationMode != NavigationMode.Back)
            await VM.LoadAsync();

        if (focusBar)
        {
            await TopBar.FocusCurrentTabAsync();
            return;
        }

        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            ChannelListView.Focus(Windows.UI.Xaml.FocusState.Keyboard));
    }

    private void ChannelListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveTvChannel channel)
        {
            VM.TuneChannelCommand.Execute(channel);
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
        if (TopBar.HandleKey(e)) return;

        if (e.Key == VirtualKey.GamepadB || e.Key == VirtualKey.Escape)
        {
            VM.GoBackCommand.Execute(null);
            e.Handled = true;
        }
    }
}

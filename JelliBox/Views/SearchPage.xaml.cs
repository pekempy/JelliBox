using JelliBox.ViewModels;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.UI.Core;

namespace JelliBox.Views;

public sealed partial class SearchPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private SearchViewModel VM => (SearchViewModel)DataContext;

    public SearchPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string initialQuery && !string.IsNullOrWhiteSpace(initialQuery))
        {
            VM.SearchText = initialQuery;
        }
        
        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            SearchTextBox.Focus(Windows.UI.Xaml.FocusState.Keyboard));
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

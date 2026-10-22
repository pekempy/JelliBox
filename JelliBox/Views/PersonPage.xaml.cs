using JelliBox.Models;
using JelliBox.ViewModels;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.UI.Core;

namespace JelliBox.Views;

public sealed partial class PersonPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private PersonViewModel VM => (PersonViewModel)DataContext;

    public PersonPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode != NavigationMode.Back && e.Parameter is JelliBox.Models.Person person)
            await VM.LoadAsync(person);

        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            (Windows.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(this)
                as Windows.UI.Xaml.Controls.Control)?.Focus(Windows.UI.Xaml.FocusState.Keyboard));
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

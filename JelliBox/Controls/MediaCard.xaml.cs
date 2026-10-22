using JelliBox.Models;
using JelliBox.Services;
using JelliBox.Views;
using System;
using System.Linq;
using System.Windows.Input;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

namespace JelliBox.Controls;

public sealed partial class MediaCard : UserControl
{
    public static event EventHandler<MediaItem>? ItemRemovedFromContinueWatching;

    public const double DefaultImageHeight = 180.0;
    public const double TextSectionHeight = 36.0;
    public const double DefaultTotalHeight = DefaultImageHeight + TextSectionHeight; // 216.0

    public MediaCard()
    {
        InitializeComponent();
        ApplyDimensions();
    }

    // ── Dependency Properties ─────────────────────────────────────────────

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(MediaItem), typeof(MediaCard),
            new PropertyMetadata(null, (d, e) => ((MediaCard)d).OnItemChanged(e.NewValue as MediaItem)));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(MediaCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowProgressProperty =
        DependencyProperty.Register(nameof(ShowProgress), typeof(bool), typeof(MediaCard),
            new PropertyMetadata(false, (d, e) => {
                var c = (MediaCard)d;
                c.ApplyDimensions();
                c.UpdateContextMenu();
            }));

    public static readonly DependencyProperty RemoveFromContinueWatchingCommandProperty =
        DependencyProperty.Register(nameof(RemoveFromContinueWatchingCommand), typeof(ICommand), typeof(MediaCard),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsInContinueWatchingProperty =
        DependencyProperty.Register(nameof(IsInContinueWatching), typeof(bool), typeof(MediaCard),
            new PropertyMetadata(false, (d, e) => ((MediaCard)d).UpdateContextMenu()));

    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public ICommand? RemoveFromContinueWatchingCommand
    {
        get => (ICommand?)GetValue(RemoveFromContinueWatchingCommandProperty);
        set => SetValue(RemoveFromContinueWatchingCommandProperty, value);
    }

    public bool IsInContinueWatching
    {
        get => (bool)GetValue(IsInContinueWatchingProperty);
        set => SetValue(IsInContinueWatchingProperty, value);
    }

    private void OnItemChanged(MediaItem? item)
    {
        ApplyDimensions();
        if (item != null) LoadImage(item);
        UpdateContextMenu();
    }

    private void ApplyDimensions()
    {
        bool isEpisode = Item?.Type == "Episode";
        bool isMusic = Item?.IsMusic ?? false;

        // Height is strictly FIXED across all media cards
        double imgH = DefaultImageHeight;
        if (Height > 0 && !double.IsNaN(Height) && Height > TextSectionHeight)
        {
            imgH = Height - TextSectionHeight;
        }

        // Width is VARIABLE based on media aspect ratio
        double w;
        if (isEpisode)
        {
            // 16:9 Landscape Still (wider, exact same height as posters: 270 * 16 / 9 = 480)
            w = Math.Round(imgH * 16.0 / 9.0);
        }
        else if (isMusic)
        {
            // 1:1 Square Artwork (270 * 1 = 270)
            w = imgH;
        }
        else
        {
            // 2:3 Vertical Poster for Movies, TV Shows, and Seasons (270 * 2 / 3 = 180)
            w = Math.Round(imgH * 2.0 / 3.0);
        }

        double totalH = imgH + TextSectionHeight;

        this.Width = w;
        this.Height = totalH;

        CardButton.Width = w;
        CardButton.Height = totalH;
        CardSurface.Width = w;
        CardSurface.Height = imgH;
        Thumbnail.Width = w;
        Thumbnail.Height = imgH;

        CardScale.CenterX = w / 2.0;
        CardScale.CenterY = totalH / 2.0;

        if (ShowProgress && Item?.UserData is { PlaybackPositionTicks: > 0 } ud && Item.RunTimeTicks > 0)
        {
            var fraction = (double)ud.PlaybackPositionTicks / Item.RunTimeTicks.Value;
            ProgressBar.Width = Math.Max(0, fraction * w);
        }
    }

    private void LoadImage(MediaItem item)
    {
        var api = App.Api;
        string? url = null;

        bool isEpisode = item.Type == "Episode";
        bool isMusic = item.IsMusic;

        double w;
        int targetWidth;
        int targetHeight;

        if (isMusic)
        {
            // Music artwork is strictly 1:1 square
            w = DefaultImageHeight;
            targetWidth = (int)(DefaultImageHeight * 2); // 540
            targetHeight = (int)(DefaultImageHeight * 2); // 540
        }
        else if (isEpisode)
        {
            w = Math.Round(DefaultImageHeight * 16.0 / 9.0); // 480
            targetWidth = (int)(w * 2); // 960
            targetHeight = (int)(DefaultImageHeight * 2); // 540
        }
        else
        {
            w = Math.Round(DefaultImageHeight * 2.0 / 3.0); // 180
            targetWidth = (int)(w * 2); // 360
            targetHeight = (int)(DefaultImageHeight * 2); // 540
        }

        if (isEpisode)
        {
            // TV episodes: 16:9 landscape still
            if (item.ImageTags?.Primary != null)
                url = api.GetImageUrl(item.Id, "Primary", item.ImageTags.Primary, targetWidth, targetHeight);
            else if (item.ImageTags?.Thumb != null)
                url = api.GetImageUrl(item.Id, "Thumb", item.ImageTags.Thumb, targetWidth, targetHeight);
            else if (item.BackdropImageTags?.Length > 0)
                url = api.GetImageUrl(item.Id, "Backdrop", item.BackdropImageTags[0], targetWidth, targetHeight);
            else if (!string.IsNullOrEmpty(item.SeriesId))
                url = api.GetImageUrl(item.SeriesId, "Primary", null, targetWidth, targetHeight);
            else if (item.ParentBackdropItemId != null)
                url = api.GetImageUrl(item.ParentBackdropItemId, "Backdrop", null, targetWidth, targetHeight);
        }
        else if (isMusic)
        {
            // Music Album / Artist / Track: strictly 1:1 square artwork (540x540)
            if (item.ImageTags?.Primary != null)
                url = api.GetImageUrl(item.Id, "Primary", item.ImageTags.Primary, targetWidth, targetHeight);
            else if (!string.IsNullOrEmpty(item.AlbumId))
                url = api.GetImageUrl(item.AlbumId, "Primary", null, targetWidth, targetHeight);
            else if (item.ImageTags?.Thumb != null)
                url = api.GetImageUrl(item.Id, "Thumb", item.ImageTags.Thumb, targetWidth, targetHeight);
            else if (!string.IsNullOrEmpty(item.ParentId))
                url = api.GetImageUrl(item.ParentId, "Primary", null, targetWidth, targetHeight);
            else if (item.BackdropImageTags?.Length > 0)
                url = api.GetImageUrl(item.Id, "Backdrop", item.BackdropImageTags[0], targetWidth, targetHeight);
            else if (item.ParentBackdropItemId != null)
                url = api.GetImageUrl(item.ParentBackdropItemId, "Backdrop", null, targetWidth, targetHeight);
        }
        else
        {
            // Movies, TV Series, Seasons: 2:3 Vertical Poster
            if (item.ImageTags?.Primary != null)
                url = api.GetImageUrl(item.Id, "Primary", item.ImageTags.Primary, targetWidth, targetHeight);
            else if (item.ImageTags?.Thumb != null)
                url = api.GetImageUrl(item.Id, "Thumb", item.ImageTags.Thumb, targetWidth, targetHeight);
            else if (item.BackdropImageTags?.Length > 0)
                url = api.GetImageUrl(item.Id, "Backdrop", item.BackdropImageTags[0], targetWidth, targetHeight);
            else if (!string.IsNullOrEmpty(item.SeriesId))
                url = api.GetImageUrl(item.SeriesId, "Primary", null, targetWidth, targetHeight);
            else if (item.ParentBackdropItemId != null)
                url = api.GetImageUrl(item.ParentBackdropItemId, "Backdrop", null, targetWidth, targetHeight);
        }

        if (url != null)
        {
            // Check in-memory cache — instant display, no decode cost
            var cached = ImageCache.Get(url);
            if (cached != null)
            {
                Thumbnail.Source = cached;
                Thumbnail.Opacity = 1;
            }
            else
            {
                var uri = new Uri(url);
                if (ThumbSource.UriSource != uri)
                {
                    Thumbnail.Opacity = 0;
                    ThumbSource.ImageOpened -= OnThumbImageOpened;
                    ThumbSource.ImageFailed -= OnThumbImageFailed;
                    ThumbSource.ImageOpened += OnThumbImageOpened;
                    ThumbSource.ImageFailed += OnThumbImageFailed;
                    ThumbSource.DecodePixelWidth = targetWidth;
                    ThumbSource.DecodePixelHeight = targetHeight;
                    ThumbSource.UriSource = uri;
                }
            }
        }

        if (ShowProgress && item.UserData is { PlaybackPositionTicks: > 0 } ud && item.RunTimeTicks > 0)
        {
            var fraction = (double)ud.PlaybackPositionTicks / item.RunTimeTicks.Value;
            ProgressBar.Width = Math.Max(0, fraction * this.Width);
        }
    }
    private void OnThumbImageOpened(object sender, RoutedEventArgs e)
    {
        ThumbSource.ImageOpened -= OnThumbImageOpened;
        ThumbSource.ImageFailed -= OnThumbImageFailed;

        // Populate cache so next time this image appears it's instant
        if (ThumbSource.UriSource != null)
            ImageCache.Put(ThumbSource.UriSource.AbsoluteUri, ThumbSource);

        var sb = new Storyboard();
        var anim = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(anim, Thumbnail);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void OnThumbImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ThumbSource.ImageOpened -= OnThumbImageOpened;
        ThumbSource.ImageFailed -= OnThumbImageFailed;
        Thumbnail.Opacity = 1;
    }


    private void CardButton_GotFocus(object sender, RoutedEventArgs e)
    {
        FocusIn.Begin();
        AmbientService.SetItem(Item);
    }

    private void CardButton_LostFocus(object sender, RoutedEventArgs e)
        => FocusOut.Begin();

    // ── Context Menu (Xbox Start / Menu Button & Right Click) ─────────────

    public void ShowContextMenu()
    {
        UpdateContextMenu();
        CardMenuFlyout.ShowAt(CardButton);
    }

    private void CardButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.GamepadMenu or VirtualKey.Menu)
        {
            UpdateContextMenu();
            CardMenuFlyout.ShowAt(CardButton);
            e.Handled = true;
        }
    }

    private void CardButton_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        UpdateContextMenu();
    }

    private void CardMenuFlyout_Opening(object sender, object e)
    {
        UpdateContextMenu();
    }

    public void UpdateContextMenu()
    {
        bool isFav = Item?.UserData?.IsFavorite ?? false;
        FavoriteMenuItem.Text = isFav ? "Remove from Favourites" : "Add to Favourites";
        if (FavoriteMenuIcon != null)
        {
            FavoriteMenuIcon.Glyph = isFav ? "\uE735" : "\uE734";
        }

        bool isPlayed = Item?.UserData?.Played ?? false;
        WatchedMenuItem.Text = isPlayed ? "Mark as Unwatched" : "Mark as Watched";

        bool isContinueWatchingItem = IsInContinueWatching ||
                                      ShowProgress ||
                                      (Item?.UserData?.PlaybackPositionTicks ?? 0) > 0;

        if (ContinueWatchingSeparator != null)
        {
            ContinueWatchingSeparator.Visibility = isContinueWatchingItem ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RemoveFromContinueWatchingMenuItem != null)
        {
            RemoveFromContinueWatchingMenuItem.Visibility = isContinueWatchingItem ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        if (Item != null)
        {
            await PlaybackHelper.PlayAsync(Item);
        }
    }

    private void OnDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (Item != null)
        {
            App.Navigation.Navigate<DetailPage>(Item.Id);
        }
    }

    private async void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (Item == null) return;
        bool newFav = !(Item.UserData?.IsFavorite ?? false);
        if (Item.UserData == null) Item.UserData = new UserData();
        Item.UserData.IsFavorite = newFav;
        await App.Api.ToggleFavoriteAsync(Item.Id, newFav);
        UpdateContextMenu();
    }

    private async void OnWatchedClicked(object sender, RoutedEventArgs e)
    {
        if (Item == null) return;
        bool newWatched = !(Item.UserData?.Played ?? false);
        if (Item.UserData == null) Item.UserData = new UserData();
        Item.UserData.Played = newWatched;
        WatchedBadge.Visibility = newWatched ? Visibility.Visible : Visibility.Collapsed;
        await App.Api.ToggleWatchedAsync(Item.Id, newWatched);
        UpdateContextMenu();
    }

    private async void OnAddToCollectionClicked(object sender, RoutedEventArgs e)
    {
        if (Item == null) return;
        try
        {
            var collections = await App.Api.GetCollectionsAsync();
            var flyout = new MenuFlyout();

            if (collections.Count > 0)
            {
                foreach (var col in collections)
                {
                    var mItem = new MenuFlyoutItem { Text = col.Name };
                    mItem.Click += async (s, args) =>
                    {
                        await App.Api.AddToCollectionAsync(col.Id, Item.Id);
                    };
                    flyout.Items.Add(mItem);
                }
                flyout.Items.Add(new MenuFlyoutSeparator());
            }

            var newColItem = new MenuFlyoutItem { Text = "+ New Collection..." };
            newColItem.Click += async (s, args) =>
            {
                var input = new TextBox { PlaceholderText = "Collection Name", Margin = new Thickness(0, 10, 0, 0) };
                var dialog = new ContentDialog
                {
                    Title = "Create Collection",
                    Content = input,
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
                {
                    await App.Api.CreateCollectionAsync(input.Text.Trim(), Item.Id);
                }
            };
            flyout.Items.Add(newColItem);

            flyout.ShowAt(CardButton);
        }
        catch { }
    }

    private async void OnRemoveFromContinueWatchingClicked(object sender, RoutedEventArgs e)
    {
        if (Item == null) return;
        var item = Item;

        if (item.UserData != null)
        {
            item.UserData.PlaybackPositionTicks = 0;
        }
        ProgressBar.Width = 0;
        if (ContinueWatchingSeparator != null)
            ContinueWatchingSeparator.Visibility = Visibility.Collapsed;
        if (RemoveFromContinueWatchingMenuItem != null)
            RemoveFromContinueWatchingMenuItem.Visibility = Visibility.Collapsed;

        // Fire global event so HomeViewModel removes it immediately from ContinueWatching collection
        ItemRemovedFromContinueWatching?.Invoke(this, item);

        if (RemoveFromContinueWatchingCommand != null && RemoveFromContinueWatchingCommand.CanExecute(item))
        {
            RemoveFromContinueWatchingCommand.Execute(item);
        }

        await App.Api.RemoveFromContinueWatchingAsync(item.Id);
    }
}

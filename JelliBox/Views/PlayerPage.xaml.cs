using JelliBox.Models;
using JelliBox.ViewModels;
using System;
using System.Linq;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Core;

namespace JelliBox.Views;

public sealed partial class PlayerPage : Page, JelliBox.Services.IGamepadKeyTarget
{
    private PlayerViewModel VM => (PlayerViewModel)DataContext;
    private DispatcherTimer? _hideTimer;
    private MediaPlayer? _player;
    private Windows.Media.Core.MediaSource? _mediaSource;
    private MediaPlaybackItem? _playbackItem;
    private TimeSpan? _pendingSeekPosition;
    private Storyboard? _controlsFadeSb;

    private bool _closed;

    public PlayerPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not PlayerArgs args) return;

        await VM.InitAsync(args);

        // Wire ViewModel events -> MediaPlayer
        VM.PauseRequested += OnPauseRequested;
        VM.SeekDeltaRequested += OnSeekDeltaRequested;
        VM.SeekToRequested += OnSeekToRequested;
        VM.StreamReloadRequested += OnStreamReloadRequested;
        VM.StretchChanged += OnStretchChanged;
        VM.SubtitleTrackSelected += OnSubtitleTrackSelected;
        VM.SubtitleOffsetChanged += OnSubtitleOffsetChanged;

        // Set up the MediaSource & MediaPlaybackItem for native playback
        _mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(args.StreamUrl));
        _playbackItem = new MediaPlaybackItem(_mediaSource);

        _player = new MediaPlayer();
        _player.Source = _playbackItem;
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;

        // Buffering & Download progress tracking for UI
        _player.PlaybackSession.BufferingStarted += (_, _) => DispatchAsync(() =>
        {
            VM.IsBuffering = true;
            VM.BufferingText = "Buffering...";
        });
        _player.PlaybackSession.BufferingEnded += (_, _) => DispatchAsync(() =>
        {
            VM.IsBuffering = false;
        });
        _player.PlaybackSession.BufferingProgressChanged += (_, _) => DispatchAsync(() =>
        {
            if (_player != null)
            {
                VM.UpdateBufferingProgress(_player.PlaybackSession.BufferingProgress);
            }
        });
        _player.PlaybackSession.DownloadProgressChanged += (_, _) => DispatchAsync(() =>
        {
            if (_player != null)
            {
                VM.UpdateDownloadProgress(_player.PlaybackSession.DownloadProgress);
            }
        });
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatchAsync(() =>
        {
            if (_player == null) return;
            var state = _player.PlaybackSession.PlaybackState;
            if (state == MediaPlaybackState.Buffering || state == MediaPlaybackState.Opening)
            {
                VM.IsBuffering = true;
            }
            else if (state == MediaPlaybackState.Playing || state == MediaPlaybackState.Paused)
            {
                VM.IsBuffering = false;
            }
        });

        // Position updates -> ViewModel every second
        _player.PlaybackSession.PositionChanged += OnPositionChanged;

        Player.SetMediaPlayer(_player);

        // Seek to resume position
        if (args.StartTicks > 0)
        {
            _pendingSeekPosition = TimeSpan.FromTicks(args.StartTicks);
            _player.PlaybackSession.Position = TimeSpan.FromTicks(args.StartTicks);
        }

        // If default subtitles were selected and we are in native mode, attach them
        if (VM.SelectedSubtitleIndex.HasValue && VM.SelectedSubtitleIndex.Value >= 0)
        {
            var defTrack = VM.SubtitleTracks.FirstOrDefault(t => t.Index == VM.SelectedSubtitleIndex.Value);
            bool burnIn = VM.BurnInSubtitles || (defTrack?.IsImageBased ?? false);
            if (!burnIn)
            {
                ApplyNativeSubtitle(VM.SelectedSubtitleIndex.Value);
            }
        }

        StartHideTimer();

        // Focus play/pause button so A immediately plays without needing D-pad navigation
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            TogglePauseButton.Focus(Windows.UI.Xaml.FocusState.Keyboard));
    }

    protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        _closed = true;
        _hideTimer?.Stop();
        _controlsFadeSb?.Stop();
        long? currentPosTicks = null;
        var player = _player;
        _player = null;
        if (player != null)
        {
            try
            {
                player.Pause();
                currentPosTicks = player.PlaybackSession.Position.Ticks;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PLAYER] pause on exit failed: {ex.Message}"); }

            // Stop events from reaching the page, and detach the player before disposing it.
            player.MediaOpened -= OnMediaOpened;
            player.MediaFailed -= OnMediaFailed;
            player.PlaybackSession.PositionChanged -= OnPositionChanged;
            try { Player.SetMediaPlayer(null); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PLAYER] detach failed: {ex.Message}"); }
        }

        try { await VM.OnStoppedAsync(currentPosTicks); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PLAYER] report stop failed: {ex.Message}"); }

        try { player?.Dispose(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PLAYER] dispose failed: {ex.Message}"); }
        _mediaSource?.Dispose();
        _mediaSource = null;
        _playbackItem = null;

        VM.PauseRequested -= OnPauseRequested;
        VM.SeekDeltaRequested -= OnSeekDeltaRequested;
        VM.SeekToRequested -= OnSeekToRequested;
        VM.StreamReloadRequested -= OnStreamReloadRequested;
        VM.StretchChanged -= OnStretchChanged;
        VM.SubtitleTrackSelected -= OnSubtitleTrackSelected;
        VM.SubtitleOffsetChanged -= OnSubtitleOffsetChanged;
    }

    private void OnSeekToRequested(long ticks)
    {
        if (_player == null) return;
        DispatchAsync(() =>
        {
            if (_player == null) return;
            var target = TimeSpan.FromTicks(ticks);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            _player.PlaybackSession.Position = target;
        });
    }

    private void OnSubtitleTrackSelected(int? trackIndex, bool burnIn)
    {
        if (_player == null || VM.Item == null || VM.MediaSource == null) return;

        if (!trackIndex.HasValue || trackIndex.Value < 0)
        {
            // Turn off subtitles
            if (_playbackItem != null)
            {
                for (uint i = 0; i < (uint)_playbackItem.TimedMetadataTracks.Count; i++)
                {
                    _playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
                }
            }
            return;
        }

        if (burnIn)
        {
            OnStreamReloadRequested();
            return;
        }

        ApplyNativeSubtitle(trackIndex.Value);
    }

    private void ApplyNativeSubtitle(int trackIndex)
    {
        if (_mediaSource == null || _playbackItem == null || VM.Item == null || VM.MediaSource == null) return;

        for (uint i = 0; i < (uint)_playbackItem.TimedMetadataTracks.Count; i++)
        {
            _playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.Disabled);
        }

        var subUrl = App.Api.GetSubtitleStreamUrl(VM.Item.Id, VM.MediaSource.Id, trackIndex, "vtt");
        var tts = TimedTextSource.CreateFromUri(new Uri(subUrl));
        tts.Resolved += (sender, args) =>
        {
            DispatchAsync(() =>
            {
                if (_playbackItem == null) return;

                if (VM.SubtitleOffsetSeconds != 0)
                {
                    var delta = TimeSpan.FromSeconds(VM.SubtitleOffsetSeconds);
                    foreach (var track in args.Tracks)
                    {
                        foreach (var cue in track.Cues)
                        {
                            if (cue is TimedTextCue tc)
                            {
                                tc.StartTime = tc.StartTime.Add(delta);
                            }
                        }
                    }
                }

                for (uint i = 0; i < (uint)_playbackItem.TimedMetadataTracks.Count; i++)
                {
                    var metaTrack = _playbackItem.TimedMetadataTracks[(int)i];
                    if (args.Tracks.Contains(metaTrack))
                    {
                        _playbackItem.TimedMetadataTracks.SetPresentationMode(i, TimedMetadataTrackPresentationMode.PlatformPresented);
                    }
                }
            });
        };

        _mediaSource.ExternalTimedTextSources.Add(tts);
    }

    private void OnSubtitleOffsetChanged(double deltaSeconds)
    {
        if (_playbackItem == null) return;
        var delta = TimeSpan.FromSeconds(deltaSeconds);
        DispatchAsync(() =>
        {
            foreach (var track in _playbackItem.TimedMetadataTracks)
            {
                foreach (var cue in track.Cues)
                {
                    if (cue is TimedTextCue tc)
                    {
                        tc.StartTime = tc.StartTime.Add(delta);
                    }
                }
            }
        });
    }

    private void OnStreamReloadRequested()
    {
        if (_player == null || VM.Item == null || VM.MediaSource == null) return;
        var currentPos = _player.PlaybackSession.Position;
        _pendingSeekPosition = currentPos;
        VM.IsBuffering = true;
        VM.BufferingText = "Loading stream...";

        var subTrack = VM.SelectedSubtitleIndex.HasValue
            ? VM.SubtitleTracks.FirstOrDefault(t => t.Index == VM.SelectedSubtitleIndex.Value)
            : null;
        bool burn = VM.BurnInSubtitles || (subTrack?.IsImageBased ?? false);

        var newUrl = App.Api.GetStreamUrl(
            VM.Item,
            VM.MediaSource,
            audioStreamIndex: VM.SelectedAudioIndex,
            subtitleStreamIndex: VM.SelectedSubtitleIndex,
            maxBitrate: VM.SelectedBitrate,
            playSessionId: VM.SessionId,
            burnSubtitles: burn);

        _mediaSource?.Dispose();
        _mediaSource = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(newUrl));
        _playbackItem = new MediaPlaybackItem(_mediaSource);
        _player.Source = _playbackItem;
        _player.Play();
    }

    private void OnStretchChanged(Windows.UI.Xaml.Media.Stretch stretch)
    {
        Player.Stretch = stretch;
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        DispatchAsync(() =>
        {
            VM.IsBuffering = false;
        });
        if (_pendingSeekPosition.HasValue)
        {
            var target = _pendingSeekPosition.Value;
            _pendingSeekPosition = null;
            DispatchAsync(() =>
            {
                if (_player != null)
                {
                    _player.PlaybackSession.Position = target;
                }
            });
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatchAsync(() =>
        {
            VM.IsBuffering = false;
            ErrorMessageText.Text = $"The server could not play this stream ({args.Error}: {args.ErrorMessage ?? args.ExtendedErrorCode?.Message}).";
            ErrorBanner.Visibility = Visibility.Visible;
            ShowControls();
            // Move focus to error banner so user can press A to go back
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                ErrorGoBackButton.Focus(Windows.UI.Xaml.FocusState.Keyboard));
        });
    }

    private void OnPositionChanged(MediaPlaybackSession session, object args)
    {
        DispatchAsync(() =>
        {
            var pos = session.Position.Ticks;
            var dur = session.NaturalDuration.Ticks;
            VM.UpdatePosition(pos, dur);
        });
    }

    private void TimelineSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        VM.StartScrubbing();
        _hideTimer?.Stop();
    }

    private void TimelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        VM.FinishScrubbing(TimelineSlider.Value);
        StartHideTimer();
    }

    private void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VM.IsScrubbing)
        {
            VM.UpdateScrubbing(e.NewValue);
        }
    }

    private void TimelineSlider_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.GamepadLeftThumbstickLeft or VirtualKey.GamepadLeftThumbstickRight or
            VirtualKey.GamepadDPadLeft or VirtualKey.GamepadDPadRight or
            VirtualKey.GamepadA or VirtualKey.Enter)
        {
            VM.FinishScrubbing(TimelineSlider.Value);
            StartHideTimer();
        }
    }

    private void OnPauseRequested(bool pause)
    {
        if (pause) _player?.Pause();
        else _player?.Play();
    }

    private void OnSeekDeltaRequested(long deltaTicks)
    {
        if (_player == null) return;
        var newPos = _player.PlaybackSession.Position + TimeSpan.FromTicks(deltaTicks);
        if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
        _player.PlaybackSession.Position = newPos;
    }

    // Controls visibility

    private void StartHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hideTimer.Tick += (_, _) =>
        {
            if (VM.IsAnyMenuOpen) return;
            _hideTimer.Stop();
            HideControls();
        };
        _hideTimer.Start();
    }

    private bool IsFocusInsideControls()
    {
        var d = Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() as DependencyObject;
        for (; d != null; d = Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ControlsOverlay)) return true;
        return false;
    }

    private void ShowControls()
    {
        // Only pull focus to Play when the overlay was hidden; otherwise D-pad moves would snap back to it.
        bool needsFocus = ControlsOverlay.Visibility != Windows.UI.Xaml.Visibility.Visible
                          || ControlsOverlay.Opacity < 1 || !IsFocusInsideControls();
        _controlsFadeSb?.Stop();
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.Visibility = Windows.UI.Xaml.Visibility.Visible;
        StartHideTimer();
        if (!VM.IsAnyMenuOpen && needsFocus)
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
                TogglePauseButton.Focus(Windows.UI.Xaml.FocusState.Keyboard));
    }

    private void HideControls()
    {
        _hideTimer?.Stop();
        _controlsFadeSb?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new Windows.UI.Xaml.Media.Animation.CubicEase { EasingMode = Windows.UI.Xaml.Media.Animation.EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, ControlsOverlay);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Completed += (_, _) =>
        {
            ControlsOverlay.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            ControlsOverlay.Opacity = 1;
        };
        _controlsFadeSb = sb;
        sb.Begin();
    }


    private void ControlsOverlay_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ShowControls();

    // Gamepad / keyboard
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
            if (VM.IsAnyMenuOpen)
            {
                VM.CloseMenus();
                ShowControls();
                // Restore focus to play/pause button
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    TogglePauseButton.Focus(Windows.UI.Xaml.FocusState.Keyboard));
                e.Handled = true;
                return;
            }
            if (ControlsOverlay.Visibility == Windows.UI.Xaml.Visibility.Visible)
            {
                HideControls();
                e.Handled = true;
                return;
            }
            VM.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        bool wasHidden = ControlsOverlay.Visibility != Windows.UI.Xaml.Visibility.Visible
                         || ControlsOverlay.Opacity < 1;
        ShowControls();
        if (wasHidden && e.Key is VirtualKey.GamepadDPadUp or VirtualKey.GamepadDPadDown
            or VirtualKey.GamepadDPadLeft or VirtualKey.GamepadDPadRight
            or VirtualKey.GamepadLeftThumbstickUp or VirtualKey.GamepadLeftThumbstickDown
            or VirtualKey.GamepadLeftThumbstickLeft or VirtualKey.GamepadLeftThumbstickRight)
        {
            // First press just reveals the controls.
            e.Handled = true;
            return;
        }

        if (VM.IsAnyMenuOpen) return;

        switch (e.Key)
        {
            case VirtualKey.GamepadLeftTrigger:
                VM.SeekBySeconds(-15);
                e.Handled = true;
                break;
            case VirtualKey.GamepadRightTrigger:
                VM.SeekBySeconds(15);
                e.Handled = true;
                break;
            case VirtualKey.GamepadA:
            case VirtualKey.Space:
                // A on a focused control (Quality, Audio, +30s, ...) activates that control; only toggle pause otherwise.
                if (Windows.UI.Xaml.Input.FocusManager.GetFocusedElement() is Windows.UI.Xaml.Controls.Primitives.ButtonBase
                    or Windows.UI.Xaml.Controls.Slider)
                    break;
                VM.TogglePauseCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.GamepadRightShoulder:
            case VirtualKey.Right:
                VM.SkipForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.GamepadLeftShoulder:
            case VirtualKey.Left:
                VM.SkipBackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void DispatchAsync(Action action)
        => _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
        {
            if (_closed) return;
            try { action(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PLAYER] ui callback failed: {ex}"); }
        });
}

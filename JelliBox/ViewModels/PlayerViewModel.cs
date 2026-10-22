using JelliBox.Models;
using JelliBox.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.UI.Xaml.Media;

namespace JelliBox.ViewModels;

public class PlayerViewModel : ObservableBase
{
    private readonly JellyfinApiService _api;
    private System.Threading.Timer? _progressTimer;
    private long _currentTicks;
    private string _mediaSourceId = "";
    private string _sessionId = "";
    private bool _hasStopped;

    public string SessionId => _sessionId;
    public string MediaSourceId => _mediaSourceId;

    private MediaItem? _item;
    public MediaItem? Item { get => _item; set => Set(ref _item, value); }

    private MediaSource? _mediaSource;
    public MediaSource? MediaSource { get => _mediaSource; set => Set(ref _mediaSource, value); }

    private string _streamUrl = "";
    public string StreamUrl { get => _streamUrl; set => Set(ref _streamUrl, value); }

    private long _startTicks;
    public long StartTicks { get => _startTicks; set => Set(ref _startTicks, value); }

    private bool _isPaused;
    public bool IsPaused { get => _isPaused; set => Set(ref _isPaused, value); }

    private string _timeDisplay = "0:00 / 0:00";
    public string TimeDisplay { get => _timeDisplay; set => Set(ref _timeDisplay, value); }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set => Set(ref _progressPercent, value); }

    private double _sliderPosition;
    public double SliderPosition { get => _sliderPosition; set => Set(ref _sliderPosition, value); }

    private bool _isScrubbing;
    public bool IsScrubbing { get => _isScrubbing; set => Set(ref _isScrubbing, value); }

    private long _totalDurationTicks;
    public long TotalDurationTicks => _totalDurationTicks;

    private string _itemTitle = "";
    public string ItemTitle { get => _itemTitle; set => Set(ref _itemTitle, value); }

    private string _itemSubtitle = "";
    public string ItemSubtitle { get => _itemSubtitle; set => Set(ref _itemSubtitle, value); }

    private string? _posterUrl;
    public string? PosterUrl { get => _posterUrl; set => Set(ref _posterUrl, value); }

    public double MiniPosterWidth => (Item?.IsMusic ?? false) ? 90.0 : 60.0;

    // Stream & Format Badges
    private string _resolutionBadge = "";
    public string ResolutionBadge { get => _resolutionBadge; set => Set(ref _resolutionBadge, value); }

    private string _videoCodecBadge = "";
    public string VideoCodecBadge { get => _videoCodecBadge; set => Set(ref _videoCodecBadge, value); }

    private string _audioBadge = "";
    public string AudioBadge { get => _audioBadge; set => Set(ref _audioBadge, value); }

    private string _hdrBadge = "";
    public string HdrBadge { get => _hdrBadge; set => Set(ref _hdrBadge, value); }

    private string _playMethodBadge = "DIRECT PLAY";
    public string PlayMethodBadge { get => _playMethodBadge; set => Set(ref _playMethodBadge, value); }

    private bool _isTranscoding;
    public bool IsTranscoding { get => _isTranscoding; set => Set(ref _isTranscoding, value); }

    public bool HasSubtitlesBadge => SelectedSubtitleIndex.HasValue && SelectedSubtitleIndex.Value >= 0;

    // Buffering & Download progress
    private bool _isBuffering = true;
    public bool IsBuffering { get => _isBuffering; set => Set(ref _isBuffering, value); }

    private string _bufferingText = "Starting Playback...";
    public string BufferingText { get => _bufferingText; set => Set(ref _bufferingText, value); }

    private double _bufferingProgressValue = 0;
    public double BufferingProgressValue { get => _bufferingProgressValue; set => Set(ref _bufferingProgressValue, value); }

    private bool _isIndeterminate = true;
    public bool IsIndeterminate { get => _isIndeterminate; set => Set(ref _isIndeterminate, value); }

    private bool _hasBufferingPercent;
    public bool HasBufferingPercent { get => _hasBufferingPercent; set => Set(ref _hasBufferingPercent, value); }

    private string _bufferingPercentText = "";
    public string BufferingPercentText { get => _bufferingPercentText; set => Set(ref _bufferingPercentText, value); }

    private double _bufferPosition;
    public double BufferPosition { get => _bufferPosition; set => Set(ref _bufferPosition, value); }

    private double _bufferPercent;
    public double BufferPercent { get => _bufferPercent; set => Set(ref _bufferPercent, value); }

    // Live Clock & Ends-At Time
    private string _currentClockText = "";
    public string CurrentClockText { get => _currentClockText; set => Set(ref _currentClockText, value); }

    private string _endsAtText = "";
    public string EndsAtText { get => _endsAtText; set => Set(ref _endsAtText, value); }

    private string _timeRemainingText = "";
    public string TimeRemainingText { get => _timeRemainingText; set => Set(ref _timeRemainingText, value); }

    // Center HUD Flash (Seek & Play/Pause Feedback)
    private System.Threading.Timer? _hudTimer;
    private string _centerHudIcon = "";
    public string CenterHudIcon { get => _centerHudIcon; set => Set(ref _centerHudIcon, value); }

    private string _centerHudText = "";
    public string CenterHudText { get => _centerHudText; set => Set(ref _centerHudText, value); }

    private bool _showCenterHud;
    public bool ShowCenterHud { get => _showCenterHud; set => Set(ref _showCenterHud, value); }

    // Menus visibility
    private bool _isAudioMenuOpen;
    public bool IsAudioMenuOpen
    {
        get => _isAudioMenuOpen;
        set
        {
            if (Set(ref _isAudioMenuOpen, value) && value)
            {
                IsSubtitleMenuOpen = false;
                IsVideoMenuOpen = false;
            }
        }
    }

    private bool _isSubtitleMenuOpen;
    public bool IsSubtitleMenuOpen
    {
        get => _isSubtitleMenuOpen;
        set
        {
            if (Set(ref _isSubtitleMenuOpen, value) && value)
            {
                IsAudioMenuOpen = false;
                IsVideoMenuOpen = false;
            }
        }
    }

    private bool _isVideoMenuOpen;
    public bool IsVideoMenuOpen
    {
        get => _isVideoMenuOpen;
        set
        {
            if (Set(ref _isVideoMenuOpen, value) && value)
            {
                IsAudioMenuOpen = false;
                IsSubtitleMenuOpen = false;
            }
        }
    }

    public bool IsAnyMenuOpen => IsAudioMenuOpen || IsSubtitleMenuOpen || IsVideoMenuOpen;

    // Selections
    public int? SelectedAudioIndex { get; set; }
    public int? SelectedSubtitleIndex { get; set; }
    public long? SelectedBitrate { get; set; }
    public Stretch SelectedStretch { get; set; } = Stretch.Uniform;

    // Collections
    public ObservableCollection<TrackOptionItem> AudioTracks { get; } = new();
    public ObservableCollection<TrackOptionItem> SubtitleTracks { get; } = new();
    public ObservableCollection<QualityOptionItem> QualityOptions { get; } = new();
    public ObservableCollection<StretchOptionItem> StretchOptions { get; } = new();

    // Skip intro / outro
    private bool _showSkipIntro;
    public bool ShowSkipIntro { get => _showSkipIntro; set => Set(ref _showSkipIntro, value); }

    private bool _showSkipOutro;
    public bool ShowSkipOutro { get => _showSkipOutro; set => Set(ref _showSkipOutro, value); }

    private List<MediaSegment> _segments = new();
    private readonly HashSet<MediaSegment> _autoSkippedSegments = new();
    private MediaSegment? _currentIntroSegment;
    private MediaSegment? _currentOutroSegment;

    // Subtitle Sync Offset
    private double _subtitleOffsetSeconds = 0.0;
    public double SubtitleOffsetSeconds
    {
        get => _subtitleOffsetSeconds;
        set
        {
            if (Set(ref _subtitleOffsetSeconds, value))
            {
                OnPropertyChanged(nameof(SubtitleOffsetText));
            }
        }
    }

    public string SubtitleOffsetText =>
        _subtitleOffsetSeconds == 0
            ? "0.0s (In Sync)"
            : (_subtitleOffsetSeconds > 0 ? $"+{_subtitleOffsetSeconds:F2}s" : $"{_subtitleOffsetSeconds:F2}s");

    public bool BurnInSubtitles
    {
        get => App.Settings.BurnInSubtitles;
        set
        {
            if (App.Settings.BurnInSubtitles != value)
            {
                App.Settings.BurnInSubtitles = value;
                OnPropertyChanged(nameof(BurnInSubtitles));
                if (SelectedSubtitleIndex.HasValue && SelectedSubtitleIndex.Value >= 0)
                {
                    var track = SubtitleTracks.FirstOrDefault(t => t.Index == SelectedSubtitleIndex.Value);
                    SubtitleTrackSelected?.Invoke(SelectedSubtitleIndex.Value, value || (track?.IsImageBased ?? false));
                }
            }
        }
    }

    // Events
    public event Action<bool>? PauseRequested;
    public event Action<long>? SeekDeltaRequested;
    public event Action<long>? SeekToRequested;
    public event Action? StreamReloadRequested;
    public event Action<Stretch>? StretchChanged;
    public event Action<int?, bool>? SubtitleTrackSelected;
    public event Action<double>? SubtitleOffsetChanged;

    // Commands
    public ICommand TogglePauseCommand            { get; }
    public ICommand SkipForwardCommand            { get; }
    public ICommand SkipBackCommand               { get; }
    public ICommand GoBackCommand                 { get; }
    public ICommand SkipIntroCommand              { get; }
    public ICommand SkipOutroCommand              { get; }
    public ICommand ToggleAudioMenuCommand        { get; }
    public ICommand ToggleSubtitleMenuCommand     { get; }
    public ICommand ToggleVideoMenuCommand        { get; }
    public ICommand CloseMenusCommand             { get; }
    public ICommand SelectAudioTrackCommand       { get; }
    public ICommand SelectSubtitleTrackCommand    { get; }
    public ICommand SelectQualityCommand          { get; }
    public ICommand SelectStretchCommand          { get; }
    public ICommand SubtitleDelayMinusHalfCommand { get; }
    public ICommand SubtitleDelayMinusCommand     { get; }
    public ICommand SubtitleDelayPlusCommand      { get; }
    public ICommand SubtitleDelayPlusHalfCommand  { get; }
    public ICommand SubtitleDelayResetCommand     { get; }
    public ICommand ToggleBurnSubtitlesCommand    { get; }

    public PlayerViewModel()
    {
        _api = App.Api;
        TogglePauseCommand            = new RelayCommand(TogglePause);
        SkipForwardCommand            = new RelayCommand(() => SeekDelta( 30 * TimeSpan.TicksPerSecond));
        SkipBackCommand               = new RelayCommand(() => SeekDelta(-10 * TimeSpan.TicksPerSecond));
        GoBackCommand                 = new AsyncRelayCommand(GoBackAsync);
        SkipIntroCommand              = new RelayCommand(SkipIntro);
        SkipOutroCommand              = new RelayCommand(SkipOutro);

        ToggleAudioMenuCommand        = new RelayCommand(() => IsAudioMenuOpen = !IsAudioMenuOpen);
        ToggleSubtitleMenuCommand     = new RelayCommand(() => IsSubtitleMenuOpen = !IsSubtitleMenuOpen);
        ToggleVideoMenuCommand        = new RelayCommand(() => IsVideoMenuOpen = !IsVideoMenuOpen);
        CloseMenusCommand             = new RelayCommand(CloseMenus);

        SelectAudioTrackCommand       = new RelayCommand<TrackOptionItem>(SelectAudioTrack);
        SelectSubtitleTrackCommand    = new RelayCommand<TrackOptionItem>(SelectSubtitleTrack);
        SelectQualityCommand          = new RelayCommand<QualityOptionItem>(SelectQuality);
        SelectStretchCommand          = new RelayCommand<StretchOptionItem>(SelectStretch);

        SubtitleDelayMinusHalfCommand = new RelayCommand(() => AdjustSubtitleOffset(-0.5));
        SubtitleDelayMinusCommand     = new RelayCommand(() => AdjustSubtitleOffset(-0.25));
        SubtitleDelayPlusCommand      = new RelayCommand(() => AdjustSubtitleOffset(0.25));
        SubtitleDelayPlusHalfCommand  = new RelayCommand(() => AdjustSubtitleOffset(0.5));
        SubtitleDelayResetCommand     = new RelayCommand(() => SetSubtitleOffset(0.0));
        ToggleBurnSubtitlesCommand    = new RelayCommand(() => BurnInSubtitles = !BurnInSubtitles);
    }

    public void CloseMenus()
    {
        IsAudioMenuOpen = false;
        IsSubtitleMenuOpen = false;
        IsVideoMenuOpen = false;
    }

    public async Task InitAsync(PlayerArgs args)
    {
        Item           = args.Item;
        MediaSource    = args.MediaSource;
        StreamUrl      = args.StreamUrl;
        StartTicks     = args.StartTicks;
        _mediaSourceId = args.MediaSourceId;
        _sessionId     = args.SessionId;
        _currentTicks  = args.StartTicks;
        _hasStopped    = false;
        ItemTitle      = args.Item.DisplayTitle;

        // Subtitle & series context
        if (args.Item.IsMusic)
        {
            ItemSubtitle = args.Item.AlbumArtist ?? (!string.IsNullOrEmpty(args.Item.Album) ? args.Item.Album : "");
        }
        else if (args.Item.Type == "Episode")
        {
            var epName = MediaItem.CleanTitle(args.Item.Name);
            ItemSubtitle = !string.IsNullOrWhiteSpace(epName) ? epName : (args.Item.SeriesName ?? "");
        }
        else if (args.Item.Type == "Season")
        {
            ItemSubtitle = args.Item.SeasonDisplayName;
        }
        else
        {
            ItemSubtitle = args.Item.ProductionYear.HasValue && args.Item.ProductionYear.Value > 0
                ? args.Item.ProductionYear.Value.ToString()
                : "";
        }

        // Poster / Artwork for Loading Card & Overlay
        if (args.Item.IsMusic)
        {
            PosterUrl = args.Item.ImageTags?.Primary != null ? _api.GetImageUrl(args.Item.Id, "Primary", args.Item.ImageTags.Primary, 300, 300) :
                        (!string.IsNullOrEmpty(args.Item.AlbumId) ? _api.GetImageUrl(args.Item.AlbumId, "Primary", null, 300, 300) :
                        (args.Item.ImageTags?.Thumb != null ? _api.GetImageUrl(args.Item.Id, "Thumb", args.Item.ImageTags.Thumb, 300, 300) :
                        (!string.IsNullOrEmpty(args.Item.ParentId) ? _api.GetImageUrl(args.Item.ParentId, "Primary", null, 300, 300) : null)));
        }
        else
        {
            PosterUrl = args.Item.ImageTags?.Primary != null ? _api.GetImageUrl(args.Item.Id, "Primary", args.Item.ImageTags.Primary, 300) :
                        (args.Item.ImageTags?.Thumb != null ? _api.GetImageUrl(args.Item.Id, "Thumb", args.Item.ImageTags.Thumb, 300) :
                        (!string.IsNullOrEmpty(args.Item.SeriesId) ? _api.GetImageUrl(args.Item.SeriesId, "Primary", null, 300) : null));
        }

        OnPropertyChanged(nameof(MiniPosterWidth));

        ExtractBadges();
        PopulateOptions();

        await _api.ReportStartAsync(Item.Id, _mediaSourceId, _sessionId);

        // Fetch intro and outro segments asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                var segs = await _api.GetMediaSegmentsAsync(Item.Id, Item);
                _segments = segs;
            }
            catch { }
        });

        _progressTimer = new System.Threading.Timer(
            async _ =>
            {
                if (Item != null && !string.IsNullOrEmpty(_sessionId))
                {
                    await _api.ReportProgressAsync(Item.Id, _mediaSourceId, _currentTicks, IsPaused, _sessionId, "timeupdate");
                }
            },
            null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private void ExtractBadges()
    {
        var streams = MediaSource?.MediaStreams ?? Item?.MediaStreams ?? Array.Empty<MediaStream>();
        var videoStream = streams.FirstOrDefault(s => string.Equals(s.Type, "Video", StringComparison.OrdinalIgnoreCase));
        var audioStream = streams.FirstOrDefault(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase) && (s.Index == SelectedAudioIndex || s.IsDefault))
                       ?? streams.FirstOrDefault(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase));

        if (videoStream != null)
        {
            int h = videoStream.Height ?? 0;
            int w = videoStream.Width ?? 0;
            if (h >= 2000 || w >= 3800) ResolutionBadge = "4K UHD";
            else if (h >= 1000 || w >= 1900) ResolutionBadge = "1080p";
            else if (h >= 700 || w >= 1200) ResolutionBadge = "720p";
            else if (h > 0) ResolutionBadge = $"{h}p";
            else ResolutionBadge = "HD";

            VideoCodecBadge = (videoStream.Codec ?? "H264").ToUpperInvariant();

            var vRange = (videoStream.VideoRange ?? "").ToUpperInvariant();
            if (vRange.Contains("DOVI") || vRange.Contains("VISION")) HdrBadge = "DOVI";
            else if (vRange.Contains("HDR10")) HdrBadge = "HDR10";
            else if (vRange.Contains("HDR")) HdrBadge = "HDR";
            else HdrBadge = "";
        }

        if (audioStream != null)
        {
            int channels = audioStream.Channels ?? 2;
            if (channels >= 8) AudioBadge = "7.1 CH";
            else if (channels >= 6) AudioBadge = "5.1 CH";
            else AudioBadge = "STEREO";
        }
        else
        {
            AudioBadge = "STEREO";
        }

        PlayMethodBadge = (MediaSource?.SupportsDirectPlay ?? true) && !BurnInSubtitles ? "DIRECT PLAY" : "TRANSCODING";
        IsTranscoding = PlayMethodBadge == "TRANSCODING";
    }

    private void PopulateOptions()
    {
        // 1. Audio Tracks
        AudioTracks.Clear();
        var streams = MediaSource?.MediaStreams ?? Item?.MediaStreams ?? Array.Empty<MediaStream>();
        var audioStreams = streams.Where(s => string.Equals(s.Type, "Audio", StringComparison.OrdinalIgnoreCase)).ToList();

        int defaultAudioIdx = MediaSource?.DefaultAudioStreamIndex ?? audioStreams.FirstOrDefault(s => s.IsDefault)?.Index ?? (audioStreams.Count > 0 ? audioStreams[0].Index : 0);
        SelectedAudioIndex = defaultAudioIdx;

        foreach (var a in audioStreams)
        {
            var title = !string.IsNullOrWhiteSpace(a.DisplayTitle) ? a.DisplayTitle :
                        $"{(!string.IsNullOrEmpty(a.Language) ? a.Language.ToUpperInvariant() : "Audio")} ({a.Codec})";
            AudioTracks.Add(new TrackOptionItem
            {
                Index = a.Index,
                Title = title,
                IsSelected = a.Index == SelectedAudioIndex
            });
        }

        // 2. Subtitles
        SubtitleTracks.Clear();
        int? defaultSubIdx = MediaSource?.DefaultSubtitleStreamIndex;
        bool hasDefaultSub = defaultSubIdx.HasValue && defaultSubIdx.Value >= 0;

        SubtitleTracks.Add(new TrackOptionItem { Index = -1, Title = "Off", IsSelected = !hasDefaultSub });
        SelectedSubtitleIndex = hasDefaultSub ? defaultSubIdx.Value : -1;

        var subStreams = streams.Where(s => string.Equals(s.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var sub in subStreams)
        {
            var title = !string.IsNullOrWhiteSpace(sub.DisplayTitle) ? sub.DisplayTitle :
                        $"{(!string.IsNullOrEmpty(sub.Language) ? sub.Language.ToUpperInvariant() : "Subtitle")} ({sub.Codec})";
            bool isDef = defaultSubIdx.HasValue && defaultSubIdx.Value == sub.Index;
            bool isImg = string.Equals(sub.Codec, "pgs", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sub.Codec, "pgssub", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sub.Codec, "vobsub", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sub.Codec, "dvd_subtitle", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sub.Codec, "dvb_subtitle", StringComparison.OrdinalIgnoreCase);

            SubtitleTracks.Add(new TrackOptionItem
            {
                Index = sub.Index,
                Title = title,
                Codec = sub.Codec ?? "",
                IsImageBased = isImg,
                IsSelected = isDef
            });
        }

        // 3. Quality Options
        QualityOptions.Clear();
        long currentBitrate = App.Settings.MaxStreamingBitrate;
        SelectedBitrate = currentBitrate;
        var qList = new (string title, long bps)[]
        {
            ("Direct Play (Maximum)", 0),
            ("4K — 60 Mbps", 60000000),
            ("1080p — 20 Mbps", 20000000),
            ("1080p — 10 Mbps", 10000000),
            ("720p — 4 Mbps", 4000000),
            ("480p — 1.5 Mbps", 1500000)
        };
        foreach (var (title, bps) in qList)
        {
            QualityOptions.Add(new QualityOptionItem
            {
                Bitrate = bps,
                Title = title,
                IsSelected = bps == currentBitrate
            });
        }

        // 4. Stretch / Letterbox Options
        StretchOptions.Clear();
        SelectedStretch = Stretch.Uniform;
        StretchOptions.Add(new StretchOptionItem { StretchMode = Stretch.Uniform, Title = "Fit (Letterbox)", IsSelected = true });
        StretchOptions.Add(new StretchOptionItem { StretchMode = Stretch.UniformToFill, Title = "Fill / Zoom Screen", IsSelected = false });
        StretchOptions.Add(new StretchOptionItem { StretchMode = Stretch.Fill, Title = "Stretch to Screen", IsSelected = false });
    }

    private void SelectAudioTrack(TrackOptionItem? track)
    {
        if (track == null || track.Index == SelectedAudioIndex) return;
        SelectedAudioIndex = track.Index;
        foreach (var t in AudioTracks) t.IsSelected = t.Index == track.Index;
        CloseMenus();
        StreamReloadRequested?.Invoke();
    }

    private void SelectSubtitleTrack(TrackOptionItem? track)
    {
        if (track == null) return;
        SelectedSubtitleIndex = track.Index;
        foreach (var t in SubtitleTracks) t.IsSelected = t.Index == track.Index;
        OnPropertyChanged(nameof(HasSubtitlesBadge));
        CloseMenus();

        bool burnIn = BurnInSubtitles || track.IsImageBased;
        SubtitleTrackSelected?.Invoke(track.Index, burnIn);
    }

    public void UpdateBufferingProgress(double pct)
    {
        BufferingProgressValue = Math.Round(pct * 100.0, 1);
        HasBufferingPercent = pct > 0 && pct < 1.0;
        IsIndeterminate = pct <= 0 || pct >= 1.0;
        BufferingPercentText = $"{Math.Round(pct * 100.0)}%";
        if (pct > 0 && pct < 1.0)
        {
            BufferingText = $"Buffering... {BufferingPercentText}";
        }
    }

    public void UpdateDownloadProgress(double dl)
    {
        BufferPosition = Math.Clamp(dl * 1000.0, 0, 1000.0);
        BufferPercent = Math.Clamp(dl * 100.0, 0, 100.0);
    }

    public void TriggerCenterHud(string icon, string text)
    {
        CenterHudIcon = icon;
        CenterHudText = text;
        ShowCenterHud = true;
        _hudTimer?.Dispose();
        _hudTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        ShowCenterHud = false;
                    });
            }
            catch { }
        }, null, 900, System.Threading.Timeout.Infinite);
    }

    private void AdjustSubtitleOffset(double deltaSeconds)
    {
        SubtitleOffsetSeconds = Math.Round(SubtitleOffsetSeconds + deltaSeconds, 2);
        SubtitleOffsetChanged?.Invoke(deltaSeconds);
    }

    private void SetSubtitleOffset(double seconds)
    {
        var delta = seconds - SubtitleOffsetSeconds;
        SubtitleOffsetSeconds = seconds;
        if (delta != 0) SubtitleOffsetChanged?.Invoke(delta);
    }

    private void SelectQuality(QualityOptionItem? opt)
    {
        if (opt == null || opt.Bitrate == SelectedBitrate) return;
        SelectedBitrate = opt.Bitrate;
        foreach (var q in QualityOptions) q.IsSelected = q.Bitrate == opt.Bitrate;
        CloseMenus();
        StreamReloadRequested?.Invoke();
    }

    private void SelectStretch(StretchOptionItem? opt)
    {
        if (opt == null) return;
        SelectedStretch = opt.StretchMode;
        foreach (var s in StretchOptions) s.IsSelected = s.StretchMode == opt.StretchMode;
        CloseMenus();
        StretchChanged?.Invoke(opt.StretchMode);
    }

    public void UpdatePosition(long positionTicks, long durationTicks)
    {
        _totalDurationTicks = durationTicks;
        _currentTicks       = positionTicks;

        if (!_isScrubbing)
        {
            SliderPosition  = durationTicks > 0 ? (double)positionTicks / durationTicks * 1000.0 : 0;
            ProgressPercent = durationTicks > 0 ? (double)positionTicks / durationTicks * 100.0 : 0;
            TimeDisplay     = $"{FormatTicks(positionTicks)} / {FormatTicks(durationTicks)}";

            if (BufferPosition < SliderPosition)
            {
                BufferPosition = Math.Min(1000.0, SliderPosition + 50.0);
                BufferPercent = BufferPosition / 10.0;
            }
        }

        // Clock & Ends-At time
        CurrentClockText = DateTime.Now.ToString("HH:mm");
        if (durationTicks > 0)
        {
            var remainingTicks = durationTicks - positionTicks;
            if (remainingTicks > 0)
            {
                TimeRemainingText = $"-{FormatTicks(remainingTicks)}";
                var endTime = DateTime.Now.Add(TimeSpan.FromTicks(remainingTicks));
                EndsAtText = $"Ends at {endTime:HH:mm}";
            }
            else
            {
                TimeRemainingText = "0:00";
                EndsAtText = "";
            }
        }

        // Segment detection (Intro / Outro)
        var cur = TimeSpan.FromTicks(positionTicks);

        var intro = _segments.FirstOrDefault(s => string.Equals(s.Type, "Intro", StringComparison.OrdinalIgnoreCase) && cur >= s.Start && cur < s.End);
        if (intro != null)
        {
            if (App.Settings.AutoSkipIntro && !_autoSkippedSegments.Contains(intro))
            {
                _autoSkippedSegments.Add(intro);
                SeekToRequested?.Invoke(intro.End.Ticks);
                ShowSkipIntro = false;
                _currentIntroSegment = null;
            }
            else
            {
                _currentIntroSegment = intro;
                ShowSkipIntro = true;
            }
        }
        else
        {
            ShowSkipIntro = false;
            _currentIntroSegment = null;
        }

        var outro = _segments.FirstOrDefault(s => (string.Equals(s.Type, "Outro", StringComparison.OrdinalIgnoreCase) || string.Equals(s.Type, "Credits", StringComparison.OrdinalIgnoreCase)) && cur >= s.Start && cur < s.End);
        if (outro != null)
        {
            if (App.Settings.AutoSkipOutro && !_autoSkippedSegments.Contains(outro))
            {
                _autoSkippedSegments.Add(outro);
                SeekToRequested?.Invoke(outro.End.Ticks);
                ShowSkipOutro = false;
                _currentOutroSegment = null;
            }
            else
            {
                _currentOutroSegment = outro;
                ShowSkipOutro = true;
            }
        }
        else
        {
            ShowSkipOutro = false;
            _currentOutroSegment = null;
        }
    }

    private void SkipIntro()
    {
        if (_currentIntroSegment != null)
        {
            _autoSkippedSegments.Add(_currentIntroSegment);
            SeekToRequested?.Invoke(_currentIntroSegment.End.Ticks);
            ShowSkipIntro = false;
            _currentIntroSegment = null;
        }
    }

    private void SkipOutro()
    {
        if (_currentOutroSegment != null)
        {
            _autoSkippedSegments.Add(_currentOutroSegment);
            SeekToRequested?.Invoke(_currentOutroSegment.End.Ticks);
            ShowSkipOutro = false;
            _currentOutroSegment = null;
        }
    }

    public void SeekBySeconds(int seconds) => SeekDelta(seconds * TimeSpan.TicksPerSecond);

    private void SeekDelta(long deltaTicks)
    {
        var target = _currentTicks + deltaTicks;
        if (target < 0) target = 0;
        if (_totalDurationTicks > 0 && target > _totalDurationTicks) target = _totalDurationTicks;
        _currentTicks = target;
        SeekToRequested?.Invoke(target);
        long secs = deltaTicks / TimeSpan.TicksPerSecond;
        TriggerCenterHud(secs > 0 ? "\uE793" : "\uE792", secs > 0 ? $"+{secs}s" : $"{secs}s");
        if (Item != null && !string.IsNullOrEmpty(_sessionId))
        {
            _ = _api.ReportProgressAsync(Item.Id, _mediaSourceId, target, IsPaused, _sessionId, "timeupdate");
        }
    }

    public void StartScrubbing()
    {
        IsScrubbing = true;
    }

    public void UpdateScrubbing(double sliderVal)
    {
        SliderPosition = sliderVal;
        if (_totalDurationTicks > 0)
        {
            var previewTicks = (long)(sliderVal / 1000.0 * _totalDurationTicks);
            TimeDisplay = $"{FormatTicks(previewTicks)} / {FormatTicks(_totalDurationTicks)}";
        }
    }

    public void FinishScrubbing(double sliderVal)
    {
        IsScrubbing = false;
        SliderPosition = sliderVal;
        if (_totalDurationTicks > 0)
        {
            var targetTicks = (long)(sliderVal / 1000.0 * _totalDurationTicks);
            _currentTicks = targetTicks;
            SeekToRequested?.Invoke(targetTicks);
            if (Item != null && !string.IsNullOrEmpty(_sessionId))
            {
                _ = _api.ReportProgressAsync(Item.Id, _mediaSourceId, targetTicks, IsPaused, _sessionId, "timeupdate");
            }
        }
    }

    private void TogglePause()
    {
        IsPaused = !IsPaused;
        PauseRequested?.Invoke(IsPaused);
        TriggerCenterHud(IsPaused ? "\uE769" : "\uE768", IsPaused ? "Paused" : "Playing");
        if (Item != null && !string.IsNullOrEmpty(_sessionId))
        {
            _ = _api.ReportProgressAsync(Item.Id, _mediaSourceId, _currentTicks, IsPaused, _sessionId, IsPaused ? "pause" : "unpause");
        }
    }

    public async Task OnStoppedAsync(long? finalTicks = null)
    {
        if (_hasStopped) return;
        _hasStopped = true;

        _progressTimer?.Dispose();
        _progressTimer = null;

        var ticks = finalTicks ?? _currentTicks;
        if (Item != null && !string.IsNullOrEmpty(_sessionId))
        {
            await _api.ReportStopAsync(Item.Id, _mediaSourceId, ticks, _sessionId);
        }
    }

    private async Task GoBackAsync()
    {
        await OnStoppedAsync();
        App.Navigation.GoBack();
    }

    private static string FormatTicks(long ticks)
    {
        var ts = TimeSpan.FromTicks(ticks);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}

public class TrackOptionItem : ObservableBase
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public string Codec { get; set; } = "";
    public bool IsImageBased { get; set; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public class QualityOptionItem : ObservableBase
{
    public long Bitrate { get; set; }
    public string Title { get; set; } = "";
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public class StretchOptionItem : ObservableBase
{
    public Stretch StretchMode { get; set; }
    public string Title { get; set; } = "";
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

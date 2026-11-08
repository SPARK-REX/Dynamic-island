// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class MainWindow
{
    private TimeSpan _currentPosition = TimeSpan.FromSeconds(102); // 1:42 prototype default
    private TimeSpan _currentDuration = TimeSpan.FromSeconds(230); // 3:50 prototype default
    private bool _shuffleActive;
    private int _repeatMode; // 0: None, 1: List, 2: Track
    private readonly DispatcherTimer _mediaProgressTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private async Task InitMediaAsync()
    {
        _mediaService.TrackInfoChanged += info => Dispatcher.InvokeAsync(() => ApplyTrackInfo(info));
        _mediaService.PlaybackStatusChanged += isPlaying => Dispatcher.InvokeAsync(() => ApplyPlaybackStatus(isPlaying));
        _mediaService.TimelineChanged += (pos, dur) => Dispatcher.InvokeAsync(() => ApplyTimeline(pos, dur));
        _mediaService.NeedRebind += () => Dispatcher.InvokeAsync(() => _mediaService.Rebind());

        _audioService.VolumeChanged += (vol, muted) => Dispatcher.InvokeAsync(() =>
        {
            UpdateOutputDevicePill();
            UpdateMusicVolumeSliderUi(vol, muted);
        });

        _mediaProgressTimer.Tick += (_, _) => OnProgressTimerTick();
        _mediaProgressTimer.Start();

        UpdateOutputDevicePill();
        UpdateProgressUi();
        UpdateMusicVolumeSliderUi(_audioService.Volume, _audioService.IsMuted);

        _mediaService.LoadLast();
        await _mediaService.InitializeAsync();
    }

    private void Watchdog()
    {
        _mediaService.Watchdog();
        EnforceTopmost();
    }

    private void ApplyPlaybackStatus(bool isPlaying)
    {
        bool wasPlaying = _playing;
        _playing = isPlaying;
        SetPlayGlyph(isPlaying);
        if (!isPlaying && !_mediaService.HasMedia)
        {
            if (_mode != Mode.Volume) Go(Rest());
        }
        else
        {
            SyncRest();
        }
        UpdateBars();

        if (isPlaying && !wasPlaying && _mediaService.HasMedia)
        {
            _view = (int)IslandLogic.ExpandedView.Music;
            ApplyView();
            Go(Mode.Media, 4.5);
        }
    }

    private void ApplyTrackInfo(MediaTrackInfo info)
    {
        bool wasPlaying = _playing;
        _playing = info.IsPlaying;
        SetPlayGlyph(info.IsPlaying);

        TitleText.Text = string.IsNullOrWhiteSpace(info.Title) ? "Starboy" : info.Title;
        TitleMini.Text = string.IsNullOrWhiteSpace(info.Title) ? "Music" : info.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(info.Artist) ? "The Weeknd ft. Daft Punk" : info.Artist;

        if (info.Artwork != null)
        {
            Art.ImageSource = ArtMini.ImageSource = info.Artwork;
            if (ArtRect != null) ArtRect.Visibility = Visibility.Visible;
        }
        else
        {
            Art.ImageSource = ArtMini.ImageSource = null;
            if (ArtRect != null) ArtRect.Visibility = Visibility.Collapsed;
        }

        SyncRest();

        if (info.IsPlaying && (info.TrackChanged || !wasPlaying))
        {
            _view = (int)IslandLogic.ExpandedView.Music;
            ApplyView();
            Go(Mode.Media, 4.5);
        }
    }

    private void ApplyTimeline(TimeSpan position, TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            _currentDuration = duration;
            _currentPosition = position;
            UpdateProgressUi(0.2);
        }
    }

    private void OnProgressTimerTick()
    {
        if (_playing && _currentDuration > TimeSpan.Zero)
        {
            _currentPosition += TimeSpan.FromSeconds(1);
            if (_currentPosition > _currentDuration) _currentPosition = _currentDuration;
            UpdateProgressUi(0.4);
        }
    }

    private void UpdateProgressUi(double duration = 0.35)
    {
        if (ElapsedText == null || DurationText == null || ProgressFill == null) return;

        ElapsedText.Text = IslandLogic.FormatPlaybackTime(_currentPosition);
        var remaining = _currentDuration - _currentPosition;
        DurationText.Text = remaining > TimeSpan.Zero
            ? "-" + IslandLogic.FormatPlaybackTime(remaining)
            : IslandLogic.FormatPlaybackTime(_currentDuration);

        double ratio = IslandLogic.CalculateProgressRatio(_currentPosition, _currentDuration);
        double trackWidth = ProgressBarTrack?.ActualWidth > 0 ? ProgressBarTrack.ActualWidth : (ProgressBarContainer?.ActualWidth > 0 ? ProgressBarContainer.ActualWidth : 200.0);
        double targetWidth = IslandLogic.CalculateProgressWidth(ratio, trackWidth);

        if (duration > 0.001 && !Gsap.Reduced)
        {
            double curWidth = double.IsNaN(ProgressFill.Width) ? 0.0 : ProgressFill.Width;
            Gsap.Num(ProgressFill, "prog_fill", curWidth, targetWidth, duration, Ease.PowerOut(3),
                v => ProgressFill.Width = Math.Max(0, v));
        }
        else
        {
            ProgressFill.Width = targetWidth;
        }
    }

    private void UpdateOutputDevicePill()
    {
        if (OutputDeviceText != null)
        {
            OutputDeviceText.Text = IslandLogic.FormatAudioDeviceName(_audioService.CurrentDeviceName);
        }
    }

    // Play/Pause icon swaps with tactile pop animation and optical centering
    private void SetPlayGlyph(bool playing)
    {
        string g = IslandLogic.PlayPauseGlyph(playing);
        PlayGlyph.Margin = IslandLogic.PlayPauseOpticalMargin(playing);
        if (PlayGlyph.Text == g) return;
        PlayGlyph.Text = g;
        Fx.Pop(PlayPauseBtn ?? (FrameworkElement)PlayGlyph, 0.4);
    }

    private void OnPrev(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) Fx.Pop(fe, 0.3);
        _mediaService.Previous();
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        // Immediate optimistic UI response with tactile pop animation
        _playing = !_playing;
        SetPlayGlyph(_playing);
        UpdateBars();
        _mediaService.TogglePlayPause();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) Fx.Pop(fe, 0.3);
        _mediaService.Next();
    }

    private async void OnShuffle(object sender, RoutedEventArgs e)
    {
        _shuffleActive = !_shuffleActive;
        ShuffleGlyph.Foreground = _shuffleActive
            ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        if (ShuffleBtn != null) Fx.Pop(ShuffleBtn, 0.3);
        await _mediaService.ToggleShuffleAsync();
    }

    private async void OnRepeat(object sender, RoutedEventArgs e)
    {
        _repeatMode = (_repeatMode + 1) % 3;
        RepeatGlyph.Foreground = _repeatMode > 0
            ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        // E8ED = Repeat One, E8EE = Repeat All in Segoe Fluent Icons
        RepeatGlyph.Text = _repeatMode == 2 ? "\uE8ED" : "\uE8EE";
        if (RepeatBtn != null) Fx.Pop(RepeatBtn, 0.3);
        await _mediaService.CycleRepeatAsync();
    }

    private void OnOutputDeviceClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (OutputDevicePill != null) Fx.Pop(OutputDevicePill, 0.3);
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private void OnProgressMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ProgressBarContainer == null) return;
        ProgressBarContainer.CaptureMouse();
        HandleScrub(e.GetPosition(ProgressBarContainer).X, duration: 0.18);
        e.Handled = true;
    }

    private void OnProgressMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ProgressBarContainer != null)
        {
            HandleScrub(e.GetPosition(ProgressBarContainer).X, duration: 0.05);
            e.Handled = true;
        }
    }

    private void OnProgressMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ProgressBarContainer != null && ProgressBarContainer.IsMouseCaptured)
        {
            ProgressBarContainer.ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void HandleScrub(double clickX, double duration = 0.2)
    {
        double trackWidth = ProgressBarTrack?.ActualWidth > 0 ? ProgressBarTrack.ActualWidth : 360.0;
        _currentPosition = IslandLogic.CalculateScrubTime(clickX, trackWidth, _currentDuration);
        double ratio = IslandLogic.CalculateProgressRatio(_currentPosition, _currentDuration);
        UpdateProgressUi(duration);
        _ = _mediaService.SeekToRatioAsync(ratio);
    }

    public void UpdateMusicVolumeSliderUi(float volume, bool muted, double duration = 0.28)
    {
        if (MusicVolSliderContainer == null || MusicVolFill == null || MusicVolThumb == null) return;

        float effectiveVol = muted ? 0.0f : Math.Clamp(volume, 0.0f, 1.0f);
        double trackWidth = MusicVolTrack?.ActualWidth > 0 ? MusicVolTrack.ActualWidth : 76.0;
        double thumbWidth = MusicVolThumb.ActualWidth > 0 ? MusicVolThumb.ActualWidth : 10.0;

        double targetFillWidth = IslandLogic.CalculateVolumeFillWidth(effectiveVol, trackWidth);
        double targetThumbLeft = IslandLogic.CalculateVolumeThumbLeft(effectiveVol, trackWidth, thumbWidth);

        if (duration > 0.001 && !Gsap.Reduced)
        {
            double curFill = double.IsNaN(MusicVolFill.Width) ? 0.0 : MusicVolFill.Width;
            double curThumb = MusicVolThumb.Margin.Left;

            Gsap.Num(MusicVolFill, "vol_fill", curFill, targetFillWidth, duration, Ease.PowerOut(3),
                v => MusicVolFill.Width = Math.Max(0, v));

            Gsap.Num(MusicVolThumb, "vol_thumb", curThumb, targetThumbLeft, duration, Ease.PowerOut(3),
                v => MusicVolThumb.Margin = new Thickness(Math.Max(0, v), 0, 0, 0));
        }
        else
        {
            MusicVolFill.Width = targetFillWidth;
            MusicVolThumb.Margin = new Thickness(targetThumbLeft, 0, 0, 0);
        }

        if (MusicVolMinGlyph != null)
        {
            MusicVolMinGlyph.Text = muted || effectiveVol <= 0.0f ? "\uE74F" : "\uE992";
            MusicVolMinGlyph.Foreground = muted
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A))
                : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
        }
    }

    private void OnVolMouseEnter(object sender, MouseEventArgs e)
    {
        if (MusicVolThumbScale != null && !Gsap.Reduced)
        {
            Gsap.Num(MusicVolThumbScale, "scale", MusicVolThumbScale.ScaleX, 1.25, 0.2, Ease.BackOut(2.0),
                v => MusicVolThumbScale.ScaleX = MusicVolThumbScale.ScaleY = v);
        }
    }

    private void OnVolMouseLeave(object sender, MouseEventArgs e)
    {
        if (MusicVolSliderContainer != null && MusicVolSliderContainer.IsMouseCaptured) return;
        if (MusicVolThumbScale != null && !Gsap.Reduced)
        {
            Gsap.Num(MusicVolThumbScale, "scale", MusicVolThumbScale.ScaleX, 1.0, 0.25, Ease.PowerOut(3),
                v => MusicVolThumbScale.ScaleX = MusicVolThumbScale.ScaleY = v);
        }
    }

    private void OnVolMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (MusicVolSliderContainer == null) return;
        MusicVolSliderContainer.CaptureMouse();
        if (MusicVolThumbScale != null && !Gsap.Reduced)
        {
            Gsap.Num(MusicVolThumbScale, "scale", MusicVolThumbScale.ScaleX, 1.3, 0.15, Ease.BackOut(2.2),
                v => MusicVolThumbScale.ScaleX = MusicVolThumbScale.ScaleY = v);
        }
        HandleVolumeScrub(e.GetPosition(MusicVolSliderContainer).X, duration: 0.18);
        e.Handled = true;
    }

    private void OnVolMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && MusicVolSliderContainer != null)
        {
            HandleVolumeScrub(e.GetPosition(MusicVolSliderContainer).X, duration: 0.05);
            e.Handled = true;
        }
    }

    private void OnVolMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (MusicVolSliderContainer != null && MusicVolSliderContainer.IsMouseCaptured)
        {
            MusicVolSliderContainer.ReleaseMouseCapture();
        }
        if (MusicVolThumbScale != null && !Gsap.Reduced)
        {
            Gsap.Num(MusicVolThumbScale, "scale", MusicVolThumbScale.ScaleX, 1.0, 0.25, Ease.PowerOut(3),
                v => MusicVolThumbScale.ScaleX = MusicVolThumbScale.ScaleY = v);
        }
        e.Handled = true;
    }

    private void OnVolMouseWheel(object sender, MouseWheelEventArgs e)
    {
        float delta = e.Delta > 0 ? 0.05f : -0.05f;
        float newVol = Math.Clamp(_audioService.Volume + delta, 0.0f, 1.0f);
        _audioService.Volume = newVol;
        if (_audioService.IsMuted && newVol > 0) _audioService.IsMuted = false;
        UpdateMusicVolumeSliderUi(_audioService.Volume, _audioService.IsMuted, duration: 0.25);
        e.Handled = true;
    }

    private void HandleVolumeScrub(double clickX, double duration = 0.2)
    {
        double trackWidth = MusicVolTrack?.ActualWidth > 0 ? MusicVolTrack.ActualWidth : 76.0;
        float ratio = IslandLogic.CalculateVolumeRatio(clickX, trackWidth);
        _audioService.Volume = ratio;
        if (_audioService.IsMuted && ratio > 0) _audioService.IsMuted = false;
        UpdateMusicVolumeSliderUi(_audioService.Volume, _audioService.IsMuted, duration);
    }

    private void OnVolMinClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) Fx.Pop(fe, 0.3);
        _audioService.ToggleMute();
        UpdateMusicVolumeSliderUi(_audioService.Volume, _audioService.IsMuted, duration: 0.35);
    }

    private void OnVolMaxClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) Fx.Pop(fe, 0.3);
        _audioService.Volume = 1.0f;
        if (_audioService.IsMuted) _audioService.IsMuted = false;
        UpdateMusicVolumeSliderUi(_audioService.Volume, _audioService.IsMuted, duration: 0.35);
    }

    private void OnCenterDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ResetPosition(); return; }
        if (e.ButtonState != MouseButtonState.Pressed) return;
        e.Handled = true;
        double l = Left, t = Top;
        DragMove();
        if (Left != l || Top != t)
        {
            SnapToEdge();
        }
    }

    private void OnOpenSourceClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe) Fx.Pop(fe, 0.3);
        OpenSource();
        e.Handled = true;
    }

    private void OpenSource() => _mediaService.OpenSource();
}

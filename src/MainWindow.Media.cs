// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Threading.Tasks;
using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

public partial class MainWindow
{
    private async Task InitMediaAsync()
    {
        _mediaService.TrackInfoChanged += info => Dispatcher.InvokeAsync(() => ApplyTrackInfo(info));
        _mediaService.PlaybackStatusChanged += isPlaying => Dispatcher.InvokeAsync(() => ApplyPlaybackStatus(isPlaying));
        _mediaService.NeedRebind += () => Dispatcher.InvokeAsync(() => _mediaService.Rebind());

        _mediaService.LoadLast();
        await _mediaService.InitializeAsync();
    }

    private void Watchdog() => _mediaService.Watchdog();

    private void ApplyPlaybackStatus(bool isPlaying)
    {
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
    }

    private void ApplyTrackInfo(MediaTrackInfo info)
    {
        _playing = info.IsPlaying;
        SetPlayGlyph(info.IsPlaying);

        TitleText.Text = TitleMini.Text = info.Title;
        ArtistText.Text = info.Artist;
        Art.ImageSource = ArtMini.ImageSource = info.Artwork;

        SyncRest();

        if (info.TrackChanged && info.IsPlaying)
        {
            _view = (int)IslandLogic.ExpandedView.Music;
            ApplyView();
            Go(Mode.Media, 4);
        }
    }

    // play/pause icon swaps with a springy pop
    private void SetPlayGlyph(bool playing)
    {
        string g = IslandLogic.PlayPauseGlyph(playing);
        if (PlayGlyph.Text == g) return;
        PlayGlyph.Text = g;
        Fx.Pop(PlayGlyph, 0.4);
    }

    private void OnPrev(object sender, RoutedEventArgs e) => _mediaService.Previous();

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        // Immediate optimistic UI response with tactile pop animation
        _playing = !_playing;
        SetPlayGlyph(_playing);
        UpdateBars();
        _mediaService.TogglePlayPause();
    }

    private void OnNext(object sender, RoutedEventArgs e) => _mediaService.Next();

    private void OpenSource() => _mediaService.OpenSource();
}

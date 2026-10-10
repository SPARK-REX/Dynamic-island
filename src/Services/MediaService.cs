// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace DynamicIsland.Services;

public sealed record MediaTrackInfo(
    string Title,
    string Artist,
    BitmapImage? Artwork,
    bool IsPlaying,
    bool TrackChanged,
    string AppId
);

/// <summary>
/// Domain service managing Windows SMTC (System Media Transport Controls),
/// media metadata observation, artwork decoding, and safe playback source relaunch.
/// </summary>
public sealed class MediaService : IDisposable
{
    private sealed record LastTrack(string Title, string Artist, string AppId, string Exe);

    private static readonly string LastDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    private static readonly string LastJson = Path.Combine(LastDir, "last.json");
    private static readonly string LastPng = Path.Combine(LastDir, "last.png");

    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();

    private bool _initing;
    private int _gen;
    private string _lastBound = "";
    private string _lastKey = "";
    private string _lastAppId = "";
    private string _lastExe = "";
    private long _resumeUntil;
    private bool _disposed;

    public bool HasMedia { get; private set; }
    public bool IsPlaying { get; private set; }
    public string CurrentTitle { get; private set; } = "";
    public string CurrentArtist { get; private set; } = "";
    public BitmapImage? CurrentArtwork { get; private set; }

    public event Action<MediaTrackInfo>? TrackInfoChanged;
    public event Action<bool>? PlaybackStatusChanged;
    public event Action? NeedRebind;

    public async Task InitializeAsync()
    {
        if (_disposed || _initing || _mgr != null) return;
        _initing = true;
        try
        {
            for (int i = 0; i < 6 && _mgr == null && !_disposed; i++)
            {
                try
                {
                    var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    if (_disposed) return;

                    mgr.CurrentSessionChanged += OnSessionListChanged;
                    mgr.SessionsChanged += OnSessionListChanged;
                    _mgr = mgr;
                    Rebind();
                }
                catch (Exception ex)
                {
                    Program.Log(ex);
                    await Task.Delay(2000 * (i + 1));
                }
            }
        }
        finally
        {
            _initing = false;
        }
    }

    private void OnSessionListChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        if (!_disposed) NeedRebind?.Invoke();
    }

    public void Watchdog()
    {
        if (_disposed) return;
        try
        {
            if (_mgr == null)
            {
                _ = InitializeAsync();
                return;
            }
            if (!ReferenceEquals(Pick(), _session))
            {
                Rebind();
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private GlobalSystemMediaTransportControlsSession? Pick()
    {
        var cur = _mgr?.GetCurrentSession();
        var all = _mgr?.GetSessions();

        static bool Playing(GlobalSystemMediaTransportControlsSession x) =>
            x.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        if (cur != null && Playing(cur)) return cur;
        if (all != null)
        {
            foreach (var x in all)
            {
                if (Playing(x)) return x;
            }
        }

        // If currently bound session is still valid in sessions, retain it even when paused
        if (_session != null && all != null)
        {
            foreach (var x in all)
            {
                if (ReferenceEquals(x, _session) || x.SourceAppUserModelId == _session.SourceAppUserModelId)
                    return _session;
            }
        }

        return cur ?? (all != null && all.Count > 0 ? all[0] : null);
    }

    public void Rebind()
    {
        if (_disposed) return;
        try
        {
            if (_session != null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            foreach (var w in _watched) w.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _watched.Clear();

            _session = Pick();
            if (_session != null) _session.MediaPropertiesChanged += OnMediaPropertiesChanged;

            var all = _mgr?.GetSessions();
            if (all != null)
            {
                foreach (var x in all)
                {
                    x.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _watched.Add(x);
                }
            }

            var msg = $"bound: {_session?.SourceAppUserModelId ?? "none"} ({all?.Count ?? 0} sessions)";
            if (msg != _lastBound) Program.Log(_lastBound = msg);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            _session = null;
        }

        if (_session != null && Environment.TickCount64 < _resumeUntil && _session.SourceAppUserModelId == _lastAppId)
        {
            _resumeUntil = 0;
            _ = ResumeAsync(_session);
        }

        _ = RefreshAsync(props: true, announce: false);
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, object a)
    {
        if (!_disposed) _ = RefreshAsync(props: true, announce: true);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, object a)
    {
        if (_disposed) return;
        try
        {
            if (!ReferenceEquals(Pick(), _session))
            {
                Rebind();
                return;
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        _ = RefreshAsync(props: false, announce: false);
    }

    public async Task RefreshAsync(bool props, bool announce)
    {
        if (_disposed) return;
        var s = _session;
        if (s == null)
        {
            HasMedia = _lastKey != "";
            IsPlaying = false;
            PlaybackStatusChanged?.Invoke(false);
            return;
        }

        try
        {
            IsPlaying = s.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            HasMedia = true;
            PlaybackStatusChanged?.Invoke(IsPlaying);

            if (!props && _lastKey != "") return;

            int gen = ++_gen;
            GlobalSystemMediaTransportControlsSessionMediaProperties? p = null;
            for (int i = 0; i < 4; i++)
            {
                try { p = await s.TryGetMediaPropertiesAsync(); }
                catch (Exception ex) { Program.Log(ex); }
                if (s != _session || gen != _gen || _disposed) return;
                if (p != null && !string.IsNullOrWhiteSpace(p.Title)) break;
                await Task.Delay(400);
            }
            if (p == null || _disposed) return;

            var art = await LoadArtAsync(p.Thumbnail);
            if (art == null && !_disposed)
            {
                await Task.Delay(800);
                if (s != _session || gen != _gen || _disposed) return;
                try
                {
                    p = await s.TryGetMediaPropertiesAsync();
                    art = await LoadArtAsync(p?.Thumbnail);
                }
                catch (Exception ex) { Program.Log(ex); }
                if (s != _session || gen != _gen || _disposed) return;
            }

            CurrentTitle = string.IsNullOrWhiteSpace(p?.Title) ? "Unknown title" : p.Title;
            CurrentArtist = p?.Artist ?? "";
            CurrentArtwork = art;

            var key = $"{s.SourceAppUserModelId}|{CurrentTitle}|{CurrentArtist}";
            bool changed = key != _lastKey;
            _lastKey = key;

            if (!string.IsNullOrWhiteSpace(CurrentTitle))
            {
                var id = s.SourceAppUserModelId;
                _lastExe = FindExe(id) ?? (id == _lastAppId ? _lastExe : "");
                _lastAppId = id;
                SaveLast(CurrentTitle, CurrentArtist, art);
            }

            TrackInfoChanged?.Invoke(new MediaTrackInfo(
                CurrentTitle,
                CurrentArtist,
                CurrentArtwork,
                IsPlaying,
                announce && changed && IsPlaying,
                s.SourceAppUserModelId
            ));
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private static async Task<BitmapImage?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            var ms = new MemoryStream();
            await ras.AsStreamForRead().CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public void Previous() => _ = _session?.TrySkipPreviousAsync();

    public void TogglePlayPause()
    {
        if (_session != null)
        {
            _ = _session.TryTogglePlayPauseAsync();
        }
        else
        {
            ResumeLast();
        }
    }

    public void Next() => _ = _session?.TrySkipNextAsync();

    private void SaveLast(string title, string artist, BitmapImage? art)
    {
        try
        {
            Directory.CreateDirectory(LastDir);
            File.WriteAllText(LastJson, JsonSerializer.Serialize(new LastTrack(title, artist, _lastAppId, _lastExe)));
            if (art == null)
            {
                if (File.Exists(LastPng)) File.Delete(LastPng);
                return;
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(art));
            using var fs = File.Create(LastPng);
            enc.Save(fs);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    public void LoadLast()
    {
        try
        {
            if (!File.Exists(LastJson)) return;
            var l = JsonSerializer.Deserialize<LastTrack>(File.ReadAllText(LastJson));
            if (l == null || string.IsNullOrWhiteSpace(l.Title)) return;

            BitmapImage? img = null;
            if (File.Exists(LastPng))
            {
                img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.DecodePixelWidth = 128;
                img.UriSource = new Uri(LastPng);
                img.EndInit();
                img.Freeze();
            }

            CurrentTitle = l.Title;
            CurrentArtist = l.Artist;
            CurrentArtwork = img;
            _lastAppId = l.AppId;
            _lastExe = l.Exe;
            _lastKey = $"{l.AppId}|{l.Title}|{l.Artist}";
            HasMedia = true;

            TrackInfoChanged?.Invoke(new MediaTrackInfo(
                CurrentTitle,
                CurrentArtist,
                CurrentArtwork,
                IsPlaying: false,
                TrackChanged: false,
                AppId: _lastAppId
            ));
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private static string? FindExe(string id)
    {
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.ProcessName.Length >= 3 && id.Contains(p.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        var f = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(f) && File.Exists(f)) return f;
                    }
                }
                catch
                {
                    // Access denied for system / protected process
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        finally
        {
            if (processes != null)
            {
                foreach (var p in processes) p.Dispose();
            }
        }
        return null;
    }

    public void ResumeLast()
    {
        if (string.IsNullOrEmpty(_lastAppId)) return;
        _resumeUntil = Environment.TickCount64 + 30_000;
        OpenSource();
    }

    private static async Task ResumeAsync(GlobalSystemMediaTransportControlsSession s)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await Task.Delay(i == 0 ? 1500 : 2000);
                await s.TryPlayAsync();
                await Task.Delay(500);
                if (s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return;
            }
            catch (Exception ex)
            {
                Program.Log(ex);
            }
        }
    }

    public void OpenSource()
    {
        var id = _session?.SourceAppUserModelId;
        if (string.IsNullOrEmpty(id)) id = _lastAppId;
        if (string.IsNullOrEmpty(id)) return;

        try
        {
            IntPtr best = IntPtr.Zero;
            var song = CurrentTitle;

            NativeMethods.EnumWindows((h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                NativeMethods.GetWindowThreadProcessId(h, out var pid);
                if (pid == 0) return true;

                string procName = NativeMethods.GetProcessNameCached(pid);
                if (procName.Length < 3 || !id.Contains(procName, StringComparison.OrdinalIgnoreCase)) return true;

                var sb = new StringBuilder(256);
                NativeMethods.GetWindowText(h, sb, 256);
                if (sb.Length == 0) return true;

                bool hit = song.Length > 0 && sb.ToString().Contains(song, StringComparison.OrdinalIgnoreCase);
                if (best == IntPtr.Zero || hit) best = h;
                return !hit;
            }, IntPtr.Zero);

            if (best != IntPtr.Zero)
            {
                if (NativeMethods.IsIconic(best)) NativeMethods.ShowWindow(best, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(best);
                return;
            }

            if (!string.IsNullOrEmpty(_lastExe) &&
                Path.IsPathRooted(_lastExe) &&
                string.Equals(Path.GetExtension(_lastExe), ".exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_lastExe))
            {
                Process.Start(new ProcessStartInfo(_lastExe) { UseShellExecute = true });
                return;
            }

            if (id.Contains('!') || id.Contains('_'))
            {
                if (Regex.IsMatch(id, @"^[a-zA-Z0-9\._\-!]+$"))
                {
                    NativeMethods.StartPackagedApp(id);
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_mgr != null)
            {
                _mgr.CurrentSessionChanged -= OnSessionListChanged;
                _mgr.SessionsChanged -= OnSessionListChanged;
            }

            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            }

            foreach (var w in _watched)
            {
                w.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }
            _watched.Clear();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        finally
        {
            _session = null;
            _mgr = null;
        }
    }
}

// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DynamicIsland.Services;

/// <summary>
/// Focus timer domain service handling countdown progression, sound chimes, and lifecycle.
/// </summary>
public sealed class FocusTimerService : IDisposable
{
    private DateTime? _timerEnd;
    private TimeSpan _timerLen;
    private readonly DispatcherTimer _timerTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _disposed;

    public bool IsActive => _timerEnd != null;
    public TimeSpan Duration => _timerLen;

    public event Action<TimeSpan>? Tick;
    public event Action<int>? Completed;

    public FocusTimerService()
    {
        _timerTick.Tick += OnTick;
    }

    public void Start(int minutes)
    {
        if (_disposed) return;
        _timerLen = TimeSpan.FromMinutes(minutes);
        _timerEnd = DateTime.Now + _timerLen;
        _timerTick.Start();
        OnTick(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        _timerTick.Stop();
        _timerEnd = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _timerEnd is not { } end) return;
        var left = end - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            int totalMin = (int)_timerLen.TotalMinutes;
            Cancel();
            PlayChimeSound();
            Completed?.Invoke(totalMin);
            return;
        }

        left += TimeSpan.FromMilliseconds(999); // show initial minute clearly
        Tick?.Invoke(left);
    }

    public static void PlayChimeSound()
    {
        _ = Task.Run(() =>
        {
            try
            {
                string chimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "chimes.wav");
                if (File.Exists(chimePath))
                {
                    using var player = new SoundPlayer(chimePath);
                    player.PlaySync();
                }
                else
                {
                    SystemSounds.Exclamation.Play();
                }
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                SystemSounds.Asterisk.Play();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timerTick.Stop();
        _timerTick.Tick -= OnTick;
        _timerEnd = null;
    }
}

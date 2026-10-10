// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public partial class MainWindow
{
    private void InitVolume()
    {
        _audioService.VolumeChanged += (volume, muted) =>
            Dispatcher.InvokeAsync(() => ShowVolume(volume, muted));
        _audioService.Initialize();
    }

    private void ShowVolume(float volume, bool muted)
    {
        VolIcon.Text = IslandLogic.VolumeGlyph(volume, muted);
        int pct = IslandLogic.Percent(volume, muted);
        VolPct.Text = pct.ToString();

        Gsap.Num(VolFill, "w", VolFill.Width, VolTrack.ActualWidth * pct / 100.0, 0.35, Ease.PowerOut(4), v => VolFill.Width = Math.Max(0, v));
        Go(Mode.Volume, 1.5);
    }
}

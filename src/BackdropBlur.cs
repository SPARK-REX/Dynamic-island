// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DPixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// Real backdrop blur for the island. The window is hidden from screen capture (WDA_EXCLUDEFROMCAPTURE),
/// so a capture of its own rectangle is the desktop behind it. That image is shrunk (= blurred),
/// uploaded to a WriteableBitmap and used as the island fill, so the blur follows the animated shape exactly.
/// Needs Windows 10 2004+. If unsupported, the island stays solid black.
/// </summary>
internal sealed class BackdropBlur
{
    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    const int Shrink = 10;   // blur strength: capture is divided by this

    [DllImport("user32.dll")]
    static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    readonly Window _win;
    readonly Path _shape, _tint;
    readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
    ImageBrush? _brush;
    WriteableBitmap? _wb;
    Bitmap? _full, _small;
    int _pw, _ph;
    bool _active;

    public BackdropBlur(Window win, Path shape, Path tint)
    {
        _win = win; _shape = shape; _tint = tint;
        _timer.Tick += (_, _) => Refresh();
    }

    public bool Start()
    {
        try
        {
            var hwnd = new WindowInteropHelper(_win).Handle;
            if (hwnd == IntPtr.Zero || !SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)) return false;
            _active = true;
            Refresh();
            if (_brush == null) { _active = false; return false; }
            _shape.Fill = _brush;
            _tint.Visibility = Visibility.Visible;
            _timer.Start();
            return true;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            return false;
        }
    }

    /// <summary>Back to pure black; the island shows up in screenshots again.</summary>
    public void Stop()
    {
        _timer.Stop();
        _active = false;
        var hwnd = new WindowInteropHelper(_win).Handle;
        if (hwnd != IntPtr.Zero) SetWindowDisplayAffinity(hwnd, 0);
        _shape.Fill = System.Windows.Media.Brushes.Black;
        _tint.Visibility = Visibility.Collapsed;
    }

    public void Refresh()
    {
        if (!_active) return;
        if (!_win.IsVisible || _win.WindowState == WindowState.Minimized) return;
        try
        {
            var src = PresentationSource.FromVisual(_win);
            if (src?.CompositionTarget == null) return;
            var m = src.CompositionTarget.TransformToDevice;
            int pw = (int)Math.Round(_win.ActualWidth * m.M11), ph = (int)Math.Round(_win.ActualHeight * m.M22);
            if (pw < Shrink || ph < Shrink) return;
            int px = (int)Math.Round(_win.Left * m.M11), py = (int)Math.Round(_win.Top * m.M22);

            if (_full == null || pw != _pw || ph != _ph) Allocate(pw, ph);
            using (var g = Graphics.FromImage(_full!))
                g.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph),
                                 CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
            using (var g = Graphics.FromImage(_small!))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_full!, new System.Drawing.Rectangle(0, 0, _small!.Width, _small.Height));
            }

            var bd = _small!.LockBits(new System.Drawing.Rectangle(0, 0, _small.Width, _small.Height),
                                      ImageLockMode.ReadOnly, DPixelFormat.Format32bppPArgb);
            try
            {
                _wb!.WritePixels(new Int32Rect(0, 0, _small.Width, _small.Height), bd.Scan0, bd.Stride * _small.Height, bd.Stride);
            }
            finally { _small.UnlockBits(bd); }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    void Allocate(int pw, int ph)
    {
        _pw = pw; _ph = ph;
        _full?.Dispose(); _small?.Dispose();
        int sw = Math.Max(1, pw / Shrink), sh = Math.Max(1, ph / Shrink);
        _full = new Bitmap(pw, ph, DPixelFormat.Format32bppPArgb);
        _small = new Bitmap(sw, sh, DPixelFormat.Format32bppPArgb);
        _wb = new WriteableBitmap(sw, sh, 96, 96, PixelFormats.Pbgra32, null);
        _brush = new ImageBrush(_wb)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, _win.ActualWidth, _win.ActualHeight),
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
        RenderOptions.SetBitmapScalingMode(_brush, BitmapScalingMode.HighQuality);
        if (_active) _shape.Fill = _brush;
    }
}

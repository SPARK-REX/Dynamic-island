// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DynamicIsland;

/// <summary>
/// Ultra-fluid, high-performance focus-timer picker dialog.
/// Features a luxury multi-layer dark glass gradient background, zero-cost native radial aura bloom,
/// tactile spring button physics, glowing slider jewel thumb, and instantaneous drag responsiveness.
/// Enter = start, Esc = cancel.
/// </summary>
public partial class TimerDialog : Window
{
    private static readonly int[] Presets = { 15, 25, 45, 60, 90 };
    private const int Min = 1, Max = 180;

    // Shared static easing curves to eliminate per-interaction object allocations
    private static readonly BackEase SpringBack = new() { Amplitude = 0.38, EasingMode = EasingMode.EaseOut };
    private static readonly BackEase SnappyBack = new() { Amplitude = 0.45, EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase SmoothCubic = new() { EasingMode = EasingMode.EaseOut };

    private readonly Button[] _chips;
    private readonly ScaleTransform[] _chipScales;
    private readonly ScaleTransform _minusScale = new(1, 1);
    private readonly ScaleTransform _plusScale = new(1, 1);
    private readonly ScaleTransform _cancelScale = new(1, 1);
    private readonly ScaleTransform _startScale = new(1, 1);

    private int _min;
    private bool _dragging, _closing;

    /// <summary>Shows the dialog; returns chosen minutes or null if cancelled.</summary>
    public static int? Ask(int initial)
    {
        var d = new TimerDialog(initial);
        return d.ShowDialog() == true ? d._min : null;
    }

    private TimerDialog(int initial)
    {
        InitializeComponent();
        _chips = new[] { C0, C1, C2, C3, C4 };
        _chipScales = new ScaleTransform[_chips.Length];

        // Attach tactile spring transforms to chips
        for (int i = 0; i < _chips.Length; i++)
        {
            int v = Presets[i];
            var chip = _chips[i];
            var scale = new ScaleTransform(1, 1);
            _chipScales[i] = scale;
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            chip.RenderTransform = scale;

            chip.Content = v.ToString();
            chip.Click += (_, _) => SetMin(v);
            AutomationProperties.SetName(chip, $"{v} minutes");

            SetupScaleAnim(chip, scale, hoverScale: 1.07, pressScale: 0.93);
        }

        // Stepper buttons with spring recoil
        Minus.RenderTransformOrigin = new Point(0.5, 0.5);
        Minus.RenderTransform = _minusScale;
        SetupScaleAnim(Minus, _minusScale, hoverScale: 1.10, pressScale: 0.90);
        Minus.Click += (_, _) =>
        {
            BounceButton(_minusScale);
            SetMin(_min - 1);
        };

        Plus.RenderTransformOrigin = new Point(0.5, 0.5);
        Plus.RenderTransform = _plusScale;
        SetupScaleAnim(Plus, _plusScale, hoverScale: 1.10, pressScale: 0.90);
        Plus.Click += (_, _) =>
        {
            BounceButton(_plusScale);
            SetMin(_min + 1);
        };

        // Cancel button with glass hover lift & press recoil
        CancelBtn.RenderTransformOrigin = new Point(0.5, 0.5);
        CancelBtn.RenderTransform = _cancelScale;
        SetupScaleAnim(CancelBtn, _cancelScale, hoverScale: 1.04, pressScale: 0.94);
        CancelBtn.Click += (_, _) => Finish(false);

        // Start button with shimmer beam, glowing halo & play icon nudge
        StartBtn.RenderTransformOrigin = new Point(0.5, 0.5);
        StartBtn.RenderTransform = _startScale;
        SetupStartButtonAnim();
        StartBtn.Click += (_, _) => Finish(true);

        // Close button with rotation micro-interaction
        SetupCloseButtonAnim();
        CloseBtn.Click += (_, _) => Finish(false);

        // Slider interaction: high-speed, zero-jitter tracking
        TrackHit.MouseEnter += (_, _) =>
        {
            ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.24, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.24, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            ThumbHalo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140)));
        };

        TrackHit.MouseLeave += (_, _) =>
        {
            if (_dragging) return;
            ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            ThumbHalo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.7, TimeSpan.FromMilliseconds(160)));
        };

        TrackHit.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            TrackHit.CaptureMouse();
            ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.36, TimeSpan.FromMilliseconds(100)) { EasingFunction = SmoothCubic });
            ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.36, TimeSpan.FromMilliseconds(100)) { EasingFunction = SmoothCubic });
            FromMouse(e);
        };

        TrackHit.MouseMove += (_, e) =>
        {
            if (_dragging) FromMouse(e);
        };

        TrackHit.MouseLeftButtonUp += (_, _) =>
        {
            if (_dragging)
            {
                _dragging = false;
                TrackHit.ReleaseMouseCapture();
                double target = TrackHit.IsMouseOver ? 1.24 : 1.0;
                ThumbScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
                ThumbScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
                ThumbHalo.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(TrackHit.IsMouseOver ? 1.0 : 0.7, TimeSpan.FromMilliseconds(140)));
                TriggerTactileFeedback();
            }
        };

        TrackHit.SizeChanged += (_, _) => UpdateFill(false);

        // Draggable window card
        Card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // Keyboard navigation
        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter: Finish(true); break;
                case Key.Escape: Finish(false); break;
                case Key.Up: SetMin(_min + 1); break;
                case Key.Down: SetMin(_min - 1); break;
                case Key.Right: SetMin(_min + 5); break;
                case Key.Left: SetMin(_min - 5); break;
                default: return;
            }
            e.Handled = true;
        };

        MouseWheel += (_, e) => SetMin(_min + (e.Delta > 0 ? 5 : -5));

        Opacity = 0;
        _min = Math.Clamp(initial, Min, Max);
        Refresh(animateFill: false, isDragging: false);
        Loaded += (_, _) => { Activate(); Open(); };
    }

    private void SetupScaleAnim(UIElement elem, ScaleTransform scale, double hoverScale, double pressScale)
    {
        elem.MouseEnter += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(hoverScale, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(hoverScale, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
        };

        elem.MouseLeave += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
        };

        elem.PreviewMouseDown += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(pressScale, TimeSpan.FromMilliseconds(60)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(pressScale, TimeSpan.FromMilliseconds(60)));
        };

        elem.PreviewMouseUp += (_, _) =>
        {
            double target = elem.IsMouseOver ? hoverScale : 1.0;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
        };
    }

    private void SetupCloseButtonAnim()
    {
        CloseBtn.MouseEnter += (_, _) =>
        {
            CloseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.10, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            CloseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.10, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            CloseRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(90, TimeSpan.FromMilliseconds(180)) { EasingFunction = SpringBack });
        };

        CloseBtn.MouseLeave += (_, _) =>
        {
            CloseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            CloseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            CloseRot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
        };

        CloseBtn.PreviewMouseDown += (_, _) =>
        {
            CloseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.90, TimeSpan.FromMilliseconds(50)));
            CloseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.90, TimeSpan.FromMilliseconds(50)));
        };

        CloseBtn.PreviewMouseUp += (_, _) =>
        {
            double target = CloseBtn.IsMouseOver ? 1.10 : 1.0;
            CloseScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
            CloseScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
        };
    }

    private void SetupStartButtonAnim()
    {
        StartBtn.MouseEnter += (_, _) =>
        {
            // Lift with tactile spring
            _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });
            _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });

            // Intense solar bloom opacity boost (no expensive blur radius re-rasterization)
            StartGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(150)));

            // Play icon kinetic nudge forward
            StartIconShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(3.5, TimeSpan.FromMilliseconds(140)) { EasingFunction = SpringBack });

            // Smooth single-shot shimmer sweep
            TriggerShimmer(TimeSpan.FromMilliseconds(750));
        };

        StartBtn.MouseLeave += (_, _) =>
        {
            _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            StartIconShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = SmoothCubic });
            StartGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(160)));
        };

        StartBtn.PreviewMouseDown += (_, _) =>
        {
            _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(50)));
            _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(50)));
            StartGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(50)));
        };

        StartBtn.PreviewMouseUp += (_, _) =>
        {
            double target = StartBtn.IsMouseOver ? 1.04 : 1.0;
            _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
            _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = SpringBack });
            StartGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(StartBtn.IsMouseOver ? 0.85 : 0.55, TimeSpan.FromMilliseconds(120)));
        };
    }

    private void TriggerShimmer(TimeSpan duration, TimeSpan? beginTime = null)
    {
        var shimmer = new DoubleAnimation(-80, 260, duration)
        {
            BeginTime = beginTime,
            EasingFunction = SmoothCubic
        };
        StartShimmerShift.BeginAnimation(TranslateTransform.XProperty, shimmer);
    }

    private void BounceButton(ScaleTransform scale)
    {
        var bounce = new DoubleAnimation(0.90, 1.08, TimeSpan.FromMilliseconds(65))
        {
            AutoReverse = true,
            EasingFunction = SnappyBack
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
    }

    private void FromMouse(MouseEventArgs e)
    {
        double w = TrackHit.ActualWidth;
        if (w <= 0) return;
        double ratio = Math.Clamp(e.GetPosition(TrackHit).X / w, 0.0, 1.0);
        int v = IslandLogic.CalculateTimerFromRatio(ratio, Min, Max);
        SetMin(v, animateFill: false, isDragging: true);
    }

    private void SetMin(int v, bool animateFill = true, bool isDragging = false)
    {
        v = Math.Clamp(v, Min, Max);
        if (v == _min) return;
        _min = v;
        Refresh(animateFill, isDragging);

        if (!isDragging)
        {
            TriggerTactileFeedback();
        }
    }

    private void TriggerTactileFeedback()
    {
        // Tactile spring pop on number change + vertical odometer micro-slide
        var pop = new DoubleAnimation(1.14, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = SpringBack
        };
        NumScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        NumScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        var slide = new DoubleAnimation(-5, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = SpringBack
        };
        NumShift.BeginAnimation(TranslateTransform.YProperty, slide);

        // Ambient dial aura flare
        var flare = new DoubleAnimation(0.65, 0.30, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = SmoothCubic
        };
        NumAura.BeginAnimation(UIElement.OpacityProperty, flare);

        // Header badge joyful pulse
        var badgePulse = new DoubleAnimation(1.08, 1.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = SpringBack
        };
        HeaderBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, badgePulse);
        HeaderBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, badgePulse);
    }

    private void Refresh(bool animateFill, bool isDragging)
    {
        Num.Text = _min.ToString();
        EndsText.Text = IslandLogic.FormatTimerEndsAt(DateTime.Now, _min);

        for (int i = 0; i < _chips.Length; i++)
        {
            bool isActive = (Presets[i] == _min);
            bool wasActive = Equals(_chips[i].Tag, "Active");
            _chips[i].Tag = isActive ? "Active" : "Normal";

            if (isActive && !wasActive && !isDragging && _chipScales[i] != null)
            {
                var chipPop = new DoubleAnimation(1.12, 1.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = SpringBack
                };
                _chipScales[i].BeginAnimation(ScaleTransform.ScaleXProperty, chipPop);
                _chipScales[i].BeginAnimation(ScaleTransform.ScaleYProperty, chipPop);
            }
        }

        UpdateFill(animateFill);
    }

    private void UpdateFill(bool animate)
    {
        double target = IslandLogic.CalculateTimerFillWidth(TrackHit.ActualWidth, _min, Min, Max);
        if (animate)
        {
            Fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = SmoothCubic
            });
        }
        else
        {
            Fill.BeginAnimation(WidthProperty, null);
            Fill.Width = target;
        }
    }

    /// <summary>Smooth spring opening animation with blooming ambient aura. Zero idle loops.</summary>
    private void Open()
    {
        UpdateFill(false);

        var d = TimeSpan.FromMilliseconds(240);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = SpringBack });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = SpringBack });
        CardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, d) { EasingFunction = SmoothCubic });

        // Bloom ambient aura once cleanly
        AmbientAura.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(260)) { EasingFunction = SmoothCubic });

        // Welcoming sweep across primary CTA
        TriggerShimmer(TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Close with a snappy, elegant fade and shrink.</summary>
    private void Finish(bool ok)
    {
        if (_closing) return;
        _closing = true;

        var d = TimeSpan.FromMilliseconds(100);
        var fade = new DoubleAnimation(0, d) { EasingFunction = SmoothCubic };
        fade.Completed += (_, _) => DialogResult = ok;

        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, d) { EasingFunction = SmoothCubic });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, d) { EasingFunction = SmoothCubic });
        AmbientAura.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, d));
        BeginAnimation(OpacityProperty, fade);
    }
}

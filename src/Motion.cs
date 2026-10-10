// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

// GSAP-style motion for WPF: one shared ticker, tweens, timelines with positions, stagger,
// overwrite-by-key, and gsap's ease family (power / back / elastic / expo). UI thread only.

/// <summary>Ease functions: progress 0..1 in, eased progress out (may overshoot, like gsap back/elastic).</summary>
public static class Ease
{
    public static readonly Func<double, double> Linear = p => p;
    public static Func<double, double> PowerOut(int n) => p => 1 - Math.Pow(1 - p, n);
    public static Func<double, double> PowerInOut(int n) =>
        p => p < 0.5 ? Math.Pow(2 * p, n) / 2 : 1 - Math.Pow(2 * (1 - p), n) / 2;
    public static readonly Func<double, double> ExpoOut = p => p >= 1 ? 1 : 1 - Math.Pow(2, -10 * p);
    public static readonly Func<double, double> SineInOut = p => -(Math.Cos(Math.PI * p) - 1) / 2;

    /// <summary>gsap back.out(s). s=1.7 is gsap's default overshoot.</summary>
    public static Func<double, double> BackOut(double s = 1.70158) => p => { p -= 1; return p * p * ((s + 1) * p + s) + 1; };

    /// <summary>gsap elastic.out(amplitude, period).</summary>
    public static Func<double, double> ElasticOut(double amp = 1, double period = 0.3)
    {
        double a = Math.Max(1, amp);
        double s = period / (2 * Math.PI) * Math.Asin(1 / a);
        return p => p <= 0 ? 0 : p >= 1 ? 1 : a * Math.Pow(2, -10 * p) * Math.Sin((p - s) * (2 * Math.PI) / period) + 1;
    }
}

public class Tween
{
    internal (object, string)? Id;
    protected double T;
    protected bool Started;
    readonly double _delay, _dur;
    readonly Func<double, double> _ease;
    readonly Action<double>? _upd;
    readonly Action? _done;

    protected Tween() { _ease = Ease.Linear; }

    internal Tween(double dur, Func<double, double> ease, Action<double> upd, double delay, Action? done)
    { _dur = dur; _ease = ease; _upd = upd; _delay = delay; _done = done; }

    /// <summary>Advance by dt seconds. True when finished.</summary>
    internal virtual bool Advance(double dt)
    {
        T += dt;
        if (T < _delay) return false;
        double p = _dur <= 0 ? 1 : Math.Min((T - _delay) / _dur, 1);
        Started = true;
        _upd?.Invoke(_ease(p));
        if (p < 1) return false;
        _done?.Invoke();
        return true;
    }

    public void Kill() => Gsap.Remove(this);
}

/// <summary>Sequenced tweens on one clock. Position = absolute start (s); omit to append after the previous child.</summary>
public sealed class GsapTimeline : Tween
{
    sealed class Child
    {
        public double Start, Dur;
        public Func<double, double> Fn = DynamicIsland.Ease.Linear;
        public Action<double> Upd = _ => { };
        public double Last = -1;
    }

    readonly List<Child> _kids = new();
    readonly Action? _done;
    double _end, _lastStart;

    public GsapTimeline(Action? onComplete = null) { _done = onComplete; }

    public GsapTimeline To(double dur, Func<double, double> ease, Action<double> update, double? at = null)
    {
        double start = at ?? _end;
        _kids.Add(new Child { Start = start, Dur = dur, Fn = ease, Upd = update });
        _lastStart = start;
        _end = Math.Max(_end, start + dur);
        return this;
    }

    /// <summary>Add one child per item, each starting `each` seconds after the last (gsap stagger).</summary>
    public GsapTimeline Stagger(int count, double each, double dur, Func<double, double> ease, Action<int, double> update, double? at = null)
    {
        double start = at ?? _end;
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            To(dur, ease, p => update(idx, p), start + i * each);
        }
        return this;
    }

    /// <summary>Start time of the most recently added child ("&lt;" in gsap position syntax).</summary>
    public double LastStart => _lastStart;

    internal override bool Advance(double dt)
    {
        T += dt;
        bool all = true;
        foreach (var k in _kids)
        {
            if (T < k.Start) { all = false; continue; }
            double p = k.Dur <= 0 ? 1 : Math.Min((T - k.Start) / k.Dur, 1);
            if (p != k.Last) { k.Last = p; k.Upd(k.Fn(p)); }
            if (p < 1) all = false;
        }
        if (!all) return false;
        _done?.Invoke();
        return true;
    }
}

public static class Gsap
{
    static readonly List<Tween> Live = new();
    static readonly Dictionary<(object, string), Tween> Keys = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double _last;
    static bool _hooked;

    /// <summary>OS "animations off" setting: everything snaps to its end state.</summary>
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;

    /// <summary>Tween 0..1 (eased) into `update`. Same (owner,key) kills the running one first (gsap overwrite).</summary>
    public static Tween To(object owner, string key, double dur, Func<double, double> ease, Action<double> update,
                           double delay = 0, Action? onComplete = null)
        => Play(owner, key, new Tween(dur, ease, update, delay, onComplete));

    /// <summary>Tween a number from its current value. The live value is read at call time, so retargeting is seamless.</summary>
    public static Tween Num(object owner, string key, double from, double to, double dur, Func<double, double> ease,
                            Action<double> set, double delay = 0, Action? onComplete = null)
        => To(owner, key, dur, ease, p => set(from + (to - from) * p), delay, onComplete);

    public static Tween Play(object owner, string key, Tween tw)
    {
        if (Keys.TryGetValue((owner, key), out var old)) Remove(old);
        if (Reduced) { tw.Advance(1e6); return tw; }
        tw.Id = (owner, key);
        Keys[(owner, key)] = tw;
        Live.Add(tw);
        if (!_hooked)
        {
            _hooked = true;
            _last = Clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += Tick;
        }
        return tw;
    }

    internal static void Remove(Tween tw)
    {
        Live.Remove(tw);
        if (tw.Id is { } id && Keys.TryGetValue(id, out var cur) && ReferenceEquals(cur, tw)) Keys.Remove(id);
        tw.Id = null;
    }

    static void Tick(object? s, EventArgs e)
    {
        double now = Clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _last, 0.1);   // lag smoothing: a stall never skips an animation
        if (dt <= 0) return;
        _last = now;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            if (i >= Live.Count) continue;
            var tw = Live[i];
            if (tw.Advance(dt)) Remove(tw);
        }
        if (Live.Count == 0)
        {
            CompositionTarget.Rendering -= Tick;   // idle = zero cost
            _hooked = false;
        }
    }
}

/// <summary>Reusable effects built on Gsap. Transforms + opacity only (no layout).</summary>
public static class Fx
{
    static ScaleTransform ScaleOf(FrameworkElement e)
    {
        if (e.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1, 1);
            e.RenderTransform = st;
            e.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        return st;
    }

    static TranslateTransform MoveOf(FrameworkElement e)
    {
        if (e.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
        {
            tt = new TranslateTransform();
            e.RenderTransform = tt;
        }
        return tt;
    }

    /// <summary>Scale + fade in with a back.out overshoot.</summary>
    public static void Pop(FrameworkElement e, double from = 0.5, double delay = 0, double dur = 0.5)
    {
        var st = ScaleOf(e);
        st.ScaleX = st.ScaleY = from;
        e.Opacity = 0;
        Gsap.To(e, "pop", dur, Ease.BackOut(2.2), p =>
        {
            st.ScaleX = st.ScaleY = from + (1 - from) * p;
            e.Opacity = Math.Min(1, p * 1.6);
        }, delay);
    }

    /// <summary>Slide `dy` px to rest while fading in.</summary>
    public static void Reveal(FrameworkElement e, double dy, double delay = 0, double dur = 0.45)
    {
        var tt = MoveOf(e);
        tt.Y = dy;
        e.Opacity = 0;
        Gsap.To(e, "reveal", dur, Ease.PowerOut(3), p =>
        {
            tt.Y = dy * (1 - p);
            e.Opacity = p;
        }, delay);
    }

    /// <summary>Pop each item in turn (gsap stagger).</summary>
    public static void PopStagger(IReadOnlyList<FrameworkElement> items, double delay = 0, double each = 0.07)
    {
        for (int i = 0; i < items.Count; i++) Pop(items[i], 0.5, delay + i * each);
    }
}

/// <summary>Tactile hover/press/release (spring) on a Button's template root. Set via style.</summary>
public static class Tactile
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Tactile), new PropertyMetadata(false, OnEnabled));
    public static readonly DependencyProperty HoverScaleProperty = DependencyProperty.RegisterAttached(
        "HoverScale", typeof(double), typeof(Tactile), new PropertyMetadata(1.0));
    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(double), typeof(Tactile), new PropertyMetadata(0.9));

    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetHoverScale(DependencyObject d, double v) => d.SetValue(HoverScaleProperty, v);
    public static double GetHoverScale(DependencyObject d) => (double)d.GetValue(HoverScaleProperty);
    public static void SetPressScale(DependencyObject d, double v) => d.SetValue(PressScaleProperty, v);
    public static double GetPressScale(DependencyObject d) => (double)d.GetValue(PressScaleProperty);

    static readonly Func<double, double> Spring = Ease.ElasticOut(1, 0.4);
    static readonly Func<double, double> Settle = Ease.PowerOut(3);
    static readonly Func<double, double> Squish = Ease.PowerOut(2);

    static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button b) return;
        b.MouseEnter -= Enter; b.MouseLeave -= Leave;
        b.PreviewMouseLeftButtonDown -= Down; b.PreviewMouseLeftButtonUp -= Up;
        if (!(bool)e.NewValue) return;
        b.MouseEnter += Enter; b.MouseLeave += Leave;
        b.PreviewMouseLeftButtonDown += Down; b.PreviewMouseLeftButtonUp += Up;
    }

    // Template root is the scaled Border in all island button styles.
    static ScaleTransform? Scale(Button b)
    {
        if (!b.IsLoaded || VisualTreeHelper.GetChildrenCount(b) == 0) return null;
        if (VisualTreeHelper.GetChild(b, 0) is not Border bd) return null;
        if (bd.RenderTransform is not ScaleTransform st || st.IsFrozen)
        {
            st = new ScaleTransform(1, 1);
            bd.RenderTransform = st;
        }
        return st;
    }

    static void Go(Button b, double to, double dur, Func<double, double> ease)
    {
        var st = Scale(b);
        if (st == null) return;
        Gsap.Num(st, "s", st.ScaleX, to, dur, ease, v => st.ScaleX = st.ScaleY = v);
    }

    static void Enter(object s, System.Windows.Input.MouseEventArgs e) { var b = (Button)s; Go(b, GetHoverScale(b), 0.3, Settle); }
    static void Leave(object s, System.Windows.Input.MouseEventArgs e) { var b = (Button)s; Go(b, 1, 0.35, Settle); }
    static void Down(object s, System.Windows.Input.MouseButtonEventArgs e) { var b = (Button)s; Go(b, GetPressScale(b), 0.1, Squish); }
    static void Up(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        var b = (Button)s;
        Go(b, b.IsMouseOver ? GetHoverScale(b) : 1, 0.65, Spring);   // springy release
    }
}

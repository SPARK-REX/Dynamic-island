// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public partial class MainWindow
{
    private int _batteryPercent = 100;
    private bool _alwaysShowCharging;

    private static readonly Brush BatteryGreenBrush = CreateFrozenSolidBrush(0x30, 0xD1, 0x58);
    private static readonly Brush BatteryRedBrush = CreateFrozenSolidBrush(0xFF, 0x45, 0x3A);

    private static SolidColorBrush CreateFrozenSolidBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void InitPower()
    {
        _alwaysShowCharging = _settingsService.AlwaysShowChargingIndicator;
        _powerService.ChargerConnected += pct =>
            Dispatcher.InvokeAsync(() => ShowCharging(pct));
        _powerService.PowerStatusChanged += (isCharging, pct) =>
            Dispatcher.InvokeAsync(() => UpdateConstantChargingIndicator(isCharging, pct));
        _powerService.Initialize();
        UpdateClockBattery();
    }

    private void SetAlwaysCharging(bool enable)
    {
        _alwaysShowCharging = enable;
        UpdateConstantChargingIndicator(_powerService.IsCharging, _powerService.BatteryPercent);
    }

    // Centre text in the space right of the battery; no shift when privacy dots take the right side.
    private void UpdateIdleTextShift() =>
        IdleText.Margin = new Thickness(_isCharging && !_privacyActive ? 36 : 0, 0, 0, 0);

    private void UpdateConstantChargingIndicator(bool isCharging, int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        _batteryPercent = clamped;
        _isCharging = _alwaysShowCharging && isCharging;

        if (IdleChargingBattery != null)
        {
            IdleChargingBattery.Visibility = _isCharging ? Visibility.Visible : Visibility.Collapsed;
            UpdateIdleTextShift();
            IdleChargingBattery.ToolTip = $"{clamped}% Charging (Click to expand)";

            // Interior usable fill width is 18px
            double fillWidth = IslandLogic.CalculateBatteryFillWidth(18.0, clamped);
            IdleBatteryFill.Width = fillWidth;
        }

        UpdateClockBattery();

        if (_mode == Mode.Idle)
        {
            Animate(Mode.Idle);
        }
    }

    private void UpdateClockBattery()
    {
        if (ClockBatteryPanel == null) return;

        int pct = _batteryPercent;
        bool charging = _powerService.IsCharging;

        ClockBatteryPctText.Text = IslandLogic.FormatBatteryPercent(pct);
        ClockBatteryBolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;

        // Interior track of ClockBatteryFill is 18px
        double fillWidth = IslandLogic.CalculateBatteryFillWidth(18.0, pct);
        ClockBatteryFill.Width = fillWidth;
        ClockBatteryFill.Background = (charging || pct > 20) ? BatteryGreenBrush : BatteryRedBrush;
    }

    private void OnIdleBatteryClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowCharging(_batteryPercent);
    }

    private void ShowCharging(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        ChargePctText.Text = IslandLogic.FormatBatteryPercent(clamped);
        ChargeStatusText.Text = "Charging";

        // Battery shell interior usable width is 22px
        double fillWidth = IslandLogic.CalculateBatteryFillWidth(22.0, clamped);
        Gsap.Num(ChargeFill, "w", ChargeFill.Width, fillWidth, 0.5, Ease.PowerOut(3), v => ChargeFill.Width = Math.Max(0, v));

        // neon bolt: elastic pop
        Gsap.Num(ChargeBoltScale, "pop", 1.5, 1.0, 0.8, Ease.ElasticOut(1, 0.4), v => ChargeBoltScale.ScaleX = ChargeBoltScale.ScaleY = v);

        Go(Mode.Charging, 3.2);
    }
}

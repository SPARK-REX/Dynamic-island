// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Domain service monitoring Windows AC power connection, charger plug-in events, and battery status.
/// Triggers when the device transitions from battery to AC power, and provides simulation hooks.
/// </summary>
public sealed class PowerService : IDisposable
{
    private byte _lastAcStatus = 255; // 255 = Unknown, 0 = Offline, 1 = Online
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _initialized;
    private bool _disposed;

    /// <summary>Fired when a charger is plugged in with current battery percentage (0-100).</summary>
    public event Action<int>? ChargerConnected;

    /// <summary>Fired when AC charging status or battery level changes (isCharging, percent).</summary>
    public event Action<bool, int>? PowerStatusChanged;

    /// <summary>True when currently connected to AC power.</summary>
    public bool IsCharging => _lastAcStatus == 1;

    /// <summary>Current battery percentage (0-100).</summary>
    public int BatteryPercent { get; private set; } = 100;

    public void Initialize()
    {
        if (_disposed || _initialized) return;
        _initialized = true;

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }

        _pollTimer.Tick += (_, _) => CheckPowerStatus(triggerOnConnectOnly: true);
        _pollTimer.Start();

        // Baseline reading without triggering animation on cold startup
        CheckPowerStatus(triggerOnConnectOnly: false);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Mode == PowerModes.StatusChange)
        {
            CheckPowerStatus(triggerOnConnectOnly: true);
        }
    }

    /// <summary>
    /// Checks the current system power status via Win32 GetSystemPowerStatus.
    /// Fires ChargerConnected only when transitioning from unplugged/unknown to plugged in.
    /// Fires PowerStatusChanged whenever charging state or percent changes.
    /// </summary>
    public void CheckPowerStatus(bool triggerOnConnectOnly)
    {
        if (_disposed) return;

        try
        {
            if (NativeMethods.GetSystemPowerStatus(out var status))
            {
                byte currentAc = status.ACLineStatus;
                int batteryPct = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
                bool isCharging = currentAc == 1;
                bool stateChanged = _lastAcStatus != currentAc || BatteryPercent != batteryPct;

                BatteryPercent = batteryPct;

                if (triggerOnConnectOnly && IslandLogic.IsChargerPluggedIn(_lastAcStatus, currentAc))
                {
                    ChargerConnected?.Invoke(batteryPct);
                }

                _lastAcStatus = currentAc;

                if (stateChanged || !triggerOnConnectOnly)
                {
                    PowerStatusChanged?.Invoke(isCharging, batteryPct);
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    /// <summary>
    /// Simulates plugging in the charger with a specified or current battery level.
    /// </summary>
    public void SimulateChargerConnected(int? percent = null)
    {
        if (_disposed) return;

        int pct = percent ?? 85;
        try
        {
            if (percent == null && NativeMethods.GetSystemPowerStatus(out var status) && status.BatteryLifePercent <= 100)
            {
                pct = status.BatteryLifePercent;
            }
        }
        catch { }

        _lastAcStatus = 1;
        BatteryPercent = Math.Clamp(pct, 0, 100);
        PowerStatusChanged?.Invoke(true, BatteryPercent);
        ChargerConnected?.Invoke(BatteryPercent);
    }

    /// <summary>
    /// Simulates unplugging the charger.
    /// </summary>
    public void SimulateChargerDisconnected()
    {
        if (_disposed) return;
        _lastAcStatus = 0;
        PowerStatusChanged?.Invoke(false, BatteryPercent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }

        _pollTimer.Stop();
    }
}

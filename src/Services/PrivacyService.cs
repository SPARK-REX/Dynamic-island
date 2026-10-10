// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Diagnostics;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIsland.Services;

/// <summary>
/// Monitors Windows microphone and camera hardware access by background and desktop applications.
/// </summary>
public sealed class PrivacyService
{
    private static readonly string[] KnownCameraProcesses = { "WindowsCamera" };

    public static (bool MicInUse, bool CamInUse) CheckHardwareAccess()
    {
        bool mic = IsMicrophoneInUse();
        bool cam = IsCameraInUse();
        return (mic, cam);
    }

    public static bool IsMicrophoneInUse()
    {
        // 1. Check real-time WASAPI capture audio sessions (zero-delay, 100% accurate for all audio apps)
        if (IsWasapiCaptureActive()) return true;

        // 2. Check CapabilityAccessManager consent store in HKCU and HKLM
        return IsCapabilityInUse("microphone");
    }

    public static bool IsCameraInUse()
    {
        // 1. Check if known dedicated camera applications are running (e.g. Windows Camera on Windows 11)
        if (IsCameraProcessRunning()) return true;

        // 2. Check CapabilityAccessManager consent store in HKCU and HKLM
        return IsCapabilityInUse("webcam");
    }

    /// <summary>
    /// Pure, testable predicate: returns true when a CapabilityAccessManager consent-store entry
    /// represents a device that is currently in use (i.e. not yet released by the app).
    /// Windows writes LastUsedTimeStart when device access begins.
    /// When releasing, it writes LastUsedTimeStop (non-zero).
    /// If an app starts a new session, LastUsedTimeStart is updated while the old LastUsedTimeStop
    /// may remain until release, so start > stop indicates an active session.
    /// </summary>
    /// <param name="start">Value of <c>LastUsedTimeStart</c>, or null if absent.</param>
    /// <param name="stop">Value of <c>LastUsedTimeStop</c>, or null if absent.</param>
    public static bool IsEntryInUse(long? start, long? stop)
    {
        if (!start.HasValue || start.Value <= 0) return false;
        if (!stop.HasValue || stop.Value == 0) return true;
        return start.Value > stop.Value;
    }

    private static bool IsWasapiCaptureActive()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in endpoints)
            {
                try
                {
                    var sessionManager = device.AudioSessionManager;
                    if (sessionManager == null) continue;

                    sessionManager.RefreshSessions();
                    var sessions = sessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.IsSystemSoundsSession) continue;
                        if (session.State == AudioSessionState.AudioSessionStateActive)
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // CoreAudio enumeration may fail if audio service is stopped or hardware absent
        }
        return false;
    }

    private static bool IsCameraProcessRunning()
    {
        try
        {
            foreach (var name in KnownCameraProcesses)
            {
                var procs = Process.GetProcessesByName(name);
                bool any = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (any) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool IsCapabilityInUse(string capability)
    {
        string subPath = $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";
        if (CheckRegistryRoot(Registry.CurrentUser, subPath)) return true;
        if (CheckRegistryRoot(Registry.LocalMachine, subPath)) return true;
        return false;
    }

    private static bool CheckRegistryRoot(RegistryKey baseKey, string subPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subPath);
            if (key == null) return false;
            return CheckConsentKey(key);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckConsentKey(RegistryKey parent)
    {
        foreach (var subName in parent.GetSubKeyNames())
        {
            using var subKey = parent.OpenSubKey(subName);
            if (subKey == null) continue;

            // recurse into any container key (NonPackaged, nested per-app keys)
            if (subKey.GetSubKeyNames().Length > 0 && CheckConsentKey(subKey)) return true;
            {
                var stopRaw = subKey.GetValue("LastUsedTimeStop");
                var startRaw = subKey.GetValue("LastUsedTimeStart");
                long? stop  = stopRaw  is long sv ? sv : (stopRaw != null && long.TryParse(stopRaw.ToString(), out var sp) ? sp : null);
                long? start = startRaw is long st ? st : (startRaw != null && long.TryParse(startRaw.ToString(), out var tp) ? tp : null);
                if (IsEntryInUse(start, stop)) return true;
            }
        }
        return false;
    }
}

// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Services;

/// <summary>
/// Monitors Windows master audio output volume and mute status via NAudio WASAPI.
/// Properly unregisters COM notification handlers on disposal.
/// </summary>
public sealed class AudioVolumeService : IDisposable
{
    private MMDevice? _device;
    private AudioEndpointVolumeNotificationDelegate? _handler;
    private bool _disposed;

    public event Action<float, bool>? VolumeChanged;

    public void Initialize()
    {
        if (_disposed || _device != null) return;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_device?.AudioEndpointVolume != null)
            {
                _handler = data =>
                {
                    if (!_disposed)
                    {
                        VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
                    }
                };
                _device.AudioEndpointVolume.OnVolumeNotification += _handler;
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            // Audio output device missing or disabled: gracefully continue without volume HUD
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_device?.AudioEndpointVolume != null && _handler != null)
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= _handler;
            }
            _device?.Dispose();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        finally
        {
            _device = null;
            _handler = null;
        }
    }
}

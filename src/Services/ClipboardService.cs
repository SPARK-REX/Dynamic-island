// ====================================================================================================
// Author: Akhin (SPARK-REX)
// GitHub: https://github.com/SPARK-REX
// Project: Dynamic Island for Windows
// ====================================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace DynamicIsland.Services;

/// <summary>
/// Domain service for monitoring Windows clipboard updates and maintaining
/// a ring buffer of the last 10 unique copied texts.
/// Thread-safe, leak-free, and handles Win32 clipboard lock contention gracefully.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    public const int MaxHistoryCount = 10;
    private const int MaxCharacterLimit = 100_000;

    private readonly List<string> _history = new(MaxHistoryCount);
    private readonly object _lock = new();

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private bool _internalCopy;
    private bool _disposed;

    /// <summary>Fired whenever the clipboard history collection changes.</summary>
    public event Action<IReadOnlyList<string>>? HistoryChanged;

    /// <summary>Fired whenever a new non-empty text item is copied.</summary>
    public event Action<string>? LatestTextChanged;

    /// <summary>The most recently copied string, or empty if history is empty.</summary>
    public string LatestText
    {
        get
        {
            lock (_lock)
            {
                return _history.Count > 0 ? _history[0] : string.Empty;
            }
        }
    }

    /// <summary>Snapshot of the current history (most recent first, up to 10 items).</summary>
    public IReadOnlyList<string> History
    {
        get
        {
            lock (_lock)
            {
                return _history.ToArray();
            }
        }
    }

    /// <summary>Number of items currently stored in history.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }
    }

    /// <summary>
    /// Initializes clipboard format listener on the specified window handle.
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero || _hwnd != IntPtr.Zero) return;

        _hwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProcHook);
        NativeMethods.AddClipboardFormatListener(hwnd);

        // Pre-populate with current clipboard text if available
        TryFetchClipboardText();
    }

    /// <summary>
    /// Adds text directly to history (MRU order, max 10 items).
    /// Returns true if added or moved to top, false if rejected (empty/whitespace).
    /// </summary>
    public bool AddText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Prevent runaway memory usage on massive copies
        string sanitized = text.Length > MaxCharacterLimit
            ? text.Substring(0, MaxCharacterLimit)
            : text;

        IReadOnlyList<string> snapshot;
        lock (_lock)
        {
            int existingIndex = _history.IndexOf(sanitized);
            if (existingIndex == 0)
            {
                // Already the latest item; no change needed
                return false;
            }

            if (existingIndex > 0)
            {
                _history.RemoveAt(existingIndex);
            }

            _history.Insert(0, sanitized);

            while (_history.Count > MaxHistoryCount)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            snapshot = _history.ToArray();
        }

        LatestTextChanged?.Invoke(sanitized);
        HistoryChanged?.Invoke(snapshot);
        return true;
    }

    /// <summary>
    /// Copies text back to the Windows clipboard with re-entrancy prevention.
    /// </summary>
    public bool CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        _internalCopy = true;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    AddText(text);
                    return true;
                }
                catch (COMException)
                {
                    Task.Delay(30).Wait();
                }
                catch (ExternalException)
                {
                    Task.Delay(30).Wait();
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
        finally
        {
            _internalCopy = false;
        }

        return false;
    }

    /// <summary>Clears the entire clipboard history.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
        }

        LatestTextChanged?.Invoke(string.Empty);
        HistoryChanged?.Invoke(Array.Empty<string>());
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            if (!_internalCopy)
            {
                // Dispatcher async to avoid blocking OS clipboard chain and retry if temporarily locked
                Application.Current?.Dispatcher.InvokeAsync(TryFetchClipboardText);
            }
        }

        return IntPtr.Zero;
    }

    private void TryFetchClipboardText()
    {
        if (_disposed || _internalCopy) return;

        // Clipboard read with up to 3 retries for transient lock contention
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        AddText(text);
                    }
                }
                return;
            }
            catch (COMException)
            {
                Task.Delay(25).Wait();
            }
            catch (ExternalException)
            {
                Task.Delay(25).Wait();
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
            _hwndSource?.RemoveHook(WndProcHook);
            _hwnd = IntPtr.Zero;
            _hwndSource = null;
        }
    }
}

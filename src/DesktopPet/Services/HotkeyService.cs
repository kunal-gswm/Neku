using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPet.Services;

/// <summary>
/// Registers global hotkeys on a host window using RegisterHotKey + a WndProc hook on the
/// pet window's HWND — no extra low-level keyboard hook needed. Supports multiple hotkey IDs
/// (e.g. Chat and Exit).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    public const int SummonHotkeyId = 0xB001;
    public const int ExitHotkeyId = 0xB002;
    public const int PaletteHotkeyId = 0xB003;
    public const int PaletteFallbackHotkeyId = 0xB004;

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly HashSet<int> _registeredIds = new();
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _disposed;

    /// <summary>Formats modifier keys and a key into standard human-readable format (e.g. "Ctrl + Alt + P").</summary>
    public static string FormatHotkey(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    /// <summary>Hook the host window so WM_HOTKEY messages reach us.</summary>
    public void Attach(IntPtr hwnd)
    {
        _hwnd   = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Register a specific hotkey ID with callback. Returns false if registration fails (e.g. conflict).
    /// </summary>
    public bool Register(int id, ModifierKeys mods, Key key, Action action)
    {
        Unregister(id);
        if (_hwnd == IntPtr.Zero || key == Key.None) return false;

        uint fs = MOD_NOREPEAT;
        if (mods.HasFlag(ModifierKeys.Alt))     fs |= MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Control)) fs |= MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Shift))   fs |= MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) fs |= MOD_WIN;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        bool ok = RegisterHotKey(_hwnd, id, fs, vk);
        if (!ok && (fs & MOD_NOREPEAT) != 0)
        {
            ok = RegisterHotKey(_hwnd, id, fs & ~MOD_NOREPEAT, vk);
        }

        if (ok)
        {
            _registeredIds.Add(id);
            _callbacks[id] = action;
        }
        return ok;
    }

    /// <summary>
    /// Register the global summon hotkey (Ctrl + Alt + N).
    /// Returns false if the combination is taken, invalid, or window is unattached.
    /// </summary>
    public bool RegisterSummon(Action action)
    {
        return Register(SummonHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.N, action);
    }

    /// <summary>Unregister the global summon hotkey.</summary>
    public void UnregisterSummon()
    {
        Unregister(SummonHotkeyId);
    }

    /// <summary>
    /// Register the global graceful exit hotkey (Ctrl + Alt + Q).
    /// Returns false if the combination is taken, invalid, or window is unattached.
    /// </summary>
    public bool RegisterExit(Action action)
    {
        return Register(ExitHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.Q, action);
    }

    /// <summary>Unregister the global graceful exit hotkey.</summary>
    public void UnregisterExit()
    {
        Unregister(ExitHotkeyId);
    }

    /// <summary>
    /// Register the global Command Palette hotkey (default Ctrl + Alt + P).
    /// Returns false if the combination is taken, invalid, or window is unattached.
    /// </summary>
    public bool RegisterPalette(ModifierKeys mods, Key key, Action action)
    {
        return Register(PaletteHotkeyId, mods, key, action);
    }

    /// <summary>Unregister the global Command Palette hotkey.</summary>
    public void UnregisterPalette()
    {
        Unregister(PaletteHotkeyId);
    }

    /// <summary>Unregister a specific hotkey ID.</summary>
    public void Unregister(int id)
    {
        if (_registeredIds.Contains(id) && _hwnd != IntPtr.Zero)
        {
            try { UnregisterHotKey(_hwnd, id); } catch { }
            _registeredIds.Remove(id);
        }
        _callbacks.Remove(id);
    }

    public bool IsRegistered(int id) => _registeredIds.Contains(id);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _registeredIds.ToList())
        {
            if (_hwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hwnd, id); } catch { }
            }
        }
        _registeredIds.Clear();
        _callbacks.Clear();

        try { _source?.RemoveHook(WndProc); } catch { }
        _source = null;
    }
}

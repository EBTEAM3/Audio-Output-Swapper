using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

namespace AudioSwapper.Interop;

/// <summary>
/// Registers one system-wide hotkey through RegisterHotKey.
///
/// RegisterHotKey is used rather than a low-level keyboard hook because it works
/// while a fullscreen game or another elevated-equal app has focus, and it does
/// not make the app look like a keylogger to security software.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA5D1;

    [Flags]
    private enum Modifiers : uint
    {
        None = 0x0,
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
        NoRepeat = 0x4000,
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public event Action? Pressed;

    /// <summary>
    /// Null while the hotkey is registered. Otherwise explains why it is not --
    /// almost always because another app already owns that combination.
    /// </summary>
    public string? LastError { get; private set; }

    public bool IsRegistered => _registered;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Applies <paramref name="gesture"/>, replacing any previous registration.
    /// Returns false and sets <see cref="LastError"/> if Windows refused it.
    /// </summary>
    public bool Apply(string? gesture, bool enabled)
    {
        Unregister();
        LastError = null;

        if (!enabled) return true;

        if (!TryParse(gesture, out uint modifiers, out uint virtualKey))
        {
            LastError = $"'{gesture}' is not a valid shortcut.";
            return false;
        }

        // NOREPEAT stops a held-down combo from firing a switch storm.
        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | (uint)Modifiers.NoRepeat, virtualKey))
        {
            LastError = $"Windows refused '{gesture}'. Another app is probably using it.";
            return false;
        }

        _registered = true;
        return true;
    }

    private void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    /// <summary>
    /// Parses gestures like "Ctrl+Alt+A", "Win+Shift+F9", "Ctrl+Num*".
    /// Deliberately lenient about spacing and case, since the config file is
    /// meant to be hand-editable.
    /// </summary>
    public static bool TryParse(string? gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var parts = gesture
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (parts.Count == 0) return false;

        string keyToken = parts[^1];
        var mods = Modifiers.None;

        foreach (string token in parts.Take(parts.Count - 1))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Modifiers.Control; break;
                case "alt": mods |= Modifiers.Alt; break;
                case "shift": mods |= Modifiers.Shift; break;
                case "win" or "meta" or "super": mods |= Modifiers.Win; break;
                default: return false;
            }
        }

        // A bare key with no modifier would swallow that key system-wide.
        if (mods == Modifiers.None) return false;

        if (!TryParseKey(keyToken, out virtualKey)) return false;

        modifiers = (uint)mods;
        return true;
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrEmpty(token)) return false;

        string key = token.ToUpperInvariant();

        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (key.StartsWith('F') && key.Length is 2 or 3 &&
            int.TryParse(key.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1); // VK_F1 == 0x70
            return true;
        }

        var named = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = 0x20,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["RETURN"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["HOME"] = 0x24,
            ["END"] = 0x23,
            ["PGUP"] = 0x21,
            ["PGDN"] = 0x22,
            ["INSERT"] = 0x2D,
            ["DELETE"] = 0x2E,
            ["UP"] = 0x26,
            ["DOWN"] = 0x28,
            ["LEFT"] = 0x25,
            ["RIGHT"] = 0x27,
            ["NUM*"] = 0x6A,
            ["NUM+"] = 0x6B,
            ["NUM-"] = 0x6D,
            ["NUM/"] = 0x6F,
            ["`"] = 0xC0,
            ["\\"] = 0xDC,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            ["-"] = 0xBD,
            ["="] = 0xBB,
        };

        return named.TryGetValue(token, out virtualKey);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Unregister();
        _source.RemoveHook(WndProc);
    }
}

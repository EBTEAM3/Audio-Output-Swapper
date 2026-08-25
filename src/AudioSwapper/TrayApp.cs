using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioSwapper.Audio;
using AudioSwapper.Config;
using AudioSwapper.Interop;
using AudioSwapper.Ui;
using Forms = System.Windows.Forms;

namespace AudioSwapper;

/// <summary>
/// Owns the tray icon and wires everything else together: the audio service,
/// the config, the hotkey, and the two windows.
/// </summary>
internal sealed class TrayApp : IDisposable
{
    private const string TrayTooltipFallback = "Audio Swapper";

    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private readonly AudioDeviceService _audio;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly HwndSource _messageWindow;
    private readonly HotkeyManager _hotkeys;
    private readonly CommandServer _commands;
    private readonly ToastWindow _toast;
    private readonly MenuWindow _menu;

    private System.Drawing.Icon? _currentTrayIcon;
    private IReadOnlyList<AudioDevice> _devices = Array.Empty<AudioDevice>();
    private bool _disposed;

    public TrayApp(string startupCommand = "none")
    {
        _store = new ConfigStore();
        _config = _store.Load();

        _audio = new AudioDeviceService(Dispatcher.CurrentDispatcher);
        _audio.DevicesChanged += OnDevicesChanged;

        // A hidden HWND to receive WM_HOTKEY and WM_SETTINGCHANGE. WPF gives no
        // window handle until something is shown, and the tray app may never
        // show anything.
        _messageWindow = new HwndSource(new HwndSourceParameters("AudioSwapper.Messages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _messageWindow.AddHook(OnMessageWindowMessage);

        _hotkeys = new HotkeyManager(_messageWindow);
        _hotkeys.Pressed += Swap;

        _toast = new ToastWindow();
        _menu = new MenuWindow(BuildState, OnMenuMessage);

        _notifyIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = TrayTooltipFallback,
        };
        _notifyIcon.MouseUp += OnTrayMouseUp;

        _commands = new CommandServer(Dispatcher.CurrentDispatcher, RunCommand);

        RefreshDevices();
        ApplyHotkey();
        SyncStartupRegistration();

        // Deferred so the constructor finishes and the tray icon is live before
        // a window is asked to open on top of it.
        if (startupCommand != "none")
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, () => RunCommand(startupCommand));
        }

        // Startup touches a lot of pages that are never read again -- JIT,
        // assembly loading, the first icon render. Give them back once things
        // have settled.
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle, MemoryTrim.TrimWorkingSet);
    }

    /// <summary>Handles a command from the command line or a second launch.</summary>
    private void RunCommand(string command)
    {
        switch (command)
        {
            case "swap": Swap(); break;
            case "menu": _ = _menu.ToggleAsync(); break;
            case "quit": Application.Current.Shutdown(); break;
        }
    }

    // ---- Tray -------------------------------------------------------------

    private void OnTrayMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left) Swap();
        else if (e.Button == Forms.MouseButtons.Right) _ = _menu.ToggleAsync();
    }

    private void UpdateTrayIcon()
    {
        var active = CurrentDefault();

        string iconKey = _config.TrayReflectsDevice && active is not null
            ? IconKeyFor(active)
            : DeviceIcons.DefaultKey;

        var icon = DeviceIcons.Get(iconKey);

        // Ask the shell how big a tray icon is right now rather than assuming
        // 16px -- it is 20 at 125% and 24 at 150%, and a stretched 16px glyph
        // looks obviously blurry next to the system icons.
        int size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);

        var rendered = TrayIconRenderer.Render(icon, size, SystemTheme.IsSystemLight());

        _notifyIcon.Icon = rendered;

        // Only release the previous icon after the new one is assigned, or the
        // shell briefly renders a destroyed handle.
        TrayIconRenderer.Release(_currentTrayIcon);
        _currentTrayIcon = rendered;

        _notifyIcon.Text = BuildTooltip(active);
    }

    private string BuildTooltip(AudioDevice? active)
    {
        string target = OtherOf(active?.Id)?.Name ?? "";
        string line = active is null
            ? "No output device"
            : $"Now: {active.Name}";

        if (!string.IsNullOrEmpty(target)) line += $"\nClick to switch to {target}";

        // NotifyIcon.Text is capped at 127 characters; anything longer is
        // silently rejected and the tooltip disappears entirely.
        return line.Length > 127 ? line[..127] : line;
    }

    // ---- Devices ----------------------------------------------------------

    private void OnDevicesChanged()
    {
        RefreshDevices();
        _menu.PushState();
    }

    private void RefreshDevices()
    {
        _devices = _audio.GetRenderDevices();

        // Remember names and seed icons for anything new, so a device keeps its
        // identity in the menu once it is unplugged.
        bool dirty = false;
        foreach (var device in _devices)
        {
            if (!_config.NamesByDeviceId.TryGetValue(device.Id, out string? known) || known != device.Name)
            {
                _config.NamesByDeviceId[device.Id] = device.Name;
                dirty = true;
            }

            if (!_config.IconsByDeviceId.ContainsKey(device.Id))
            {
                _config.IconsByDeviceId[device.Id] = DeviceIcons.GuessFor(device);
                dirty = true;
            }
        }

        if (dirty) Save();

        UpdateTrayIcon();
    }

    private AudioDevice? CurrentDefault()
    {
        string? id = _audio.TryGetDefaultId(ERole.Multimedia);
        return id is null ? null : _devices.FirstOrDefault(d => d.Id == id);
    }

    private string IconKeyFor(AudioDevice device) =>
        _config.IconsByDeviceId.TryGetValue(device.Id, out string? key)
            ? key
            : DeviceIcons.GuessFor(device);

    private string IconKeyFor(string deviceId)
    {
        if (_config.IconsByDeviceId.TryGetValue(deviceId, out string? key)) return key;

        var device = _devices.FirstOrDefault(d => d.Id == deviceId);
        return device is null ? DeviceIcons.DefaultKey : DeviceIcons.GuessFor(device);
    }

    /// <summary>The other half of the configured pair, given what is playing now.</summary>
    private RememberedDevice? OtherOf(string? currentId)
    {
        var a = _config.DeviceA;
        var b = _config.DeviceB;
        if (a is null || b is null) return null;

        return currentId == a.Id ? b : a;
    }

    // ---- Switching --------------------------------------------------------

    private void Swap()
    {
        var a = _config.DeviceA;
        var b = _config.DeviceB;

        if (a is null || b is null)
        {
            // Nothing configured yet. Open the menu rather than failing quietly.
            _ = _menu.ToggleAsync();
            return;
        }

        string? currentId = _audio.TryGetDefaultId(ERole.Multimedia);
        var target = currentId == a.Id ? b : a;

        SwitchTo(target.Id);
    }

    private void SwitchTo(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);

        if (device is null || !device.IsActive)
        {
            string name = device?.Name
                          ?? (_config.NamesByDeviceId.TryGetValue(deviceId, out string? cached)
                              ? cached
                              : "That device");

            ShowToast(name, "Not connected right now", IconKeyFor(deviceId),
                      kicker: "Unavailable", communications: false, isError: true);
            return;
        }

        try
        {
            _audio.SetDefault(device.Id, _config.AlsoSetCommunications);
        }
        catch (Exception ex)
        {
            ShowToast(device.Name, Describe(ex), IconKeyFor(device),
                      kicker: "Switch failed", communications: false, isError: true);
            return;
        }

        RefreshDevices();
        _menu.PushState();

        ShowToast(device.Name, device.AdapterName, IconKeyFor(device),
                  kicker: "Switched to",
                  communications: _config.AlsoSetCommunications);
    }

    private static string Describe(Exception ex) =>
        ex is NotSupportedException ? ex.Message : "Windows refused the change";

    private void ShowToast(
        string name, string sub, string iconKey, string kicker,
        bool communications, bool isError = false)
    {
        // Failures are shown even with the popup turned off: silently doing
        // nothing when a click was meant to switch devices is worse than an
        // unwanted notification.
        if (!_config.ShowToast && !isError) return;

        _toast.BottomOffset = _config.ToastBottomOffset;
        _toast.ShowFor(new ToastContent
        {
            DeviceName = name,
            DeviceSub = sub,
            Kicker = kicker,
            IconPaths = DeviceIcons.Get(iconKey).Paths,
            Theme = ResolveTheme(),
            Communications = communications,
            IsError = isError,
            DurationMs = _config.ToastDurationMs,
        });
    }

    // ---- Menu state and messages -----------------------------------------

    private string ResolveTheme() => _config.Theme switch
    {
        "light" => "light",
        "dark" => "dark",
        _ => SystemTheme.IsAppsLight() ? "light" : "dark",
    };

    private object BuildState()
    {
        string? aId = _config.DeviceA?.Id;
        string? bId = _config.DeviceB?.Id;

        var rows = _devices.Select(d => new
        {
            id = d.Id,
            name = d.Name,
            adapter = d.AdapterName,
            iconKey = IconKeyFor(d),
            active = d.IsActive,
            isDefault = d.IsDefault,
            slot = d.Id == aId ? "A" : d.Id == bId ? "B" : (string?)null,
            stateLabel = StateLabel(d.State),
        }).ToList();

        // A configured device whose driver has been removed will not enumerate
        // at all. Surface it anyway so the slot is not silently empty.
        foreach (var remembered in new[] { _config.DeviceA, _config.DeviceB })
        {
            if (remembered is null || rows.Any(r => r.id == remembered.Id)) continue;

            rows.Add(new
            {
                id = remembered.Id,
                name = remembered.Name,
                adapter = "",
                iconKey = remembered.IconKey,
                active = false,
                isDefault = false,
                slot = remembered.Id == aId ? "A" : (string?)"B",
                stateLabel = "Device not found",
            });
        }

        return new
        {
            type = "state",
            theme = ResolveTheme(),
            shell = "opaque",
            icons = DeviceIcons.All.Select(i => new { key = i.Key, label = i.Label, paths = i.Paths }),
            devices = rows,
            options = new
            {
                alsoSetCommunications = _config.AlsoSetCommunications,
                showToast = _config.ShowToast,
                trayReflectsDevice = _config.TrayReflectsDevice,
                startWithWindows = _config.StartWithWindows,
                hotkeyEnabled = _config.HotkeyEnabled,
                hotkey = _config.Hotkey,
                theme = _config.Theme,
            },
            hotkeyError = _config.HotkeyEnabled ? _hotkeys.LastError : null,
        };
    }

    private static string StateLabel(DeviceState state) => state switch
    {
        DeviceState.Active => "",
        DeviceState.Unplugged => "Unplugged",
        DeviceState.Disabled => "Disabled in Windows",
        DeviceState.NotPresent => "Not connected",
        _ => "Unavailable",
    };

    private void OnMenuMessage(JsonElement message)
    {
        if (!message.TryGetProperty("type", out var typeProperty)) return;

        switch (typeProperty.GetString())
        {
            case "assign":
                HandleAssign(message);
                break;

            case "setIcon":
                HandleSetIcon(message);
                break;

            case "activate":
                if (TryGetString(message, "deviceId", out string activateId))
                    SwitchTo(activateId);
                break;

            case "swap":
                Swap();
                break;

            case "option":
                HandleOption(message);
                break;

            case "setHotkey":
                if (TryGetString(message, "value", out string gesture))
                {
                    _config.Hotkey = gesture;
                    Save();
                    ApplyHotkey();
                    _menu.PushState();
                }
                break;

            case "quit":
                Application.Current.Shutdown();
                break;
        }
    }

    private void HandleAssign(JsonElement message)
    {
        if (!TryGetString(message, "deviceId", out string deviceId)) return;

        string? slot = message.TryGetProperty("slot", out var slotProperty) &&
                       slotProperty.ValueKind == JsonValueKind.String
            ? slotProperty.GetString()
            : null;

        var remembered = BuildRemembered(deviceId);

        if (slot is null)
        {
            // Clearing: drop it from whichever slot currently holds it.
            if (_config.DeviceA?.Id == deviceId) _config.DeviceA = null;
            if (_config.DeviceB?.Id == deviceId) _config.DeviceB = null;
        }
        else if (slot == "A")
        {
            // A device cannot occupy both slots, or the toggle would no-op.
            if (_config.DeviceB?.Id == deviceId) _config.DeviceB = _config.DeviceA;
            _config.DeviceA = remembered;
        }
        else if (slot == "B")
        {
            if (_config.DeviceA?.Id == deviceId) _config.DeviceA = _config.DeviceB;
            _config.DeviceB = remembered;
        }

        Save();
        UpdateTrayIcon();
        _menu.PushState();
    }

    private RememberedDevice BuildRemembered(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id == deviceId);

        return new RememberedDevice
        {
            Id = deviceId,
            Name = device?.Name
                   ?? (_config.NamesByDeviceId.TryGetValue(deviceId, out string? cached) ? cached : "Unknown device"),
            IconKey = IconKeyFor(deviceId),
        };
    }

    private void HandleSetIcon(JsonElement message)
    {
        if (!TryGetString(message, "deviceId", out string deviceId)) return;
        if (!TryGetString(message, "iconKey", out string iconKey)) return;

        _config.IconsByDeviceId[deviceId] = iconKey;

        // Keep the slot copies in step, since they carry their own icon key for
        // the case where the device no longer enumerates.
        if (_config.DeviceA?.Id == deviceId) _config.DeviceA.IconKey = iconKey;
        if (_config.DeviceB?.Id == deviceId) _config.DeviceB.IconKey = iconKey;

        Save();
        UpdateTrayIcon();
        _menu.PushState();
    }

    private void HandleOption(JsonElement message)
    {
        if (!TryGetString(message, "name", out string name)) return;
        if (!message.TryGetProperty("value", out var valueProperty)) return;

        bool boolValue = valueProperty.ValueKind == JsonValueKind.True;

        switch (name)
        {
            case "alsoSetCommunications":
                _config.AlsoSetCommunications = boolValue;
                break;

            case "showToast":
                _config.ShowToast = boolValue;
                break;

            case "trayReflectsDevice":
                _config.TrayReflectsDevice = boolValue;
                UpdateTrayIcon();
                break;

            case "startWithWindows":
                // Not "boolValue && ApplyStartup(...)": short-circuiting there
                // means switching the toggle OFF never actually removes the
                // registry entry, so the app keeps starting with Windows.
                if (ApplyStartup(boolValue)) _config.StartWithWindows = boolValue;
                // On failure the stored value is left alone and PushState
                // re-renders the toggle from config, so it snaps back to the
                // truth rather than claiming a change that did not happen.
                break;

            case "hotkeyEnabled":
                _config.HotkeyEnabled = boolValue;
                ApplyHotkey();
                break;

            case "theme":
                if (valueProperty.ValueKind == JsonValueKind.String)
                    _config.Theme = valueProperty.GetString() ?? "system";
                break;

            default:
                return;
        }

        Save();
        _menu.PushState();
    }

    private static bool TryGetString(JsonElement message, string property, out string value)
    {
        value = "";
        if (!message.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;

        value = element.GetString() ?? "";
        return value.Length > 0;
    }

    // ---- Hotkey and startup ----------------------------------------------

    private void ApplyHotkey() => _hotkeys.Apply(_config.Hotkey, _config.HotkeyEnabled);

    private bool ApplyStartup(bool enabled)
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return false;

        return StartupRegistration.Set(enabled, path);
    }

    /// <summary>
    /// Reconciles the saved preference with what is actually in the registry.
    /// They drift when the user disables the entry from Task Manager, and the
    /// menu should show the truth rather than the stale preference.
    /// </summary>
    private void SyncStartupRegistration()
    {
        bool actual = StartupRegistration.IsEnabled();

        if (_config.StartWithWindows && !actual)
        {
            if (!ApplyStartup(true))
            {
                _config.StartWithWindows = false;
                Save();
            }
        }
        else if (!_config.StartWithWindows && actual)
        {
            _config.StartWithWindows = true;
            Save();
        }
    }

    // ---- Windows messages -------------------------------------------------

    private IntPtr OnMessageWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSettingChange = 0x001A;
        const int WmDpiChanged = 0x02E0;

        if (msg == WmSettingChange)
        {
            string? area = lParam == IntPtr.Zero
                ? null
                : System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam);

            // Fired when the user flips light/dark mode. The tray glyph has to
            // recolour or it turns invisible against the new taskbar.
            if (area == "ImmersiveColorSet")
            {
                UpdateTrayIcon();
                _menu.PushState();
            }
        }
        else if (msg == WmDpiChanged)
        {
            UpdateTrayIcon();
        }

        return IntPtr.Zero;
    }

    private void Save() => _store.Save(_config);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= OnTrayMouseUp;
        _notifyIcon.Dispose();
        TrayIconRenderer.Release(_currentTrayIcon);

        _commands.Dispose();
        _hotkeys.Dispose();
        _messageWindow.RemoveHook(OnMessageWindowMessage);
        _messageWindow.Dispose();

        _audio.DevicesChanged -= OnDevicesChanged;
        _audio.Dispose();

        _toast.Shutdown();
        _menu.Shutdown();
    }
}

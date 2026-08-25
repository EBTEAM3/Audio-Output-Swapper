using System.Collections.Generic;

namespace AudioSwapper.Config;

/// <summary>
/// A remembered device. The id is what actually gets switched to; the name is
/// cached so an unplugged device still reads as "Projector (NVIDIA)" in the menu
/// instead of a bare endpoint GUID.
/// </summary>
internal sealed class RememberedDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string IconKey { get; set; } = DeviceIconDefaults.Generic;
}

internal static class DeviceIconDefaults
{
    public const string Generic = "generic";
}

/// <summary>
/// Everything the user can configure. Serialised to
/// %APPDATA%\AudioSwapper\config.json.
/// </summary>
internal sealed class AppConfig
{
    /// <summary>The two endpoints the tray click toggles between.</summary>
    public RememberedDevice? DeviceA { get; set; }
    public RememberedDevice? DeviceB { get; set; }

    /// <summary>
    /// Icon assignments for every device seen, keyed by endpoint id, so a choice
    /// survives being unassigned from the A/B pair and reassigned later.
    /// </summary>
    public Dictionary<string, string> IconsByDeviceId { get; set; } = new();

    /// <summary>Cached friendly names, same reasoning as IconsByDeviceId.</summary>
    public Dictionary<string, string> NamesByDeviceId { get; set; } = new();

    /// <summary>Move the eCommunications role too (Teams, Discord, Zoom follow it).</summary>
    public bool AlsoSetCommunications { get; set; } = true;

    /// <summary>Show the bottom-right confirmation popup after a switch.</summary>
    public bool ShowToast { get; set; } = true;

    /// <summary>How long the toast stays on screen before it animates out.</summary>
    public int ToastDurationMs { get; set; } = 2600;

    /// <summary>
    /// Gap between the bottom of the work area and the toast, in
    /// device-independent pixels. Raised well above the usual notification
    /// margin so the toast clears media controls and other overlays that live
    /// in the bottom-right corner.
    /// </summary>
    public int ToastBottomOffset { get; set; } = 94;

    /// <summary>Draw the active device's icon into the tray instead of a fixed logo.</summary>
    public bool TrayReflectsDevice { get; set; } = true;

    public bool StartWithWindows { get; set; }

    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>
    /// Hotkey as a human-readable string, e.g. "Ctrl+Alt+A". Parsed by
    /// HotkeyManager; kept as text so the config file stays hand-editable.
    /// </summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+A";

    /// <summary>"dark", "light", or "system".</summary>
    public string Theme { get; set; } = "system";
}

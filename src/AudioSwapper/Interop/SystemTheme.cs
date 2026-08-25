using System;
using Microsoft.Win32;

namespace AudioSwapper.Interop;

/// <summary>
/// Reads the two Windows theme preferences that matter here.
///
/// Windows tracks them separately, and they genuinely differ on a common setup:
/// "Custom" mode in Settings gives a dark taskbar with light app windows. Using
/// one value for both would put a white glyph on a white taskbar.
/// </summary>
internal static class SystemTheme
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when the taskbar and tray are light. Drives the tray glyph colour.</summary>
    public static bool IsSystemLight() => ReadFlag("SystemUsesLightTheme", defaultValue: false);

    /// <summary>True when app windows are light. Drives the menu and toast theme.</summary>
    public static bool IsAppsLight() => ReadFlag("AppsUseLightTheme", defaultValue: false);

    private static bool ReadFlag(string valueName, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return key?.GetValue(valueName) is int flag ? flag != 0 : defaultValue;
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }
}

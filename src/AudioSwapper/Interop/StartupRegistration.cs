using System;
using Microsoft.Win32;

namespace AudioSwapper.Interop;

/// <summary>
/// Manages the "start with Windows" entry.
///
/// Uses HKCU\...\Run rather than a Startup-folder shortcut or a scheduled task:
/// it needs no elevation, it is the location Task Manager's Startup tab shows
/// (so the user can disable it where they would expect to), and it survives the
/// app being moved because we rewrite the path on every enable.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioSwapper";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Returns true if the registry now matches <paramref name="enabled"/>.</summary>
    public static bool Set(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;

            if (enabled)
            {
                // Quoted because the path very often contains spaces --
                // "C:\Users\...\OneDrive\Projects\Audio swapper\..." being the
                // case in point.
                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception)
        {
            // Group policy or a locked hive. Report failure so the UI can revert
            // the toggle rather than lying about the state.
            return false;
        }
    }
}

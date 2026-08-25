using System;
using System.IO;
using System.Text.Json;

namespace AudioSwapper.Config;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as JSON under %APPDATA%.
///
/// Saving is write-to-temp-then-replace so a crash mid-write cannot leave a
/// truncated config behind, and every read failure falls back to defaults rather
/// than throwing -- a corrupt settings file should cost you your preferences,
/// not the ability to start the app.
/// </summary>
internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public string DirectoryPath { get; }
    public string FilePath { get; }

    public ConfigStore()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioSwapper");
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppConfig();

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return new AppConfig();

            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }
        catch (Exception)
        {
            // Unreadable or malformed. Start clean rather than refusing to run.
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);

            string json = JsonSerializer.Serialize(config, SerializerOptions);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);

            // File.Replace needs an existing destination; Move covers first save.
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, destinationBackupFileName: null);
            else
                File.Move(temp, FilePath);
        }
        catch (Exception)
        {
            // Disk full, roaming profile locked, OneDrive holding the file open.
            // Losing a settings write is not worth taking the tray icon down.
        }
    }
}

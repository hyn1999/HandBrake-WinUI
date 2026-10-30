using System;
using System.IO;
using System.Text.Json;

namespace HandBrake.WinUI;

/// <summary>
/// Independent WinUI preferences. Intentionally does not overwrite the original
/// WPF HandBrake settings.json, which has a different schema and lifecycle.
/// </summary>
public sealed class AppPreferences
{
    public string Theme { get; set; } = "System";
    public string PreferredPresetKey { get; set; } = string.Empty;
    public string DefaultPresetKey { get; set; } = string.Empty;
    public bool ShowPreviewPanel { get; set; } = true;
    public string DefaultOutputFolder { get; set; } = string.Empty;
    public bool ReplaceUnderscoresInFileNames { get; set; }
    public string Mp4Extension { get; set; } = ".mp4";
    public int ScanPreviewCount { get; set; } = 10;
    public int MinTitleDurationSeconds { get; set; }
    public bool KeepDuplicateTitles { get; set; }
    public int ScanVerbosity { get; set; } = 1;

    public static string StorageFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HandBrake", "WinUI3", "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(StorageFile))
                return new AppPreferences();

            var value = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(StorageFile));
            if (value is null) return new AppPreferences();
            value.Validate();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                    or JsonException or NotSupportedException)
        {
            // A malformed or inaccessible preferences file must not crash startup.
            return new AppPreferences();
        }
    }

    public void Save()
    {
        Validate();
        string path = StorageFile;
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        File.Move(temp, path, overwrite: true);
    }

    private void Validate()
    {
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "System";
        if (Mp4Extension is not (".mp4" or ".m4v")) Mp4Extension = ".mp4";
        DefaultOutputFolder ??= string.Empty;
        PreferredPresetKey ??= string.Empty;
        DefaultPresetKey ??= string.Empty;
        ScanPreviewCount = Math.Clamp(ScanPreviewCount, 1, 60);
        MinTitleDurationSeconds = Math.Clamp(MinTitleDurationSeconds, 0, 3600);
        ScanVerbosity = Math.Clamp(ScanVerbosity, 0, 2);
    }
}

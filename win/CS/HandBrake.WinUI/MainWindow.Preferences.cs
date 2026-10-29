using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private void ApplyPreferences()
    {
        WindowRoot.RequestedTheme = SettingsPanel.Preferences.Theme switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        UpdatePreviewLayout();
        UpdateSuggestedDestination();
    }

    private string? selectedScannedTitleName;

    private string? GetSuggestedOutputPath()
    {
        if (selectedSourcePaths.Count == 0) return null;

        string source = selectedSourcePaths[0];
        bool sourceIsFolder = Directory.Exists(source);
        if (sourceIsFolder && string.IsNullOrWhiteSpace(selectedScannedTitleName))
            return null;

        string originalName = sourceIsFolder ? selectedScannedTitleName! : source;
        string name = Path.GetFileNameWithoutExtension(originalName);
        if (SettingsPanel.Preferences.ReplaceUnderscoresInFileNames)
            name = name.Replace('_', ' ');
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        // HandBrake's native preset determines the muxer. Never suggest a
        // mismatched .mp4 extension for an MKV or WebM preset.
        string presetFormat = activePreset?.FileFormat ?? "";
        string extension = presetFormat.Contains("mkv", StringComparison.OrdinalIgnoreCase)
            ? ".mkv"
            : presetFormat.Contains("webm", StringComparison.OrdinalIgnoreCase)
                ? ".webm"
                : SettingsPanel.Preferences.Mp4Extension;

        string preferredFolder = SettingsPanel.Preferences.DefaultOutputFolder;
        string fallbackFolder = sourceIsFolder ? source : Path.GetDirectoryName(source) ?? "";
        string folder = !string.IsNullOrWhiteSpace(preferredFolder) && Directory.Exists(preferredFolder)
            ? preferredFolder
            : fallbackFolder;

        return Path.Combine(folder, name + extension);
    }

    private void UpdateSuggestedDestination()
    {
        if (selectedSourcePaths.Count == 0) return;
        string? path = GetSuggestedOutputPath();
        DestinationText.Text = path is null
            ? "Choose a detected video title to suggest an output filename"
            : path + " (suggested only)";
    }
}

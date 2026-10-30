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

        ApplyTitleBarTheme();
        UpdatePreviewLayout();
        UpdateSuggestedDestination();
    }

    private string? selectedScannedTitleName;

    private string? destinationOverride;
    private bool updatingDestination;
    private string? GetSuggestedOutputPath() => destinationOverride ?? GetAutomaticOutputPath();
    private void Destination_Changed(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs args)
    {
        if (!updatingDestination) destinationOverride = DestinationText.Text.Trim();
    }
    private string? GetAutomaticOutputPath()
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

        string candidate = Path.Combine(folder, name + extension);
        // Never suggest overwriting the input or an existing destination.
        if (File.Exists(candidate))
        {
            for (int suffix = 1; suffix <= 999; suffix++)
            {
                string alternate = Path.Combine(folder, $"{name}-encoded-{suffix}{extension}");
                if (!File.Exists(alternate))
                    return alternate;
            }
            return null;
        }
        return candidate;
    }

    private void UpdateSuggestedDestination()
    {
        if (selectedSourcePaths.Count == 0) return;
        string? path = GetSuggestedOutputPath();
        updatingDestination = true;
        try { DestinationText.Text = path ?? string.Empty; }
        finally { updatingDestination = false; }
    }
}

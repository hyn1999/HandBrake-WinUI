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

    private void UpdateSuggestedDestination()
    {
        if (selectedSourcePaths.Count == 0) return;

        string source = selectedSourcePaths[0];
        bool sourceIsFolder = Directory.Exists(source);
        if (sourceIsFolder && string.IsNullOrWhiteSpace(selectedScannedTitleName))
        {
            DestinationText.Text = "Choose a detected video title to suggest an output filename";
            return;
        }

        string originalName = sourceIsFolder ? selectedScannedTitleName! : source;
        string name = Path.GetFileNameWithoutExtension(originalName);
        if (SettingsPanel.Preferences.ReplaceUnderscoresInFileNames)
            name = name.Replace('_', ' ');
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        string fileName = name + SettingsPanel.Preferences.Mp4Extension;
        string preferredFolder = SettingsPanel.Preferences.DefaultOutputFolder;
        string fallbackFolder = sourceIsFolder ? source : Path.GetDirectoryName(source) ?? "";
        string folder = !string.IsNullOrWhiteSpace(preferredFolder) && Directory.Exists(preferredFolder)
            ? preferredFolder
            : fallbackFolder;

        // This is a preview only; the output is never created or overwritten here.
        DestinationText.Text = Path.Combine(folder, fileName) + " (suggested only)";
    }
}

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

    private void UpdateSuggestedDestination()
    {
        if (selectedSourcePaths.Count == 0) return;

        string source = selectedSourcePaths[0];
        if (Directory.Exists(source))
        {
            DestinationText.Text = "Choose a detected video title to suggest an output filename";
            return;
        }

        string name = Path.GetFileNameWithoutExtension(source);
        if (SettingsPanel.Preferences.ReplaceUnderscoresInFileNames)
            name = name.Replace('_', ' ');

        string extension = SettingsPanel.Preferences.Mp4Extension;
        string fileName = name + extension;
        string folder = SettingsPanel.Preferences.DefaultOutputFolder;

        // A missing user-selected output directory must never become a silent write target.
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            DestinationText.Text = Path.Combine(folder, fileName) + " (suggested only)";
        }
        else
        {
            DestinationText.Text = fileName + " (suggested only)";
        }
    }
}

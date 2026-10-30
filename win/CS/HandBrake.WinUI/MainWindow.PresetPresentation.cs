using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private readonly Dictionary<NativePresetChoice, ToggleMenuFlyoutItem> presetMenuItems = new();
    private MenuFlyoutItem? makeDefaultPresetItem;
    private MenuFlyoutItem? reloadPresetItem;
    private NativePresetChoice? tooltipPreset;

    private NativePresetChoice? GetDefaultPreset()
        => nativePresetChoices.FirstOrDefault(choice => choice.Key == SettingsPanel.Preferences.DefaultPresetKey)
            ?? nativePresetChoices.FirstOrDefault(choice => choice.Preset.Default)
            ?? nativePresetChoices.FirstOrDefault(choice => choice.Key == "General/Fast 1080p30")
            ?? nativePresetChoices.FirstOrDefault();

    private void BuildPresetMenu()
    {
        presetMenuItems.Clear();
        var menu = new MenuFlyout();
        reloadPresetItem = new MenuFlyoutItem { Text = "Reload selected preset" };
        reloadPresetItem.Click += (_, _) => ApplyNativePreset();
        makeDefaultPresetItem = new MenuFlyoutItem { Text = "Set selected preset as default" };
        makeDefaultPresetItem.Click += (_, _) => SetDefaultPreset();
        ToolTipService.SetToolTip(makeDefaultPresetItem, "Use this preset's saved definition when the app starts.");
        menu.Items.Add(reloadPresetItem);
        menu.Items.Add(makeDefaultPresetItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var category in nativePresetChoices.GroupBy(choice => choice.Category))
        {
            var group = new MenuFlyoutSubItem { Text = category.Key, MinHeight = 30, Padding = new Thickness(12, 4, 12, 4) };
            foreach (var choice in category)
            {
                var item = new ToggleMenuFlyoutItem { Text = choice.Preset.PresetName, MinHeight = 30, Padding = new Thickness(12, 4, 12, 4) };
                string description = PresetHelp(choice);
                ToolTipService.SetToolTip(item, new ToolTip
                {
                    Content = new TextBlock { Text = description, MaxWidth = 340, TextWrapping = TextWrapping.Wrap }
                });
                AutomationProperties.SetHelpText(item, description);
                item.Click += (_, _) =>
                {
                    ActivePresetChoice = choice;
                    ApplyNativePreset();
                };
                presetMenuItems.Add(choice, item);
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        menu.Opened += (_, _) => UpdatePresetPresentation();
        PresetPicker.Flyout = menu;
    }

    private static string PresetHelp(NativePresetChoice choice)
    {
        string description = choice.Preset.PresetDescription?.Trim() ?? string.Empty;
        if (description.Length == 0)
            description = $"{FormatPresetContainer(choice.Preset.FileFormat)} · {choice.Preset.VideoEncoder} · " +
                $"{DisplayPresetFramerate(choice.Preset.VideoFramerate)} fps";
        return choice.DisplayName + Environment.NewLine + description;
    }

    private void SetDefaultPreset()
    {
        if (ActivePresetChoice is not { } choice) return;
        var preferences = SettingsPanel.Preferences;
        string previous = preferences.DefaultPresetKey;
        preferences.DefaultPresetKey = choice.Key;
        try { preferences.Save(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            preferences.DefaultPresetKey = previous;
            PresetStatusText.Text = "Unable to save the default preset: " + error.Message;
        }
        UpdatePresetPresentation();
    }

    private void InitializePresetTracking()
    {
        // These controls affect the job. Source/title, preview and destination
        // changes deliberately do not mark the encoding preset as modified.
        foreach (var toggle in new[] { UsePresetAudioToggle, UsePresetSubtitlesToggle, UsePresetFiltersToggle,
            CustomGeometryToggle, ChapterMarkersToggle })
        {
            toggle.Checked += (_, _) => UpdatePresetPresentation();
            toggle.Unchecked += (_, _) => UpdatePresetPresentation();
        }
        foreach (var text in new[] { MetadataTitleBox, MetadataArtistBox, MetadataAlbumBox, MetadataCommentBox })
            text.TextChanged += (_, _) => UpdatePresetPresentation();
        VideoEncoderPicker.SelectionChanged += (_, _) => UpdatePresetPresentation();
        VideoRateModePicker.SelectionChanged += (_, _) => UpdatePresetPresentation();
        VideoQualitySlider.ValueChanged += (_, _) => UpdatePresetPresentation();
        VideoBitrateBox.ValueChanged += (_, _) => UpdatePresetPresentation();
    }

    private bool IsPresetModified(NativePresetChoice choice)
    {
        var preset = choice.Preset;
        if (AdvancedPresetModified()) return true;
        int mode = preset.VideoQualityType switch { 2 => 0, 1 => 1, _ => -1 };
        if (ActiveVideoEncoder?.ShortName != preset.VideoEncoder || VideoRateModePicker.SelectedIndex != mode)
            return true;
        if ((mode == 0 && !NearlyEqual(VideoQualitySlider.Value, preset.VideoQualitySlider)) ||
            (mode == 1 && !NearlyEqual(VideoBitrateBox.Value, preset.VideoAvgBitrate ?? 2500)))
            return true;
        if (UsePresetAudioToggle.IsChecked != true || UsePresetSubtitlesToggle.IsChecked != true ||
            UsePresetFiltersToggle.IsChecked != true || CustomGeometryToggle.IsChecked == true ||
            ChapterMarkersToggle.IsChecked != preset.ChapterMarkers)
            return true;
        if (new[] { MetadataTitleBox, MetadataArtistBox, MetadataAlbumBox, MetadataCommentBox }
            .Any(box => !string.IsNullOrWhiteSpace(box.Text)))
            return true;
        if (SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle title } && title.ChapterList is { } chapters)
            return chapterNameEditors.Count != chapters.Count || chapterNameEditors
                .Where((editor, index) => editor.Text.Trim() != (chapters[index].Name ?? string.Empty).Trim()).Any();
        return false;
    }

    private static bool NearlyEqual(double left, double right)
        => double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) < 0.0001;

    private void UpdatePresetPresentation()
    {
        if (updatingVideoEditor || ActivePresetChoice is not { } choice) return;
        bool modified = IsPresetModified(choice);
        var defaultPreset = GetDefaultPreset();
        bool isDefault = choice == defaultPreset;
        PresetSelectionText.Text = choice.DisplayName;
        DefaultPresetBadge.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
        ModifiedPresetBadge.Visibility = modified ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(PresetPicker, choice.DisplayName + (isDefault ? ", default preset" : "") + (modified ? ", modified" : ""));
        AutomationProperties.SetHelpText(PresetPicker, PresetHelp(choice));
        if (tooltipPreset != choice)
        {
            tooltipPreset = choice;
            ToolTipService.SetToolTip(PresetPicker, new ToolTip
            {
                Content = new TextBlock { Text = PresetHelp(choice), MaxWidth = 340, TextWrapping = TextWrapping.Wrap }
            });
        }
        foreach (var entry in presetMenuItems)
        {
            entry.Value.IsChecked = entry.Key == choice;
            entry.Value.Text = entry.Key.Preset.PresetName + (entry.Key == defaultPreset ? " (Default)" : "") +
                (entry.Key == choice && modified ? " (Modified)" : "");
        }
        if (makeDefaultPresetItem is not null) makeDefaultPresetItem.IsEnabled = !isDefault;
        if (reloadPresetItem is not null) reloadPresetItem.IsEnabled = modified;
    }
}

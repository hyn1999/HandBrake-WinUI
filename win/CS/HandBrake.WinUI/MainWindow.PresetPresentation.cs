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
    private readonly Dictionary<NativePresetChoice, ComboBoxItem> presetPickerItems = new();
    private MenuFlyoutItem? makeDefaultPresetItem;
    private string? presetTooltipText;
    private bool updatingPresetPicker;

    private NativePresetChoice? GetDefaultPreset()
        => nativePresetChoices.FirstOrDefault(choice => choice.Key == SettingsPanel.Preferences.DefaultPresetKey)
            ?? nativePresetChoices.FirstOrDefault(choice => choice.Preset.Default)
            ?? nativePresetChoices.FirstOrDefault(choice => choice.Key == "General/Fast 1080p30")
            ?? nativePresetChoices.FirstOrDefault();

    private void BuildPresetPicker()
    {
        updatingPresetPicker = true;
        try
        {
            presetPickerItems.Clear();
            PresetPicker.Items.Clear();
            foreach (var category in nativePresetChoices.GroupBy(choice => choice.Category))
            {
                // Disabled native items are category headings, not preset choices.
                var header = Design.CompactUi.PresetCategory(category.Key);
                PresetPicker.Items.Add(header);
                foreach (var choice in category)
                {
                    var item = Design.CompactUi.PresetItem(choice.Preset.PresetName, choice);
                    ToolTipService.SetToolTip(item, Design.CompactUi.Help(PresetHelp(choice)));
                    AutomationProperties.SetHelpText(item, PresetHelp(choice));
                    presetPickerItems.Add(choice, item);
                    PresetPicker.Items.Add(item);
                }
            }
        }
        finally { updatingPresetPicker = false; }

        var menu = new MenuFlyout();
        makeDefaultPresetItem = Design.CompactUi.MenuItem(new MenuFlyoutItem { Text = "Set selected preset as default" });
        makeDefaultPresetItem.Click += (_, _) => SetDefaultPreset();
        menu.Items.Add(makeDefaultPresetItem);
        menu.Opened += (_, _) => UpdatePresetPresentation();
        PresetActionsButton.Flyout = menu;
        UpdatePresetPickerBounds();
    }

    private void ReloadPreset_Click(object sender, RoutedEventArgs args)
    {
        ActivePresetChoice = nativePresetChoices.FirstOrDefault(item => item.Key == ActivePresetChoice?.Key)
            ?? ActivePresetChoice;
        ApplyNativePreset();
    }

    private void PresetPicker_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingPresetPicker || updatingVideoEditor) return;
        if (PresetPicker.SelectedItem is not ComboBoxItem { Tag: NativePresetChoice choice })
        {
            UpdatePresetPresentation();
            return;
        }
        ActivePresetChoice = choice;
        ApplyNativePreset();
    }

    private void PresetPicker_DropDownOpened(object sender, object args)
    {
        UpdatePresetPresentation();
        UpdatePresetPickerBounds();
    }

    private void PresetSelectorRow_SizeChanged(object sender, SizeChangedEventArgs args) => UpdatePresetPickerBounds();

    private void UpdatePresetPickerBounds()
    {
        if (PresetPicker is null || PresetSelectorRow is null || PresetPicker.XamlRoot is not { } root) return;
        // Reserve the label/action columns; a narrow card must shrink the field.
        double reserved = PresetSelectorRow.ColumnDefinitions.Take(PresetSelectorRow.ColumnDefinitions.Count - 1)
            .Where((column, index) => index != 1).Sum(column => column.ActualWidth) +
            (PresetSelectorRow.ColumnDefinitions.Count - 1) * Design.CompactUi.ControlGap;
        double available = Math.Max(0, PresetSelectorRow.ActualWidth - reserved);
        PresetPicker.Width = Math.Min(Design.CompactUi.Resource<double>("HBPresetWidth"), available);
        Design.CompactUi.SizeComboBoxPopup(PresetPicker, PresetPicker.Items.OfType<ComboBoxItem>());
    }

    private static string PresetCaption(string name, bool isDefault, bool modified)
    {
        var states = new List<string>();
        if (isDefault) states.Add("Default");
        if (modified) states.Add("Modified");
        return name + (states.Count == 0 ? "" : " (" + string.Join(", ", states) + ")");
    }

    private static string PresetHelp(NativePresetChoice choice, string? caption = null)
    {
        string description = choice.Preset.PresetDescription?.Trim() ?? string.Empty;
        if (description.Length == 0)
            description = $"{FormatPresetContainer(choice.Preset.FileFormat)} · {choice.Preset.VideoEncoder} · " +
                $"{DisplayPresetFramerate(choice.Preset.VideoFramerate)} fps";
        return choice.Category + " / " + (caption ?? choice.Preset.PresetName) + Environment.NewLine + description;
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
        var referenceChoice = nativePresetChoices.FirstOrDefault(item => item.Key == choice.Key) ?? choice;
        bool modified = IsPresetModified(referenceChoice);
        var defaultPreset = GetDefaultPreset();
        bool isDefault = choice.Key == defaultPreset?.Key;
        string selectedCaption = PresetCaption(choice.Preset.PresetName, isDefault, modified);
        string selectedHelp = PresetHelp(choice, selectedCaption);
        AutomationProperties.SetName(PresetPicker, selectedCaption);
        PresetActionsButton.IsEnabled = true;
        AutomationProperties.SetHelpText(PresetPicker, selectedHelp);
        if (presetTooltipText != selectedHelp)
        {
            presetTooltipText = selectedHelp;
            ToolTipService.SetToolTip(PresetPicker, Design.CompactUi.Help(selectedHelp));
        }
        updatingPresetPicker = true;
        try
        {
            foreach (var entry in presetPickerItems)
            {
                bool selected = entry.Key.Key == choice.Key;
                string caption = PresetCaption(entry.Key.Preset.PresetName, entry.Key.Key == defaultPreset?.Key, selected && modified);
                if (!Equals(entry.Value.Content, caption))
                {
                    entry.Value.Content = caption;
                    ToolTipService.SetToolTip(entry.Value, Design.CompactUi.Help(PresetHelp(entry.Key, caption)));
                    // Refresh the native selection box's cached content under
                    // the guard; this must not reapply/reset the current task.
                    if (selected && ReferenceEquals(PresetPicker.SelectedItem, entry.Value))
                        PresetPicker.SelectedItem = null;
                }
                AutomationProperties.SetName(entry.Value, entry.Key.Category + " / " + caption);
                if (selected) PresetPicker.SelectedItem = entry.Value;
            }
        }
        finally { updatingPresetPicker = false; }
        if (makeDefaultPresetItem is not null) makeDefaultPresetItem.IsEnabled = !isDefault;
        ReloadPresetButton.IsEnabled = modified;
    }
}

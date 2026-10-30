using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop.Json.Presets;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    // The original stays in the queue until Save succeeds. Never edit the live
    // object: cancel, failed scans and unavailable capabilities must be harmless.
    private QueueDraft? queueEditSnapshot;
    private bool queueEditReady;

    private void EditQueueJob_Click(object sender, RoutedEventArgs args)
    {
        if (encodeRunning || preparingEncodeQueue || scanInProgress ||
            DraftQueueList.SelectedItem is not QueueDraft { HasVideoConfiguration: true } job) return;
        if (job.SnapshotVersion != 1 || string.IsNullOrWhiteSpace(job.NativePresetJson))
        {
            QueueStatusText.Text = "This job uses an unsupported or incomplete snapshot. Open its source and configure a new job.";
            return;
        }
        if (!File.Exists(job.SourcePath) && !Directory.Exists(job.SourcePath))
        {
            QueueStatusText.Text = "The saved source is unavailable. Restore its original path before editing this job.";
            return;
        }
        AcceptSourcePaths(new[] { job.SourcePath }, job);
    }

    private void EndQueueEdit()
    {
        queueEditSnapshot = null;
        queueEditReady = false;
        StageQueueButton.Label = "Add to queue";
        ToolTipService.SetToolTip(StageQueueButton, "Add the configured title to the queue");
        CancelQueueEditButton.Visibility = Visibility.Collapsed;
    }

    private void CancelQueueEdit_Click(object sender, RoutedEventArgs args)
    {
        EndQueueEdit();
        SourceStatusText.Text = "Queue edit canceled. The original job is unchanged.";
        UpdateQueueUi();
    }

    private static void SelectSavedChoice(ComboBox picker, string? value)
    {
        string key = value ?? "";
        var item = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (item.Tag as string ?? "") == key);
        if (item is null && picker.Items.Count == 0 && key.Length == 0) return;
        if (item is null) throw new InvalidOperationException($"Saved option '{key}' is unavailable in this engine.");
        picker.SelectedItem = item;
    }

    private void RestoreQueueEditAfterScan()
    {
        if (queueEditSnapshot is not { } saved) return;
        try
        {
            var item = SourceTitlePicker.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(entry => entry.Tag is SourceTitle scannedTitle && scannedTitle.Index == saved.TitleIndex);
            if (item?.Tag is not SourceTitle title)
                throw new InvalidOperationException("The saved title was not found in the rescanned source.");
            SourceTitlePicker.SelectedItem = item;
            var catalog = nativePresetChoices.FirstOrDefault(choice => choice.Key == saved.PresetKey)
                ?? throw new InvalidOperationException("The saved preset is unavailable in this engine.");
            // Capture the catalog's normalized editor baseline so Modified is
            // measured against the preset, not against the saved job itself.
            ActivePresetChoice = catalog;
            ApplyNativePreset();
            string? catalogAdvancedBaseline = advancedPresetBaseline;
            var preset = JsonSerializer.Deserialize<HBPreset>(saved.NativePresetJson!)
                ?? throw new InvalidOperationException("The saved preset snapshot is invalid.");
            preset.VideoEncoder = saved.VideoEncoder;
            preset.VideoQualityType = saved.VideoRateControl == "ConstantQuality" ? 2 : 1;
            if (saved.VideoQuality.HasValue) preset.VideoQualitySlider = saved.VideoQuality.Value;
            if (saved.VideoBitrateKbps.HasValue) preset.VideoAvgBitrate = saved.VideoBitrateKbps;
            if (saved.VideoFramerate is not null) preset.VideoFramerate = saved.VideoFramerate;
            if (saved.VideoEncoderSpeed is not null) preset.VideoPreset = saved.VideoEncoderSpeed;
            ActivePresetChoice = new NativePresetChoice
            {
                Key = catalog.Key, Category = catalog.Category, DisplayName = catalog.DisplayName,
                Preset = preset, Json = saved.NativePresetJson!
            };
            ApplyNativePreset();
            advancedPresetBaseline = catalogAdvancedBaseline;
            if (ActiveVideoEncoder?.ShortName != saved.VideoEncoder)
                throw new InvalidOperationException("The saved video encoder is unavailable.");
            if (saved.VideoQuality.HasValue && !NearlyEqual(VideoQualitySlider.Value, saved.VideoQuality.Value))
                throw new InvalidOperationException("The saved quality is outside the current encoder range.");

            var selection = saved.Selection ?? new EncodeSelection(1, "all", 0, 0);
            ValidateSelection(selection, title);
            SelectSavedChoice(anglePicker, selection.Angle.ToString());
            SelectSavedChoice(sourceRangePicker, selection.Type);
            if (selection.Type != "all")
            {
                rangeStartBox.Value = selection.Start;
                rangeEndBox.Value = selection.End;
            }
            UpdateRangeDuration();
            CustomGeometryToggle.IsChecked = saved.Geometry is not null;
            if (saved.Geometry is { } geometry)
            {
                ValidateGeometry(geometry, title, JsonNode.Parse(saved.NativePresetJson!)!.AsObject());
                OutputWidthBox.Value = geometry.Width; OutputHeightBox.Value = geometry.Height;
                CropTopBox.Value = geometry.Top; CropBottomBox.Value = geometry.Bottom;
                CropLeftBox.Value = geometry.Left; CropRightBox.Value = geometry.Right;
            }

            UsePresetAudioToggle.IsChecked = saved.UsePresetAudio;
            if (!saved.UsePresetAudio)
            {
                if (saved.AudioTracks is null) throw new InvalidOperationException("Legacy audio selections cannot be restored losslessly.");
                SelectAudioTracks(saved, title);
                foreach (var editor in audioEditors)
                {
                    var track = saved.AudioTracks.FirstOrDefault(track => track.TrackNumber == editor.TrackNumber);
                    editor.Selected.IsChecked = track is not null;
                    if (track is null) continue;
                    editor.Name.Text = track.Name;
                    SelectSavedChoice(editor.Encoder, track.Encoder);
                    if (track.BitrateKbps.HasValue) editor.Bitrate.Value = track.BitrateKbps.Value;
                }
            }
            UsePresetSubtitlesToggle.IsChecked = saved.UsePresetSubtitles;
            BuildSubtitleTracks(saved.UsePresetSubtitles ? null : saved.Subtitles, title, CurrentMux);
            foreach (var editor in subtitleEditors)
            {
                var track = saved.Subtitles?.FirstOrDefault(track => track.TrackNumber == editor.TrackNumber);
                editor.Selected.IsChecked = track is not null;
                if (track is null) continue;
                editor.Name.Text = track.Name; editor.Offset.Value = track.Offset;
                editor.Forced.IsChecked = track.Forced; editor.Burn.IsChecked = track.Burn;
                editor.Default.IsChecked = track.Default;
            }
            ForeignAudioSearchToggle.IsChecked = saved.ForeignAudioSearch;
            ForeignAudioForcedToggle.IsChecked = saved.ForeignAudioForced;
            ForeignAudioBurnToggle.IsChecked = saved.ForeignAudioBurn;
            importedSubtitleEditors.Clear(); ImportedSubtitleList.Children.Clear();
            foreach (var import in saved.SubtitleImports ?? new()) AddImportedSubtitleEditor(import);

            UsePresetFiltersToggle.IsChecked = saved.UsePresetFilters;
            foreach (var filter in saved.FilterOverrides ?? new())
            {
                var editor = filterEditors.FirstOrDefault(editor => editor.Id == filter.Id)
                    ?? throw new InvalidOperationException("A saved filter is unavailable in this editor.");
                SelectSavedChoice(editor.Preset, filter.Preset);
                SelectSavedChoice(editor.Tune, filter.Tune);
                editor.Enabled.IsChecked = true;
            }
            ChapterMarkersToggle.IsChecked = saved.ChapterMarkers ?? preset.ChapterMarkers;
            if (saved.ChapterNames is { } names)
            {
                if (names.Count != chapterNameEditors.Count)
                    throw new InvalidOperationException("The source chapter count changed.");
                for (int i = 0; i < names.Count; i++) chapterNameEditors[i].Text = names[i];
            }
            MetadataTitleBox.Text = saved.MetadataOverrides?.GetValueOrDefault("Name") ?? "";
            MetadataArtistBox.Text = saved.MetadataOverrides?.GetValueOrDefault("Artist") ?? "";
            MetadataAlbumBox.Text = saved.MetadataOverrides?.GetValueOrDefault("Album") ?? "";
            MetadataCommentBox.Text = saved.MetadataOverrides?.GetValueOrDefault("Comment") ?? "";
            destinationOverride = saved.DestinationPath;
            UpdateSuggestedDestination();
            // Reuse capture-time validation (including imports and native filters).
            var restored = CreateDraft(saved.SourcePath, true);
            if (!restored.HasVideoConfiguration) throw new InvalidOperationException("The saved configuration is incompatible with this engine.");
            PreserveQueueEditExtensions(saved, restored);
            if (scanInstance is null) throw new InvalidOperationException("The native scanner is unavailable.");
            // Compare resolved jobs, not UI values: native defaults and inherited
            // preset fields matter even when they have no visible control.
            var expected = JsonNode.Parse(BuildNativeJob(saved, title, scanInstance));
            var actual = JsonNode.Parse(BuildNativeJob(restored, title, scanInstance));
            if (!JsonNode.DeepEquals(expected, actual))
                throw new InvalidOperationException("This editor cannot restore every saved setting without changes.");
            queueEditReady = true;
            SourceStatusText.Text = $"Editing queued job: {saved.OutputName}. Save changes updates the original; Cancel edit leaves it unchanged.";
            StageQueueButton.Label = "Save changes";
            ToolTipService.SetToolTip(StageQueueButton, "Save changes to the original queued job");
            UpdatePresetPresentation(); RefreshOutputSummary(); UpdateEncodeControls();
            _ = LoadPreviewAsync(title);
        }
        catch (Exception error)
        {
            // Keep editing blocked until explicitly canceled or another source
            // is opened; never allow a partial restore to replace the original.
            queueEditReady = false;
            SourceStatusText.Text = "Unable to restore queue edit: " + error.Message + " The queued job is unchanged. Cancel edit to continue.";
            UpdateEncodeControls();
        }
    }

    private void SaveQueueEdit()
    {
        if (!queueEditReady || queueEditSnapshot is not { } saved || encodeRunning || preparingEncodeQueue) return;
        try
        {
            int index = draftQueue.ToList().FindIndex(job => job.Id == saved.Id);
            if (index < 0) throw new InvalidOperationException("The original queue item is no longer available.");
            var updated = CreateDraft(saved.SourcePath, true);
            if (!updated.HasVideoConfiguration) throw new InvalidOperationException("Choose compatible output settings before saving.");
            string output = Path.GetFullPath(updated.DestinationPath!);
            if (string.Equals(output, Path.GetFullPath(updated.SourcePath), StringComparison.OrdinalIgnoreCase) ||
                draftQueue.Where(job => job.Id != saved.Id).Any(job =>
                    string.Equals(output, Path.GetFullPath(job.SourcePath), StringComparison.OrdinalIgnoreCase) ||
                    (job.IsPending && string.Equals(output, Path.GetFullPath(job.DestinationPath!), StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Choose an output that does not overwrite a source or another waiting job.");
            PreserveQueueEditExtensions(saved, updated);
            if (SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title } || scanInstance is null)
                throw new InvalidOperationException("The scanned source is unavailable.");
            BuildNativeJob(updated, title, scanInstance);
            updated.Id = saved.Id;
            draftQueue[index] = updated;
            EndQueueEdit();
            SaveDraftQueue();
            DraftQueueList.SelectedItem = updated;
            QueueStatusText.Text = "Job updated and reset to Waiting. Encoding has not started.";
            UpdateQueueUi();
            AppNavigation.SelectedItem = AppNavigation.MenuItems[1];
        }
        catch (Exception error) { SourceStatusText.Text = "Unable to save queue edit: " + error.Message; }
    }

    private static void PreserveQueueEditExtensions(QueueDraft saved, QueueDraft updated)
    {
        updated.AdditionalData = saved.AdditionalData;
        // The snapshot is a detached copy; retaining unknown fields here never
        // mutates the live queue entry or replaces the four editable fields.
        foreach (var field in saved.MetadataOverrides ?? new())
            if (field.Key is not ("Name" or "Artist" or "Album" or "Comment"))
                updated.MetadataOverrides![field.Key] = field.Value;
    }
}

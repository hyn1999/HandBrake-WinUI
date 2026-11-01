using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HandBrake.Interop.Interop;
using HandBrake.WinUI.Resources;
using HandBrake.Interop.Interop.Interfaces.Model.Encoders;
using HandBrake.Interop.Interop.Json.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HandBrake.WinUI;

/// <summary>
/// The preset catalog and encoder capabilities come from the *matching* native
/// libhb, not from a hard-coded list or copied WPF resources. No encode is started.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class NativePresetChoice
    {
        public required string Key { get; init; }
        public required string Category { get; init; }
        public required string DisplayName { get; init; }
        public required HBPreset Preset { get; init; }
        public required string Json { get; init; }
    }

    private bool presetLoadAttempted;
    private bool updatingVideoEditor;
    private HBPreset? activePreset;
    private readonly List<HBVideoEncoder> nativeVideoEncoders = new();
    private readonly List<NativePresetChoice> nativePresetChoices = new();

    private NativePresetChoice? ActivePresetChoice { get; set; }

    private HBVideoEncoder? ActiveVideoEncoder
        => (VideoEncoderPicker.SelectedItem as ComboBoxItem)?.Tag as HBVideoEncoder;

    private void InitializePresetCatalog()
    {
        if (presetLoadAttempted) return;
        presetLoadAttempted = true;

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "hb.dll")))
        {
            SourceStatusText.Text = "Native presets unavailable: the matching hb.dll is missing. No sample presets are substituted.";
            return;
        }

        try
        {
            // HandBrakePresetService requires hb_init, not just hb_global_init.
            // Reuse the same native instance later for source scanning.
            scanInstance ??= CreateScanInstance();
            using var catalog = JsonDocument.Parse(HandBrakePresetService.GetBuiltInPresetsJson());

            var choices = new List<NativePresetChoice>();
            foreach (var categoryJson in catalog.RootElement.EnumerateArray())
            {
                if (!categoryJson.TryGetProperty("ChildrenArray", out var children)) continue;
                string categoryName = categoryJson.GetProperty("PresetName").GetString() ?? "Other";
                foreach (var presetJson in children.EnumerateArray())
                {
                    var preset = presetJson.Deserialize<HBPreset>();
                    if (preset is null || preset.Folder || preset.PresetDisabled ||
                        string.IsNullOrWhiteSpace(preset.PresetName))
                        continue;

                    choices.Add(new NativePresetChoice
                    {
                        Key = categoryName + "/" + preset.PresetName,
                        Category = categoryName,
                        DisplayName = categoryName + " / " + preset.PresetName,
                        Preset = preset,
                        Json = presetJson.GetRawText()
                    });
                }
            }

            if (choices.Count == 0)
                throw new InvalidOperationException("No usable built-in presets were returned by libhb.");

            nativePresetChoices.Clear();
            nativePresetChoices.AddRange(choices);
            nativeVideoEncoders.Clear();
            nativeVideoEncoders.AddRange(HandBrakeEncoderHelpers.VideoEncoders);
            if (nativeVideoEncoders.Count == 0)
                throw new InvalidOperationException("libhb returned no video encoders.");

            updatingVideoEditor = true;
            VideoEncoderPicker.Items.Clear();

            foreach (var encoder in nativeVideoEncoders)
            {
                VideoEncoderPicker.Items.Add(new ComboBoxItem
                {
                    Content = encoder.DisplayName,
                    Tag = encoder
                });
            }

            BuildPresetPicker();
            ActivePresetChoice = GetDefaultPreset();
            PresetPicker.IsEnabled = true;
            updatingVideoEditor = false;
            ApplyNativePreset();
            InitializeFilterEditor();
            UpdateQueueUi();
        }
        catch (Exception ex)
        {
            updatingVideoEditor = false;
            activePreset = null;
            ActivePresetChoice = null;
            PresetPicker.PlaceholderText = "Presets unavailable";
            PresetPicker.Items.Clear();
            presetPickerItems.Clear();
            PresetActionsButton.IsEnabled = false;
            ReloadPresetButton.IsEnabled = false;
            nativePresetChoices.Clear();
            PresetPicker.IsEnabled = false;
            VideoEncoderPicker.IsEnabled = false;
            ConstantQualityChoice.IsEnabled = averageBitrateChoice.IsEnabled = false;
            VideoQualitySlider.IsEnabled = false;
            VideoBitrateBox.IsEnabled = false;
            SourceStatusText.Text = "Could not load presets from the matching native HandBrake engine: " + ex.Message;
            UpdateQueueUi();
        }
    }

    private void ApplyNativePreset()
    {
        var choice = ActivePresetChoice;
        if (choice is null) return;
        activePreset = choice.Preset;
        updatingVideoEditor = true;
        try
        {
            UsePresetAudioToggle.IsChecked = UsePresetSubtitlesToggle.IsChecked = true;
            CustomGeometryToggle.IsChecked = false;
            InitializeFilterEditor();
            LoadFilterPreset();
            ChapterMarkersToggle.IsChecked = activePreset.ChapterMarkers;
            MetadataTitleBox.Text = MetadataArtistBox.Text = MetadataAlbumBox.Text = MetadataCommentBox.Text = string.Empty;
            if (SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: HandBrake.Interop.Interop.Json.Scan.SourceTitle title })
            {
                DisplayChapterEditor(title);
                ResetManualGeometryEditor(title);
            }
            var index = nativeVideoEncoders.FindIndex(x =>
                string.Equals(x.ShortName, activePreset.VideoEncoder, StringComparison.OrdinalIgnoreCase));
            VideoEncoderPicker.SelectedIndex = index;
            // Even if this preset names an unavailable encoder, let the user
            // choose another native encoder instead of trapping them.
            VideoEncoderPicker.IsEnabled = nativeVideoEncoders.Count > 0;

            if (index >= 0)
            {
                ConfigureVideoEncoder();
                VideoQualitySlider.Value = Math.Clamp(activePreset.VideoQualitySlider,
                    VideoQualitySlider.Minimum, VideoQualitySlider.Maximum);
                VideoBitrateBox.Value = Math.Clamp(activePreset.VideoAvgBitrate ?? 2500, 100, 200000);

                int requestedMode = activePreset.VideoQualityType switch { 2 => 0, 1 => 1, _ => -1 };
                if (requestedMode == 0 && !ActiveVideoEncoder!.SupportsQuality) requestedMode = ActiveVideoEncoder.SupportsBitrate ? 1 : -1;
                if (requestedMode == 1 && !ActiveVideoEncoder!.SupportsBitrate) requestedMode = ActiveVideoEncoder.SupportsQuality ? 0 : -1;
                SetVideoRateMode(requestedMode);
            }
            else
            {
                ConstantQualityChoice.IsEnabled = averageBitrateChoice.IsEnabled = false;
                VideoQualitySlider.IsEnabled = false;
                VideoBitrateBox.IsEnabled = false;
                SetVideoRateMode(-1);
            }
            LoadAdvancedPreset();
            UpdateVideoModeControls();
        }
        finally
        {
            updatingVideoEditor = false;
        }

        UpdateSuggestedDestination();
        RefreshOutputSummary();
        UpdatePresetPresentation();

        var prefs = SettingsPanel.Preferences;
        if (prefs.PreferredPresetKey != choice.Key)
        {
            prefs.PreferredPresetKey = choice.Key;
            try { prefs.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SourceStatusText.Text = "Preset selected but could not save preference: " + ex.Message;
            }
        }
    }

    private static string FormatPresetContainer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Not specified by preset";
        if (raw.Contains("mp4", StringComparison.OrdinalIgnoreCase)) return "MP4";
        if (raw.Contains("mkv", StringComparison.OrdinalIgnoreCase)) return "MKV";
        if (raw.Contains("webm", StringComparison.OrdinalIgnoreCase)) return "WebM";
        return raw;
    }

    private static string DisplayPresetFramerate(string? raw)
        => string.IsNullOrWhiteSpace(raw) || raw == "auto" ? WpfText.Label("VideoView_SameAsSource") : raw;

    private void VideoEncoderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingVideoEditor) return;
        updatingVideoEditor = true;
        try
        {
            ConfigureVideoEncoder();
            ConfigureAdvancedEncoder();
            UpdateVideoModeControls();
        }
        finally { updatingVideoEditor = false; }
        RefreshOutputSummary();
        UpdatePresetPresentation();
    }

    private void ConfigureVideoEncoder()
    {
        var encoder = ActiveVideoEncoder;
        qualityAdjustmentAvailable = false;
        if (encoder is null)
        {
            ConstantQualityChoice.IsEnabled = averageBitrateChoice.IsEnabled = false;
            VideoQualitySlider.IsEnabled = VideoBitrateBox.IsEnabled = false;
            SetVideoRateMode(-1);
            return;
        }
        bool supportsQuality = encoder.SupportsQuality, supportsBitrate = encoder.SupportsBitrate;
        ConstantQualityChoice.IsEnabled = supportsQuality;
        averageBitrateChoice.IsEnabled = supportsBitrate;
        if (supportsQuality)
        {
            var limits = HandBrakeEncoderHelpers.GetVideoQualityLimits(encoder);
            if (limits is not null && limits.High >= limits.Low)
            {
                VideoQualitySlider.Minimum = limits.Low;
                VideoQualitySlider.Maximum = limits.High;
                VideoQualitySlider.StepFrequency = limits.Granularity > 0 ? limits.Granularity : 1;
                VideoQualitySlider.IsDirectionReversed = !limits.Ascending;
                VideoQualitySlider.Value = Math.Clamp(VideoQualitySlider.Value, limits.Low, limits.High);
                qualityAdjustmentAvailable = encoder.SupportsQualityAdjustment && limits.High > limits.Low;
            }
        }
        int selected = videoRateMode;
        if (selected == 0 && !supportsQuality) selected = supportsBitrate ? 1 : -1;
        else if (selected == 1 && !supportsBitrate) selected = supportsQuality ? 0 : -1;
        else if (selected < 0) selected = supportsQuality ? 0 : supportsBitrate ? 1 : -1;
        SetVideoRateMode(selected);
    }

    private void UpdateVideoModeControls()
    {
        UpdateAdvancedAvailability();
        var encoder = ActiveVideoEncoder;
        bool quality = videoRateMode == 0, bitrate = videoRateMode == 1;
        bool supportsQuality = encoder?.SupportsQuality == true, supportsBitrate = encoder?.SupportsBitrate == true;
        ConstantQualityChoice.IsEnabled = supportsQuality;
        averageBitrateChoice.IsEnabled = supportsBitrate;
        QualityControlPanel.Visibility = supportsQuality ? Visibility.Visible : Visibility.Collapsed;
        VideoBitrateRow.Visibility = supportsBitrate ? Visibility.Visible : Visibility.Collapsed;
        VideoQualitySlider.Visibility = QualityScaleLabels.Visibility = qualityAdjustmentAvailable ? Visibility.Visible : Visibility.Collapsed;
        VideoQualitySlider.IsEnabled = supportsQuality && quality && qualityAdjustmentAvailable;
        VideoBitrateBox.IsEnabled = supportsBitrate && bitrate;
        VideoHighQualityText.Text = WpfText.Label(encoder?.IsX264 == true ? "Video_PlaceboQuality" : "Video_HigherQuality");
        UpdateVideoQualityText();
        VideoConfigurationStatusText.Text = ActivePresetChoice is null ? "" : encoder is null
            ? "The selected encoder is unavailable. Choose another encoder."
            : !IsContainerCompatible(CurrentContainer, encoder) ? "Choose an encoder compatible with the output format."
            : videoRateMode < 0 ? "Choose a supported quality or bitrate mode."
            : bitrate && (!double.IsFinite(VideoBitrateBox.Value) || VideoBitrateBox.Value < 100 || VideoBitrateBox.Value > 200000)
                ? "Enter a valid average bitrate." : "";
        VideoConfigurationStatusText.Visibility = VideoConfigurationStatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsContainerCompatible(string container, HBVideoEncoder encoder)
        => container switch
        {
            "MP4" => encoder.SupportsMP4,
            "MKV" => encoder.SupportsMKV,
            "WebM" => encoder.SupportsWebM,
            _ => false
        };

    private void VideoQualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!updatingVideoEditor) { UpdateVideoQualityText(); RefreshOutputSummary(); }
    }

    private void VideoBitrateBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (!updatingVideoEditor) { UpdateVideoModeControls(); RefreshOutputSummary(); }
    }

    private void UpdateVideoQualityText()
    {
        var encoder = ActiveVideoEncoder;
        VideoQualityText.Text = encoder is null ? "—" : encoder.IsLossless
            ? WpfText.Label("VideoView_LosslessEncodeOnly")
            : $"{VideoQualitySlider.Value:0.##} {HandBrakeEncoderHelpers.GetVideoQualityRateControlName(encoder.ShortName)}";
    }

    private bool TryGetVideoDraftSettings(out VideoDraftSettings settings)
    {
        settings = null!;
        var preset = ActivePresetChoice;
        var encoder = ActiveVideoEncoder;
        if (preset is null || encoder is null) return false;

        bool quality = videoRateMode == 0;
        bool bitrate = videoRateMode == 1;
        if (!quality && !bitrate) return false;
        if (quality && !encoder.SupportsQuality) return false;
        if (bitrate && !encoder.SupportsBitrate) return false;
        if (bitrate && (double.IsNaN(VideoBitrateBox.Value) ||
                        VideoBitrateBox.Value < 100 || VideoBitrateBox.Value > 200000))
            return false;

        // Native encoder capabilities are muxer-specific. Do not stage a
        // video configuration the selected HandBrake container cannot mux.
        string container = CurrentContainer;
        if (!IsContainerCompatible(container, encoder)) return false;

        settings = new VideoDraftSettings(
            preset.Key,
            preset.Preset.PresetName,
            container,
            encoder.ShortName,
            quality ? "ConstantQuality" : "AverageBitrate",
            quality ? VideoQualitySlider.Value : null,
            bitrate ? (int)Math.Round(VideoBitrateBox.Value) : null,
            ChoiceValue(frameRatePicker),
            ChoiceValue(speedPicker));
        return true;
    }

    private sealed record VideoDraftSettings(
        string PresetKey, string PresetName, string Container,
        string VideoEncoder, string RateControl, double? Quality,
        int? BitrateKbps, string? Framerate, string? EncoderSpeed);
}



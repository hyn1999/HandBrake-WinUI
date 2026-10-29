using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HandBrake.Interop.Interop;
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
        public required string DisplayName { get; init; }
        public required HBPreset Preset { get; init; }
    }

    private bool presetLoadAttempted;
    private bool updatingVideoEditor;
    private HBPreset? activePreset;
    private readonly List<HBVideoEncoder> nativeVideoEncoders = new();

    private NativePresetChoice? ActivePresetChoice
        => (PresetPicker.SelectedItem as ComboBoxItem)?.Tag as NativePresetChoice;

    private HBVideoEncoder? ActiveVideoEncoder
        => (VideoEncoderPicker.SelectedItem as ComboBoxItem)?.Tag as HBVideoEncoder;

    private void InitializePresetCatalog()
    {
        if (presetLoadAttempted) return;
        presetLoadAttempted = true;

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "hb.dll")))
        {
            PresetStatusText.Text = "Native presets unavailable: the matching hb.dll is missing. No sample presets are substituted.";
            return;
        }

        try
        {
            HandBrakeUtils.EnsureGlobalInit(initNoHardwareMode: true);
            var categories = HandBrakePresetService.GetBuiltInPresets();
            if (categories is null) throw new InvalidOperationException("libhb returned no preset catalog.");

            var choices = new List<NativePresetChoice>();
            foreach (var category in categories)
            {
                if (category.ChildrenArray is null) continue;
                foreach (var preset in category.ChildrenArray)
                {
                    if (preset is null || preset.Folder || preset.PresetDisabled ||
                        string.IsNullOrWhiteSpace(preset.PresetName))
                        continue;

                    string categoryName = category.PresetName ?? "Other";
                    choices.Add(new NativePresetChoice
                    {
                        Key = categoryName + "/" + preset.PresetName,
                        DisplayName = categoryName + " / " + preset.PresetName,
                        Preset = preset
                    });
                }
            }

            if (choices.Count == 0)
                throw new InvalidOperationException("No usable built-in presets were returned by libhb.");

            nativeVideoEncoders.Clear();
            nativeVideoEncoders.AddRange(HandBrakeEncoderHelpers.VideoEncoders);
            if (nativeVideoEncoders.Count == 0)
                throw new InvalidOperationException("libhb returned no video encoders.");

            updatingVideoEditor = true;
            PresetPicker.Items.Clear();
            VideoEncoderPicker.Items.Clear();

            foreach (var encoder in nativeVideoEncoders)
            {
                VideoEncoderPicker.Items.Add(new ComboBoxItem
                {
                    Content = encoder.DisplayName + (encoder.IsHardwareEncoder ? " (hardware — not verified)" : ""),
                    Tag = encoder
                });
            }

            foreach (var choice in choices)
            {
                PresetPicker.Items.Add(new ComboBoxItem { Content = choice.DisplayName, Tag = choice });
            }

            string preferred = SettingsPanel.Preferences.PreferredPresetKey;
            int chosen = choices.FindIndex(x => x.Key == preferred);
            if (chosen < 0) chosen = choices.FindIndex(x => x.Preset.Default);
            if (chosen < 0) chosen = choices.FindIndex(x => x.Preset.PresetName == "Fast 1080p30");
            PresetPicker.SelectedIndex = chosen < 0 ? 0 : chosen;
            PresetPicker.IsEnabled = true;
            updatingVideoEditor = false;
            ApplyNativePreset();
            PresetStatusText.Text = $"{choices.Count} real built-in presets and {nativeVideoEncoders.Count} encoder definitions loaded from libhb.";
        }
        catch (Exception ex)
        {
            updatingVideoEditor = false;
            activePreset = null;
            PresetPicker.IsEnabled = false;
            VideoEncoderPicker.IsEnabled = false;
            VideoRateModePicker.IsEnabled = false;
            VideoQualitySlider.IsEnabled = false;
            VideoBitrateBox.IsEnabled = false;
            PresetStatusText.Text = "Could not load presets from the matching native HandBrake engine: " + ex.Message;
        }
    }

    private void PresetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingVideoEditor) return;
        ApplyNativePreset();
    }

    private void ApplyNativePreset()
    {
        var choice = ActivePresetChoice;
        if (choice is null) return;
        activePreset = choice.Preset;

        updatingVideoEditor = true;
        try
        {
            PresetDescriptionText.Text = string.IsNullOrWhiteSpace(activePreset.PresetDescription)
                ? choice.DisplayName
                : choice.DisplayName + "\n" + activePreset.PresetDescription;
            PresetContainerText.Text = FormatPresetContainer(activePreset.FileFormat);
            PresetVideoDetailsText.Text =
                $"Frame rate: {DisplayPresetFramerate(activePreset.VideoFramerate)} " +
                $"({activePreset.VideoFramerateMode ?? "preset default"})\n" +
                $"Encoder speed: {activePreset.VideoPreset ?? "default"}" +
                (string.IsNullOrWhiteSpace(activePreset.VideoTune)
                    ? "" : $" · Tune: {activePreset.VideoTune}");

            var index = nativeVideoEncoders.FindIndex(x =>
                string.Equals(x.ShortName, activePreset.VideoEncoder, StringComparison.OrdinalIgnoreCase));
            VideoEncoderPicker.SelectedIndex = index;
            VideoEncoderPicker.IsEnabled = index >= 0;

            if (index >= 0)
            {
                ConfigureVideoEncoder();
                VideoQualitySlider.Value = Math.Clamp(activePreset.VideoQualitySlider,
                    VideoQualitySlider.Minimum, VideoQualitySlider.Maximum);
                VideoBitrateBox.Value = Math.Clamp(activePreset.VideoAvgBitrate ?? 2500, 100, 200000);

                bool cq = activePreset.VideoQualityType == 2;
                VideoRateModePicker.SelectedIndex = cq ? 0 : 1;
                if (VideoRateModePicker.SelectedIndex == 0 &&
                    !((ComboBoxItem)VideoRateModePicker.Items[0]).IsEnabled)
                    VideoRateModePicker.SelectedIndex = 1;
                if (VideoRateModePicker.SelectedIndex == 1 &&
                    !((ComboBoxItem)VideoRateModePicker.Items[1]).IsEnabled)
                    VideoRateModePicker.SelectedIndex = 0;
            }
            else
            {
                VideoRateModePicker.IsEnabled = false;
                VideoQualitySlider.IsEnabled = false;
                VideoBitrateBox.IsEnabled = false;
                PresetVideoDetailsText.Text += "\nThe preset's encoder is not listed by this native build.";
            }
            UpdateVideoModeControls();
        }
        finally
        {
            updatingVideoEditor = false;
        }

        UpdateSuggestedDestination();

        var prefs = SettingsPanel.Preferences;
        if (prefs.PreferredPresetKey != choice.Key)
        {
            prefs.PreferredPresetKey = choice.Key;
            try { prefs.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PresetStatusText.Text = "Preset selected but could not save preference: " + ex.Message;
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
        => string.IsNullOrWhiteSpace(raw) || raw == "auto" ? "Same as source" : raw;

    private void VideoEncoderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingVideoEditor) return;
        updatingVideoEditor = true;
        try
        {
            ConfigureVideoEncoder();
            UpdateVideoModeControls();
        }
        finally { updatingVideoEditor = false; }
    }

    private void ConfigureVideoEncoder()
    {
        var encoder = ActiveVideoEncoder;
        if (encoder is null)
        {
            VideoRateModePicker.IsEnabled = false;
            VideoQualitySlider.IsEnabled = false;
            VideoBitrateBox.IsEnabled = false;
            return;
        }

        bool supportsQuality = encoder.SupportsQuality;
        bool supportsBitrate = encoder.SupportsBitrate;
        ((ComboBoxItem)VideoRateModePicker.Items[0]).IsEnabled = supportsQuality;
        ((ComboBoxItem)VideoRateModePicker.Items[1]).IsEnabled = supportsBitrate;
        VideoRateModePicker.IsEnabled = supportsQuality || supportsBitrate;

        if (supportsQuality)
        {
            var limits = HandBrakeEncoderHelpers.GetVideoQualityLimits(encoder);
            if (limits is not null && limits.High > limits.Low)
            {
                VideoQualitySlider.Minimum = limits.Low;
                VideoQualitySlider.Maximum = limits.High;
                VideoQualitySlider.StepFrequency = limits.Granularity > 0 ? limits.Granularity : 1;
            }
        }

        if (VideoRateModePicker.SelectedIndex == 0 && !supportsQuality)
            VideoRateModePicker.SelectedIndex = supportsBitrate ? 1 : -1;
        else if (VideoRateModePicker.SelectedIndex == 1 && !supportsBitrate)
            VideoRateModePicker.SelectedIndex = supportsQuality ? 0 : -1;
        else if (VideoRateModePicker.SelectedIndex < 0)
            VideoRateModePicker.SelectedIndex = supportsQuality ? 0 : supportsBitrate ? 1 : -1;
    }

    private void VideoRateModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingVideoEditor) return;
        UpdateVideoModeControls();
    }

    private void UpdateVideoModeControls()
    {
        bool hasEncoder = ActiveVideoEncoder is not null;
        bool quality = VideoRateModePicker.SelectedIndex == 0;
        bool bitrate = VideoRateModePicker.SelectedIndex == 1;
        QualityControlPanel.Visibility = quality ? Visibility.Visible : Visibility.Collapsed;
        VideoBitrateBox.Visibility = bitrate ? Visibility.Visible : Visibility.Collapsed;
        VideoQualitySlider.IsEnabled = hasEncoder && quality;
        VideoBitrateBox.IsEnabled = hasEncoder && bitrate;
        UpdateVideoQualityText();
    }

    private void VideoQualitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!updatingVideoEditor) UpdateVideoQualityText();
    }

    private void VideoBitrateBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        // The queue reads the latest validated numeric value when staging.
    }

    private void UpdateVideoQualityText()
    {
        VideoQualityText.Text = ActiveVideoEncoder is null
            ? "Quality: no supported encoder"
            : $"Quality value: {VideoQualitySlider.Value:0.##} (native encoder range)";
    }

    private bool TryGetVideoDraftSettings(out VideoDraftSettings settings)
    {
        settings = null!;
        var preset = ActivePresetChoice;
        var encoder = ActiveVideoEncoder;
        if (preset is null || encoder is null) return false;

        bool quality = VideoRateModePicker.SelectedIndex == 0;
        bool bitrate = VideoRateModePicker.SelectedIndex == 1;
        if (!quality && !bitrate) return false;
        if (quality && !encoder.SupportsQuality) return false;
        if (bitrate && !encoder.SupportsBitrate) return false;
        if (bitrate && (double.IsNaN(VideoBitrateBox.Value) ||
                        VideoBitrateBox.Value < 100 || VideoBitrateBox.Value > 200000))
            return false;

        settings = new VideoDraftSettings(
            preset.Key,
            preset.Preset.PresetName,
            FormatPresetContainer(preset.Preset.FileFormat),
            encoder.ShortName,
            quality ? "ConstantQuality" : "AverageBitrate",
            quality ? VideoQualitySlider.Value : null,
            bitrate ? (int)Math.Round(VideoBitrateBox.Value) : null,
            preset.Preset.VideoFramerate,
            preset.Preset.VideoPreset);
        return true;
    }

    private sealed record VideoDraftSettings(
        string PresetKey, string PresetName, string Container,
        string VideoEncoder, string RateControl, double? Quality,
        int? BitrateKbps, string? Framerate, string? EncoderSpeed);
}

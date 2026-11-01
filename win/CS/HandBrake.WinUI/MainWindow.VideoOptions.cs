using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private readonly ComboBox outputFormatPicker = new();
    private readonly ComboBox frameRatePicker = new();
    private readonly ComboBox speedPicker = new();
    private readonly ComboBox tunePicker = new();
    private readonly ComboBox profilePicker = new();
    private readonly ComboBox levelPicker = new();
    private readonly ComboBox colorRangePicker = new();
    private readonly CheckBox webOptimize = new() { Content = "Web optimized" };
    private readonly CheckBox alignAv = new() { Content = "Align A/V start" };
    private readonly CheckBox ipodSupport = new() { Content = "iPod 5G support" };
    private readonly CheckBox metadataPassthru = new() { Content = "Pass through common metadata" };
    private readonly CheckBox multiPass = new() { Content = "Multi-pass encoding" };
    private readonly CheckBox turboPass = new() { Content = "Turbo analysis pass" };
    private readonly CheckBox fastDecode = new() { Content = "Fast decode" };
    private readonly TextBox encoderArguments = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private bool updatingAdvancedEditor;
    private string? advancedPresetBaseline;

    private static string ChoiceValue(ComboBox picker) => (picker.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private string CurrentMux => ChoiceValue(outputFormatPicker) is { Length: > 0 } mux ? mux : activePreset?.FileFormat ?? "av_mp4";
    private string CurrentContainer => FormatPresetContainer(CurrentMux);

    private static void PopulateChoices(ComboBox picker, IEnumerable<(string Label, string Value)> choices, string? selected)
    {
        picker.Items.Clear();
        foreach (var value in choices) picker.Items.Add(new ComboBoxItem { Content = value.Label, Tag = value.Value });
        picker.SelectedIndex = Enumerable.Range(0, picker.Items.Count)
            .FirstOrDefault(index => ((ComboBoxItem)picker.Items[index]).Tag as string == selected, 0);
    }

    private void InitializeAdvancedEditors()
    {
        updatingAdvancedEditor = true;
        PopulateChoices(outputFormatPicker, new[] { ("MP4", "av_mp4"), ("MKV", "av_mkv"), ("WebM", "av_webm") }, "av_mp4");
        PopulateChoices(colorRangePicker, new[] { (WpfText.Label("VideoColourRange_SameAsSource"), "auto"), (WpfText.Label("VideoColourRange_Limited"), "limited"), (WpfText.Label("VideoColourRange_Full"), "full") }, "limited");
        Design.FieldLayout.SetRole(outputFormatPicker, Design.FieldWidthRole.ShortChoice);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLabeledBy(outputFormatPicker, FormatSummaryLabel);
        ContainerEditorPanel.Children.Add(outputFormatPicker);
        foreach (var option in new[] { webOptimize, alignAv, ipodSupport, metadataPassthru })
            ContainerEditorPanel.Children.Add(option);
        InitializeSummaryTooltips();
        InitializeVideoPresentation();
        foreach (var picker in new[] { outputFormatPicker, frameRatePicker, colorRangePicker,
            speedPicker, tunePicker, profilePicker, levelPicker })
            picker.SelectionChanged += AdvancedOption_Changed;
        foreach (var toggle in new[] { webOptimize, alignAv, ipodSupport, metadataPassthru, multiPass, turboPass, fastDecode })
        {
            toggle.Checked += AdvancedOption_Changed;
            toggle.Unchecked += AdvancedOption_Changed;
        }
        encoderArguments.TextChanged += AdvancedOption_Changed;
        updatingAdvancedEditor = false;
        ContainerEditorGate.IsEnabled = AdvancedVideoGate.IsEnabled = false;
    }

    private void LoadAdvancedPreset()
    {
        if (ActivePresetChoice is not { } choice) return;
        updatingAdvancedEditor = true;
        try
        {
            var preset = choice.Preset;
            var json = JsonNode.Parse(choice.Json)!;
            PopulateChoices(outputFormatPicker, new[] { ("MP4", "av_mp4"), ("MKV", "av_mkv"), ("WebM", "av_webm") }, preset.FileFormat);
            var rates = new List<(string Label, string Value)> { (WpfText.Label("VideoView_SameAsSource"), "auto") };
            rates.AddRange(HandBrakeEncoderHelpers.VideoFramerates.Select(rate => (rate.Name, rate.Name)));
            string rateValue = preset.VideoFramerate is null or "" or "0" ? "auto" : preset.VideoFramerate;
            if (!rates.Any(rate => rate.Value == rateValue))
            {
                if (!double.TryParse(rateValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double savedRate) || !double.IsFinite(savedRate) || savedRate <= 0)
                    throw new InvalidOperationException("The saved frame rate is invalid.");
                rates.Add((rateValue, rateValue));
            }
            PopulateChoices(frameRatePicker, rates, rateValue);
            SetFrameMode(preset.VideoFramerateMode);
            colorRangePicker.SelectedIndex = preset.VideoColorRange switch { "auto" => 0, "full" => 2, _ => 1 };
            webOptimize.IsChecked = preset.Optimize;
            alignAv.IsChecked = preset.AlignAVStart;
            ipodSupport.IsChecked = preset.Mp4iPodCompatible;
            metadataPassthru.IsChecked = json["MetadataPassthru"]?.GetValue<bool>() ?? true;
            multiPass.IsChecked = preset.VideoMultiPass;
            turboPass.IsChecked = preset.VideoTurboMultiPass;
            ConfigureAdvancedEncoder();
            ContainerEditorGate.IsEnabled = AdvancedVideoGate.IsEnabled = VideoBasicsPanel.IsEnabled = true;
            UpdateAdvancedAvailability();
            advancedPresetBaseline = AdvancedState();
        }
        finally { updatingAdvancedEditor = false; }
    }

    private void ConfigureAdvancedEncoder()
    {
        var encoder = ActiveVideoEncoder;
        bool fromPreset = encoder?.ShortName == activePreset?.VideoEncoder;
        static IEnumerable<(string, string)> Options(IEnumerable<string>? values, string label)
            => new[] { (label, "") }.Concat((values ?? Array.Empty<string>()).Select(value => (value, value)));
        PopulateChoices(speedPicker, Options(encoder?.Presets, WpfText.Label("SummaryView_Default")), fromPreset ? activePreset?.VideoPreset : "");
        string[] tunes = (fromPreset ? activePreset?.VideoTune : "")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        string tune = string.Join(",", tunes.Where(value => value != "fastdecode"));
        var availableTunes = (encoder?.Tunes ?? new List<string>()).Where(value => value != "fastdecode").ToList();
        if (tune.Contains(',') && tune.Split(',').All(value => availableTunes.Contains(value))) availableTunes.Add(tune);
        PopulateChoices(tunePicker, Options(availableTunes, WpfText.Label("SubtitleBehaviourModes_None")), tune);
        PopulateChoices(profilePicker, Options(encoder?.Profiles, WpfText.Label("PictureSettingsView_Automatic")), fromPreset && activePreset?.VideoProfile != "auto" ? activePreset?.VideoProfile : "");
        PopulateChoices(levelPicker, Options(encoder?.Levels, WpfText.Label("PictureSettingsView_Automatic")), fromPreset && activePreset?.VideoLevel != "auto" ? activePreset?.VideoLevel : "");
        fastDecode.IsChecked = tunes.Contains("fastdecode");
        encoderArguments.Text = fromPreset ? activePreset?.VideoOptionExtra ?? "" : "";
        UpdateAdvancedAvailability();
    }

    private void UpdateAdvancedAvailability()
    {
        var encoder = ActiveVideoEncoder;
        bool mp4 = CurrentContainer == "MP4";
        webOptimize.IsEnabled = mp4;
        ipodSupport.IsEnabled = mp4 && encoder?.IsH264 == true;
        speedPicker.IsEnabled = speedPicker.Items.Count > 1;
        tunePicker.IsEnabled = tunePicker.Items.Count > 1;
        profilePicker.IsEnabled = profilePicker.Items.Count > 1;
        levelPicker.IsEnabled = levelPicker.Items.Count > 1;
        fastDecode.IsEnabled = encoder?.Tunes?.Contains("fastdecode") == true;
        multiPass.IsEnabled = encoder?.SupportsMultiPass(videoRateMode == 0) == true;
        turboPass.IsEnabled = videoRateMode == 1 && multiPass.IsEnabled && multiPass.IsChecked == true && (encoder?.IsX264 == true || encoder?.IsX265 == true);
        multiPass.Visibility = encoder?.SupportsMultiPass() == true ? Visibility.Visible : Visibility.Collapsed;
        turboPass.Visibility = (encoder?.IsX264 == true || encoder?.IsX265 == true) && encoder.SupportsMultiPass() ? Visibility.Visible : Visibility.Collapsed;
        fastDecode.Visibility = fastDecode.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshFrameModeControls();
    }

    private JsonObject AdvancedValues() => new()
    {
        ["FileFormat"] = CurrentMux, ["Optimize"] = webOptimize.IsEnabled && webOptimize.IsChecked == true,
        ["AlignAVStart"] = alignAv.IsChecked == true, ["Mp4iPodCompatible"] = ipodSupport.IsEnabled && ipodSupport.IsChecked == true,
        ["MetadataPassthru"] = metadataPassthru.IsChecked == true,
        ["VideoFramerate"] = ChoiceValue(frameRatePicker), ["VideoFramerateMode"] = frameMode,
        ["VideoColorRange"] = ChoiceValue(colorRangePicker), ["VideoPreset"] = ChoiceValue(speedPicker),
        ["VideoTune"] = string.Join(",", new[] { ChoiceValue(tunePicker), fastDecode.IsEnabled && fastDecode.IsChecked == true ? "fastdecode" : "" }.Where(value => value.Length > 0)),
        ["VideoProfile"] = ChoiceValue(profilePicker), ["VideoLevel"] = ChoiceValue(levelPicker),
        ["VideoOptionExtra"] = encoderArguments.Text.Trim(),
        ["VideoMultiPass"] = multiPass.IsEnabled && multiPass.IsChecked == true,
        ["VideoTurboMultiPass"] = turboPass.IsEnabled && turboPass.IsChecked == true
    };
    private string AdvancedState() => AdvancedValues().ToJsonString();
    private bool AdvancedPresetModified() => advancedPresetBaseline is not null && AdvancedState() != advancedPresetBaseline;

    private string? GetEditorPresetJson()
    {
        if (ActivePresetChoice is not { } choice) return null;
        var json = JsonNode.Parse(choice.Json)!.AsObject();
        foreach (var value in AdvancedValues()) json[value.Key] = value.Value?.DeepClone();
        json["VideoEncoder"] = ActiveVideoEncoder?.ShortName;
        json["x264UseAdvancedOptions"] = false;
        ApplyFilterPresetEdits(json);
        ValidateEditedFilters();
        return json.ToJsonString();
    }

    private void AdvancedOption_Changed(object sender, object args)
    {
        if (updatingAdvancedEditor || updatingVideoEditor || ActivePresetChoice is null) return;
        updatingAdvancedEditor = true;
        try
        {
            if (sender == frameRatePicker)
            {
                bool sourceRate = ChoiceValue(frameRatePicker) is "" or "auto" or "0";
                if (sourceRate && frameMode == "pfr") SetFrameMode("vfr");
                else if (!sourceRate && frameMode == "vfr") SetFrameMode("pfr");
            }
            UpdateAdvancedAvailability();
            if (sender == outputFormatPicker && destinationOverride is not null)
                destinationOverride = System.IO.Path.ChangeExtension(destinationOverride,
                    CurrentContainer == "MKV" ? ".mkv" : CurrentContainer == "WebM" ? ".webm" : SettingsPanel.Preferences.Mp4Extension);
        }
        finally { updatingAdvancedEditor = false; }
        UpdateVideoModeControls();
        UpdateSuggestedDestination();
        RefreshOutputSummary();
    }
}

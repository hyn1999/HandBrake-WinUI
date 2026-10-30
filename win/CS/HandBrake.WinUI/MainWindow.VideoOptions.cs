using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private readonly ComboBox outputFormatPicker = new() { Header = "Format" };
    private readonly ComboBox frameRatePicker = new() { Header = "Frame rate" };
    private readonly ComboBox frameModePicker = new() { Header = "Frame-rate mode" };
    private readonly ComboBox speedPicker = new() { Header = "Encoder preset" };
    private readonly ComboBox tunePicker = new() { Header = "Tune" };
    private readonly ComboBox profilePicker = new() { Header = "Profile" };
    private readonly ComboBox levelPicker = new() { Header = "Level" };
    private readonly ComboBox colorRangePicker = new() { Header = "Colour range" };
    private readonly CheckBox webOptimize = new() { Content = "Web optimized" };
    private readonly CheckBox alignAv = new() { Content = "Align A/V start" };
    private readonly CheckBox ipodSupport = new() { Content = "iPod 5G support" };
    private readonly CheckBox metadataPassthru = new() { Content = "Pass through common metadata" };
    private readonly CheckBox multiPass = new() { Content = "Multi-pass encoding" };
    private readonly CheckBox turboPass = new() { Content = "Turbo analysis pass" };
    private readonly CheckBox fastDecode = new() { Content = "Fast decode" };
    private readonly TextBox encoderArguments = new() { Header = "Advanced encoder options", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
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
        PopulateChoices(frameModePicker, new[] { ("Variable", "vfr"), ("Constant", "cfr"), ("Peak", "pfr") }, "vfr");
        PopulateChoices(colorRangePicker, new[] { ("Limited", "limited"), ("Full", "full") }, "limited");
        ContainerEditorPanel.Children.Add(CompactRow(outputFormatPicker, webOptimize, alignAv));
        ContainerEditorPanel.Children.Add(CompactRow(ipodSupport, metadataPassthru));
        AdvancedVideoPanel.Children.Add(CompactRow(frameRatePicker, frameModePicker, colorRangePicker));
        AdvancedVideoPanel.Children.Add(CompactRow(speedPicker, tunePicker, fastDecode));
        AdvancedVideoPanel.Children.Add(CompactRow(profilePicker, levelPicker));
        AdvancedVideoPanel.Children.Add(CompactRow(multiPass, turboPass));
        AdvancedVideoPanel.Children.Add(encoderArguments);
        foreach (var picker in new[] { outputFormatPicker, frameRatePicker, frameModePicker, colorRangePicker,
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
            PopulateChoices(frameRatePicker, new[] { ("Same as source", "auto") }
                .Concat(HandBrakeEncoderHelpers.VideoFramerates.Select(rate => (rate.Name + " fps", rate.Name))), preset.VideoFramerate);
            frameModePicker.SelectedIndex = preset.VideoFramerateMode switch { "cfr" => 1, "pfr" => 2, _ => 0 };
            colorRangePicker.SelectedIndex = preset.VideoColorRange == "full" ? 1 : 0;
            webOptimize.IsChecked = preset.Optimize;
            alignAv.IsChecked = preset.AlignAVStart;
            ipodSupport.IsChecked = preset.Mp4iPodCompatible;
            metadataPassthru.IsChecked = json["MetadataPassthru"]?.GetValue<bool>() ?? true;
            multiPass.IsChecked = preset.VideoMultiPass;
            turboPass.IsChecked = preset.VideoTurboMultiPass;
            ConfigureAdvancedEncoder();
            ContainerEditorGate.IsEnabled = AdvancedVideoGate.IsEnabled = true;
            UpdateAdvancedAvailability();
            advancedPresetBaseline = AdvancedState();
        }
        finally { updatingAdvancedEditor = false; }
    }

    private void ConfigureAdvancedEncoder()
    {
        var encoder = ActiveVideoEncoder;
        bool fromPreset = encoder?.ShortName == activePreset?.VideoEncoder;
        static IEnumerable<(string, string)> Options(IEnumerable<string>? values, string label = "Default")
            => new[] { (label, "") }.Concat((values ?? Array.Empty<string>()).Select(value => (value, value)));
        PopulateChoices(speedPicker, Options(encoder?.Presets), fromPreset ? activePreset?.VideoPreset : "");
        string[] tunes = (fromPreset ? activePreset?.VideoTune : "")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        string tune = string.Join(",", tunes.Where(value => value != "fastdecode"));
        var availableTunes = (encoder?.Tunes ?? new List<string>()).Where(value => value != "fastdecode").ToList();
        if (tune.Contains(',') && tune.Split(',').All(value => availableTunes.Contains(value))) availableTunes.Add(tune);
        PopulateChoices(tunePicker, Options(availableTunes, "None"), tune);
        PopulateChoices(profilePicker, Options(encoder?.Profiles, "Auto"), fromPreset && activePreset?.VideoProfile != "auto" ? activePreset?.VideoProfile : "");
        PopulateChoices(levelPicker, Options(encoder?.Levels, "Auto"), fromPreset && activePreset?.VideoLevel != "auto" ? activePreset?.VideoLevel : "");
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
        multiPass.IsEnabled = encoder?.SupportsMultiPass(VideoRateModePicker.SelectedIndex == 0) == true;
        turboPass.IsEnabled = multiPass.IsEnabled && multiPass.IsChecked == true && (encoder?.IsX264 == true || encoder?.IsX265 == true);
        if (ChoiceValue(frameRatePicker) == "auto" && ChoiceValue(frameModePicker) == "pfr")
            frameModePicker.SelectedIndex = 0;
    }

    private JsonObject AdvancedValues() => new()
    {
        ["FileFormat"] = CurrentMux, ["Optimize"] = webOptimize.IsEnabled && webOptimize.IsChecked == true,
        ["AlignAVStart"] = alignAv.IsChecked == true, ["Mp4iPodCompatible"] = ipodSupport.IsEnabled && ipodSupport.IsChecked == true,
        ["MetadataPassthru"] = metadataPassthru.IsChecked == true,
        ["VideoFramerate"] = ChoiceValue(frameRatePicker), ["VideoFramerateMode"] = ChoiceValue(frameModePicker),
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
        return json.ToJsonString();
    }

    private void AdvancedOption_Changed(object sender, object args)
    {
        if (updatingAdvancedEditor || updatingVideoEditor || ActivePresetChoice is null) return;
        updatingAdvancedEditor = true;
        try
        {
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

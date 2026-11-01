using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Encode;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

// Retained for queued jobs saved by the previous manual-filter editor.
public sealed record FilterOverride(int Id, string? Preset, string? Tune);

public sealed partial class MainWindow
{
    private sealed record FilterAlgorithm(string Value, string Name, hb_filter_ids Id);
    private sealed record FilterDefinition(string Label, string Help, hb_filter_ids Id,
        string PresetKey, string CustomKey, string? TuneKey = null,
        string? AlgorithmKey = null, FilterAlgorithm[]? Algorithms = null);
    private sealed record FilterEditor(FilterDefinition Definition, ComboBox Choice,
        ComboBox Preset, ComboBox Tune, TextBox Custom);
    private readonly List<FilterEditor> filterEditors = new();
    private readonly JsonObject filterPresetEdits = new();
    private readonly CheckBox grayscaleChoice = new();
    private bool loadingFilters;

    // These are the legacy preset keys consumed by libhb/preset.c, not invented
    // job filter IDs. Native preset resolution owns ordering and infrastructure.
    private static readonly FilterDefinition[] FilterDefinitions =
    {
        new("FiltersView_Detelecine", "FilterView_Detelecine", hb_filter_ids.HB_FILTER_DETELECINE, "PictureDetelecine", "PictureDetelecineCustom"),
        new("FiltersView_InterlaceDetection", "FilterView_InterlaceDetection", hb_filter_ids.HB_FILTER_COMB_DETECT, "PictureCombDetectPreset", "PictureCombDetectCustom"),
        new("FiltersView_Deinterlace", "FilterView_Deinterlace", hb_filter_ids.HB_FILTER_DECOMB, "PictureDeinterlacePreset", "PictureDeinterlaceCustom", AlgorithmKey: "PictureDeinterlaceFilter", Algorithms: new[]
        {
            new FilterAlgorithm("decomb", "Decomb", hb_filter_ids.HB_FILTER_DECOMB),
            new FilterAlgorithm("deinterlace", "Yadif", hb_filter_ids.HB_FILTER_YADIF),
            new FilterAlgorithm("bwdif", "Bwdif", hb_filter_ids.HB_FILTER_BWDIF)
        }),
        new("FiltersView_Denoise", "FilterView_Denoise", hb_filter_ids.HB_FILTER_NLMEANS, "PictureDenoisePreset", "PictureDenoiseCustom", "PictureDenoiseTune", "PictureDenoiseFilter", new[]
        {
            new FilterAlgorithm("nlmeans", "NLMeans", hb_filter_ids.HB_FILTER_NLMEANS),
            new FilterAlgorithm("hqdn3d", "HQDN3D", hb_filter_ids.HB_FILTER_HQDN3D),
            new FilterAlgorithm("bm3d", "BM3D", hb_filter_ids.HB_FILTER_BM3D)
        }),
        new("FiltersView_ChromaSmooth", "FiltersView_ChromaSmooth", hb_filter_ids.HB_FILTER_CHROMA_SMOOTH, "PictureChromaSmoothPreset", "PictureChromaSmoothCustom", "PictureChromaSmoothTune"),
        new("FiltersView_Sharpen", "FilterView_Sharpen", hb_filter_ids.HB_FILTER_UNSHARP, "PictureSharpenPreset", "PictureSharpenCustom", "PictureSharpenTune", "PictureSharpenFilter", new[]
        {
            new FilterAlgorithm("unsharp", "Unsharp", hb_filter_ids.HB_FILTER_UNSHARP),
            new FilterAlgorithm("lapsharp", "Lapsharp", hb_filter_ids.HB_FILTER_LAPSHARP)
        }),
        new("FiltersView_Deblock", "FilterView_Deblock", hb_filter_ids.HB_FILTER_DEBLOCK, "PictureDeblockPreset", "PictureDeblockCustom", "PictureDeblockTune"),
        new("FiltersView_ColourSpace", "FiltersView_ColourSpace", hb_filter_ids.HB_FILTER_COLORSPACE, "PictureColorspacePreset", "PictureColorspaceCustom")
    };

    private void InitializeFilterEditor()
    {
        if (filterEditors.Count > 0) return;
        loadingFilters = true;
        try
        {
            foreach (var definition in FilterDefinitions)
            {
                var editor = new FilterEditor(definition, new ComboBox(), new ComboBox(), new ComboBox(), new TextBox());
                var label = new TextBlock { Text = WpfText.Label(definition.Label), Style = CompactUi.Resource<Style>("HBCompactFormSideLabelStyle") };
                AutomationProperties.SetLabeledBy(editor.Choice, label);
                AutomationProperties.SetName(editor.Preset, WpfText.Label("FilterView_Preset"));
                AutomationProperties.SetName(editor.Tune, WpfText.Label("FilterView_Tune"));
                AutomationProperties.SetName(editor.Custom, WpfText.Label("FilterView_Custom"));
                editor.Custom.PlaceholderText = WpfText.Label("FilterView_Custom");
                ToolTipService.SetToolTip(editor.Choice, CompactUi.Help(WpfText.Tooltip(definition.Help)));
                string? presetHelp = definition.AlgorithmKey switch
                {
                    "PictureDeinterlaceFilter" => "FilterView_DeinterlacePreset", "PictureDenoiseFilter" => "FilterView_DenoisePreset",
                    "PictureSharpenFilter" => "FilterView_SharpenPreset", _ => null
                };
                string? tuneHelp = definition.AlgorithmKey switch
                {
                    "PictureDenoiseFilter" => "FilterView_DenoiseTune", "PictureSharpenFilter" => "FilterView_SharpenTune", _ => null
                };
                string? customHelp = definition.AlgorithmKey switch
                {
                    "PictureDeinterlaceFilter" => "FilterView_DeinterlaceCustom", "PictureDenoiseFilter" => "FilterView_CustomDenoiseParams",
                    "PictureSharpenFilter" => "FilterView_CustomSharpenParams", _ => definition.Id == hb_filter_ids.HB_FILTER_COMB_DETECT ? "FilterView_InterlaceDetectionCustom" : null
                };
                ToolTipService.SetToolTip(editor.Preset, CompactUi.Help(presetHelp is null ? WpfText.Label("AVFilterView_PresetTooltip") : WpfText.Tooltip(presetHelp)));
                ToolTipService.SetToolTip(editor.Tune, CompactUi.Help(tuneHelp is null ? WpfText.Label("AVFilterView_TuneTooltip") : WpfText.Tooltip(tuneHelp)));
                ToolTipService.SetToolTip(editor.Custom, CompactUi.Help(customHelp is null ? WpfText.Label("AVFilterView_CustomOptionsTooltip") : WpfText.Tooltip(customHelp)));
                if (definition.Algorithms is { } algorithms)
                {
                    AddFilterChoice(editor.Choice, WpfText.Label("DeinterlaceFilter_Off"), "off");
                    foreach (var algorithm in algorithms) AddFilterChoice(editor.Choice, algorithm.Name, algorithm.Value);
                }
                else PopulateFilterPresets(editor.Choice, (int)definition.Id, includeOff: true, selected: "off");
                var body = CompactUi.AlignedFormRow(
                    new CompactFormField("", editor.Choice, FieldWidthRole.ShortChoice),
                    new CompactFormField("", editor.Preset, FieldWidthRole.ShortChoice),
                    new CompactFormField("", editor.Tune, FieldWidthRole.ShortChoice),
                    new CompactFormField("", editor.Custom, FieldWidthRole.Name));
                AddFilterRow(label, body);
                filterEditors.Add(editor);
                editor.Choice.SelectionChanged += (_, _) => FilterChoiceChanged(editor);
                editor.Preset.SelectionChanged += (_, _) => FilterDetailChanged(editor);
                editor.Tune.SelectionChanged += (_, _) => FilterDetailChanged(editor);
                editor.Custom.TextChanged += (_, _) => FilterDetailChanged(editor);
            }
            grayscaleChoice.Content = WpfText.Label("FiltersView_Grayscale");
            ToolTipService.SetToolTip(grayscaleChoice, CompactUi.Help(WpfText.Tooltip("FilterView_Grayscale")));
            AddFilterRow(new TextBlock(), grayscaleChoice);
            grayscaleChoice.Checked += GrayscaleChanged;
            grayscaleChoice.Unchecked += GrayscaleChanged;
            FilterEditorPanel.IsEnabled = true;
        }
        catch (Exception error)
        {
            filterEditors.Clear(); FilterEditorPanel.Children.Clear(); FilterEditorPanel.RowDefinitions.Clear();
            FilterEditorPanel.IsEnabled = false;
            ShowFilterError(error);
        }
        finally { loadingFilters = false; }
    }

    private void AddFilterRow(FrameworkElement label, FrameworkElement body)
    {
        int row = FilterEditorPanel.RowDefinitions.Count;
        FilterEditorPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, row); Grid.SetRow(body, row); Grid.SetColumn(body, 1);
        FilterEditorPanel.Children.Add(label); FilterEditorPanel.Children.Add(body);
    }

    private static void AddFilterChoice(ComboBox picker, string label, string value)
        => picker.Items.Add(new ComboBoxItem { Content = label, Tag = value });

    private static void SelectFilterChoice(ComboBox picker, string? value)
    {
        string key = value ?? "";
        var item = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == key);
        // Retain unrecognized saved values instead of silently substituting a
        // different setting when another row is edited.
        if (item is null && key.Length > 0)
        {
            item = new ComboBoxItem { Content = key, Tag = key };
            picker.Items.Add(item);
        }
        picker.SelectedItem = item;
    }

    private static void PopulateFilterPresets(ComboBox picker, int id, bool includeOff, string? selected)
    {
        picker.Items.Clear();
        if (includeOff) AddFilterChoice(picker, WpfText.Label("DeinterlaceFilter_Off"), "off");
        foreach (var value in HandBrakeFilterHelpers.GetFilterPresets(id).Where(value => value.ShortName != "off"))
            AddFilterChoice(picker, value.Name, value.ShortName);
        SelectFilterChoice(picker, selected);
    }

    private static int SelectedFilterId(FilterEditor editor)
    {
        if (editor.Definition.Algorithms is not { } algorithms) return (int)editor.Definition.Id;
        return algorithms.FirstOrDefault(algorithm => algorithm.Value == ChoiceValue(editor.Choice)) is { } selected ? (int)selected.Id : 0;
    }

    private void ConfigureFilterDetails(FilterEditor editor, string? preset, string? tune)
    {
        int id = SelectedFilterId(editor);
        if (editor.Definition.AlgorithmKey is not null)
        {
            PopulateFilterPresets(editor.Preset, id, includeOff: false, selected: preset);
            if (editor.Preset.SelectedIndex < 0)
            {
                var defaults = editor.Preset.Items.OfType<ComboBoxItem>().ToList();
                editor.Preset.SelectedItem = defaults.FirstOrDefault(item => (string?)item.Tag == "default")
                    ?? defaults.FirstOrDefault(item => (string?)item.Tag == "medium")
                    ?? defaults.FirstOrDefault(item => (string?)item.Tag != "custom");
            }
        }
        editor.Tune.Items.Clear();
        if (id != 0 && editor.Definition.TuneKey is not null)
            foreach (var value in HandBrakeFilterHelpers.GetFilterTunes(id)) AddFilterChoice(editor.Tune, value.Name, value.ShortName);
        SelectFilterChoice(editor.Tune, tune);
        if (editor.Tune.SelectedIndex < 0 && editor.Tune.Items.Count > 0)
            editor.Tune.SelectedItem = editor.Tune.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == "none") ?? editor.Tune.Items[0];
        UpdateFilterDetailVisibility(editor);
    }

    private static void UpdateFilterDetailVisibility(FilterEditor editor)
    {
        bool enabled = ChoiceValue(editor.Choice) is not ("off" or "");
        bool custom = (editor.Definition.AlgorithmKey is null ? ChoiceValue(editor.Choice) : ChoiceValue(editor.Preset)) == "custom";
        editor.Preset.Visibility = enabled && editor.Definition.AlgorithmKey is not null ? Visibility.Visible : Visibility.Collapsed;
        editor.Tune.Visibility = enabled && !custom && editor.Tune.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        editor.Custom.Visibility = enabled && custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadFilterPreset()
    {
        filterPresetEdits.Clear();
        LoadFilterEditorValues();
    }

    private void LoadFilterEditorValues()
    {
        if (ActivePresetChoice is null || filterEditors.Count == 0) return;
        loadingFilters = true;
        try
        {
            var json = JsonNode.Parse(ActivePresetChoice.Json)!.AsObject();
            ApplyFilterPresetEdits(json);
            foreach (var editor in filterEditors)
            {
                var d = editor.Definition;
                string preset = json[d.PresetKey]?.ToString() ?? (d.AlgorithmKey is null ? "off" : "default");
                SelectFilterChoice(editor.Choice, json[d.AlgorithmKey ?? d.PresetKey]?.ToString() ?? "off");
                ConfigureFilterDetails(editor, preset, d.TuneKey is null ? null : json[d.TuneKey]?.ToString());
                editor.Custom.Text = json[d.CustomKey]?.ToString() ?? "";
                UpdateFilterDetailVisibility(editor);
            }
            grayscaleChoice.IsChecked = json["VideoGrayScale"]?.GetValue<bool>() == true;
            FilterEditorStatus.Visibility = Visibility.Collapsed;
        }
        finally { loadingFilters = false; }
    }

    private void FilterChoiceChanged(FilterEditor editor)
    {
        if (loadingFilters || updatingVideoEditor) return;
        loadingFilters = true;
        try
        {
            ConfigureFilterDetails(editor, null, editor.Definition.AlgorithmKey is null ? ChoiceValue(editor.Tune) : null);
            // Custom text belongs to an algorithm; never apply another
            // algorithm's private parameter string after switching.
            if (editor.Definition.AlgorithmKey is not null) editor.Custom.Text = "";
        }
        finally { loadingFilters = false; }
        FilterDetailChanged(editor);
    }

    private void FilterDetailChanged(FilterEditor editor)
    {
        if (loadingFilters || updatingVideoEditor || ActivePresetChoice is null) return;
        UpdateFilterDetailVisibility(editor);
        var d = editor.Definition;
        filterPresetEdits[d.AlgorithmKey ?? d.PresetKey] = ChoiceValue(editor.Choice);
        if (d.AlgorithmKey is not null) filterPresetEdits[d.PresetKey] = ChoiceValue(editor.Preset);
        if (d.TuneKey is not null) filterPresetEdits[d.TuneKey] = ChoiceValue(editor.Tune) is { Length: > 0 } selectedTune ? selectedTune : null;
        filterPresetEdits[d.CustomKey] = editor.Custom.Text;
        FilterValuesChanged();
    }

    private void GrayscaleChanged(object sender, RoutedEventArgs args)
    {
        if (loadingFilters || updatingVideoEditor || ActivePresetChoice is null) return;
        filterPresetEdits["VideoGrayScale"] = grayscaleChoice.IsChecked == true;
        FilterValuesChanged();
    }

    private void FilterValuesChanged()
    {
        try { ValidateEditedFilters(); FilterEditorStatus.Visibility = Visibility.Collapsed; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException) { ShowFilterError(error); }
        RefreshOutputSummary();
    }

    private void ShowFilterError(Exception error)
    {
        FilterEditorStatus.Text = "Filter settings need attention.";
        FilterEditorStatus.Visibility = Visibility.Visible;
        ToolTipService.SetToolTip(FilterEditorStatus, CompactUi.Help(error.Message));
    }

    private void ApplyFilterPresetEdits(JsonObject json)
    {
        foreach (var edit in filterPresetEdits) json[edit.Key] = edit.Value?.DeepClone();
    }

    private void ValidateEditedFilters()
    {
        if (ActivePresetChoice is null) return;
        var json = JsonNode.Parse(ActivePresetChoice.Json)!.AsObject();
        ApplyFilterPresetEdits(json);
        foreach (var editor in filterEditors)
        {
            var d = editor.Definition;
            if (!filterPresetEdits.ContainsKey(d.AlgorithmKey ?? d.PresetKey)) continue;
            string algorithm = json[d.AlgorithmKey ?? d.PresetKey]?.ToString() ?? "off";
            if (algorithm == "off") continue;
            int id = (int)d.Id;
            if (d.Algorithms is { } algorithms)
            {
                var selected = algorithms.FirstOrDefault(item => item.Value == algorithm);
                id = selected is null ? 0 : (int)selected.Id;
            }
            if (id == 0) throw new InvalidOperationException("The selected filter algorithm is unavailable.");
            string? preset = json[d.PresetKey]?.ToString(), tune = d.TuneKey is null ? null : json[d.TuneKey]?.ToString();
            string? custom = json[d.CustomKey]?.ToString();
            string settings = HandBrakeFilterHelpers.GenerateFilterSettingJson(id, preset, tune, custom);
            if (string.IsNullOrWhiteSpace(settings) || settings.Trim() == "null")
                throw new InvalidOperationException(WpfText.Label(d.Label) + " The native engine rejected these settings.");
            using var parsed = JsonDocument.Parse(settings);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The native engine returned invalid filter settings.");
        }
    }

    private bool FilterSettingsModified(NativePresetChoice reference)
    {
        if (ActivePresetChoice is null) return false;
        var current = JsonNode.Parse(ActivePresetChoice.Json)!.AsObject();
        ApplyFilterPresetEdits(current);
        var baseline = JsonNode.Parse(reference.Json)!.AsObject();
        foreach (var d in FilterDefinitions)
        {
            string selector = FilterValue(current, d.AlgorithmKey ?? d.PresetKey, "off");
            if (selector != FilterValue(baseline, d.AlgorithmKey ?? d.PresetKey, "off")) return true;
            if (selector == "off") continue; // Inactive parameters do not change output.
            string preset = d.AlgorithmKey is null ? selector : FilterValue(current, d.PresetKey, "default");
            string originalPreset = d.AlgorithmKey is null ? selector : FilterValue(baseline, d.PresetKey, "default");
            if (preset != originalPreset) return true;
            if (preset == "custom")
            {
                if ((current[d.CustomKey]?.ToString() ?? "") != (baseline[d.CustomKey]?.ToString() ?? "")) return true;
            }
            else if (d.TuneKey is not null && FilterValue(current, d.TuneKey, "none") != FilterValue(baseline, d.TuneKey, "none")) return true;
        }
        return (current["VideoGrayScale"]?.GetValue<bool>() == true) != (baseline["VideoGrayScale"]?.GetValue<bool>() == true);
    }

    private static string FilterValue(JsonObject json, string key, string fallback)
        => json[key]?.ToString() is { Length: > 0 } value ? value.ToLowerInvariant() : fallback;

    private void RestoreLegacyFilterOverrides(IEnumerable<FilterOverride>? saved)
    {
        var choices = saved?.ToList() ?? new();
        foreach (var filter in BuildFilterOverrides(choices)) filter.Settings?.Dispose();
        // The former manual mode removed every effect filter. Materialize its
        // state into native preset fields before the next save, not into a new
        // destructive global mode. Old queued jobs still encode on their path.
        foreach (var d in FilterDefinitions) filterPresetEdits[d.AlgorithmKey ?? d.PresetKey] = "off";
        filterPresetEdits["VideoGrayScale"] = false;
        foreach (var filter in choices)
        {
            var d = FilterDefinitions.FirstOrDefault(d => (int)d.Id == filter.Id || d.Algorithms?.Any(a => (int)a.Id == filter.Id) == true)
                ?? throw new InvalidOperationException("A saved filter is unavailable in this editor.");
            if (d.AlgorithmKey is not null)
                filterPresetEdits[d.AlgorithmKey] = d.Algorithms!.First(a => (int)a.Id == filter.Id).Value;
            filterPresetEdits[d.PresetKey] = filter.Preset ?? "default";
            if (d.TuneKey is not null) filterPresetEdits[d.TuneKey] = filter.Tune;
            filterPresetEdits[d.CustomKey] = "";
        }
        LoadFilterEditorValues();
    }
    private static List<Filter> BuildFilterOverrides(IEnumerable<FilterOverride>? choices)
    {
        var result = new List<Filter>();
        foreach (var choice in choices ?? Enumerable.Empty<FilterOverride>())
        {
            // Saved data must not inject geometry or unsupported native filters.
            var supported = new[] { hb_filter_ids.HB_FILTER_DETELECINE, hb_filter_ids.HB_FILTER_COMB_DETECT,
                hb_filter_ids.HB_FILTER_DECOMB, hb_filter_ids.HB_FILTER_DEBLOCK, hb_filter_ids.HB_FILTER_NLMEANS,
                hb_filter_ids.HB_FILTER_CHROMA_SMOOTH, hb_filter_ids.HB_FILTER_UNSHARP };
            if (!supported.Any(id => (int)id == choice.Id) || result.Any(filter => filter.ID == choice.Id))
                throw new InvalidOperationException("Saved filter configuration is unsupported or duplicated. Re-stage this job.");
            string json = HandBrakeFilterHelpers.GenerateFilterSettingJson(choice.Id, choice.Preset, choice.Tune, null);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
                throw new InvalidOperationException("The native engine rejected a filter preset or tune. Re-stage this job.");
            var settings = JsonDocument.Parse(json);
            if (settings.RootElement.ValueKind != JsonValueKind.Object)
            {
                settings.Dispose();
                throw new InvalidOperationException("The native engine returned invalid filter settings.");
            }
            result.Add(new Filter { ID = choice.Id, Settings = settings });
        }
        return result;
    }
}

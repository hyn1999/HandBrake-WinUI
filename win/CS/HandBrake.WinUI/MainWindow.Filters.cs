using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Interfaces.Model.Filters;
using HandBrake.Interop.Interop.Json.Encode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed record FilterOverride(int Id, string? Preset, string? Tune);

public sealed partial class MainWindow
{
    private sealed record FilterEditor(int Id, CheckBox Enabled, ComboBox Preset, ComboBox Tune);
    private readonly List<FilterEditor> filterEditors = new();

    private void InitializeFilterEditor()
    {
        if (filterEditors.Count > 0) return;
        try
        {
            foreach (var definition in new[] {
                (hb_filter_ids.HB_FILTER_DETELECINE, "Detelecine"),
                (hb_filter_ids.HB_FILTER_COMB_DETECT, "Comb detection"),
                (hb_filter_ids.HB_FILTER_DECOMB, "Deinterlace · Decomb"),
                (hb_filter_ids.HB_FILTER_DEBLOCK, "Deblock"),
                (hb_filter_ids.HB_FILTER_NLMEANS, "Denoise · NLMeans"),
                (hb_filter_ids.HB_FILTER_CHROMA_SMOOTH, "Chroma smoothing"),
                (hb_filter_ids.HB_FILTER_UNSHARP, "Sharpen · Unsharp") })
            {
                int id = (int)definition.Item1;
                var enabled = new CheckBox { Content = definition.Item2 };
                var preset = new ComboBox { Header = "Preset", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
                var tune = new ComboBox { Header = "Tune", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
                foreach (var value in HandBrakeFilterHelpers.GetFilterPresets(id)
                    .Where(value => value.ShortName is not ("custom" or "off")))
                    preset.Items.Add(new ComboBoxItem { Content = value.Name, Tag = value.ShortName });
                foreach (var value in HandBrakeFilterHelpers.GetFilterTunes(id))
                    tune.Items.Add(new ComboBoxItem { Content = value.Name, Tag = value.ShortName });
                preset.SelectedIndex = preset.Items.Count > 0 ? 0 : -1;
                tune.SelectedIndex = tune.Items.Count > 0 ? 0 : -1;
                enabled.Checked += (_, _) => { preset.IsEnabled = preset.Items.Count > 0; tune.IsEnabled = tune.Items.Count > 0; };
                enabled.Unchecked += (_, _) => { preset.IsEnabled = false; tune.IsEnabled = false; };
                var fields = new List<FrameworkElement> { enabled };
                if (preset.Items.Count > 0) fields.Add(preset);
                if (tune.Items.Count > 0) fields.Add(tune);
                var card = CompactRow(fields.ToArray());
                FilterEditorPanel.Children.Add(card);
                filterEditors.Add(new FilterEditor(id, enabled, preset, tune));
            }
            FilterEditorStatus.Text = "Use preset filters to retain the complete native filter chain. Turn it off to choose manual effect filters below; geometry is retained.";
        }
        catch (Exception error)
        {
            filterEditors.Clear();
            FilterEditorPanel.Children.Clear();
            FilterEditorStatus.Text = "Native filter controls unavailable: " + error.Message;
        }
    }

    private List<FilterOverride> ReadFilterOverrides()
        => filterEditors.Where(editor => editor.Enabled.IsChecked == true)
            .Select(editor => new FilterOverride(editor.Id,
                (editor.Preset.SelectedItem as ComboBoxItem)?.Tag as string,
                (editor.Tune.SelectedItem as ComboBoxItem)?.Tag as string)).ToList();

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

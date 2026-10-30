using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? summaryTimer;

    private void ScheduleResolvedSummary()
    {
        if (EditorTabs is null || SummaryTab is null || EditorTabs.SelectedItem != SummaryTab) return;
        summaryTimer ??= CreateSummaryTimer();
        summaryTimer.Stop();
        summaryTimer.Start();
    }

    private DispatcherQueueTimer CreateSummaryTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => ResolveOutputSummary();
        Closed += (_, _) => timer.Stop();
        return timer;
    }

    private void ResolveOutputSummary()
    {
        if (scanInProgress || scanInstance is null || EditorTabs.SelectedItem != SummaryTab ||
            SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title } ||
            selectedSourcePaths.Count == 0)
        {
            ResolvedOutputDetailsText.Text = string.Empty;
            return;
        }
        try
        {
            var draft = CreateDraft(selectedSourcePaths[0], allowVideoConfiguration: true);
            if (!draft.HasVideoConfiguration)
            {
                ResolvedOutputDetailsText.Text = "Choose a compatible encoder and valid output path.";
                return;
            }
            var job = JsonNode.Parse(BuildNativeJob(draft, title, scanInstance))!;
            var filters = job["Filters"]!["FilterList"]!.AsArray();
            var scale = filters.FirstOrDefault(filter => filter?["ID"]?.GetValue<int>() == (int)hb_filter_ids.HB_FILTER_CROP_SCALE)?["Settings"];
            string geometry = scale is null ? "" : $"Scale: {scale["width"]} × {scale["height"]} pixels";
            if (job["PAR"] is { } par) geometry += $" · pixel aspect {par["Num"]}:{par["Den"]}";
            int audio = job["Audio"]?["AudioList"]?.AsArray().Count ?? 0;
            int subtitles = job["Subtitle"]?["SubtitleList"]?.AsArray().Count ?? 0;
            bool search = job["Subtitle"]?["Search"]?["Enable"]?.GetValue<bool>() == true;
            string effects = string.Join(", ", filters
                .Where(filter => filter?["ID"]?.GetValue<int>() is int id &&
                    id != (int)hb_filter_ids.HB_FILTER_CROP_SCALE && id != (int)hb_filter_ids.HB_FILTER_VFR)
                .Select(filter => ((hb_filter_ids)filter!["ID"]!.GetValue<int>()).ToString()
                    .Replace("HB_FILTER_", "").Replace("_", " ").ToLowerInvariant()));
            ResolvedOutputDetailsText.Text = $"{geometry}\n{audio} output audio track(s) · {subtitles} subtitle track(s)" +
                (search ? " · foreign-audio subtitle search" : "") +
                $"\nFilters: {(effects.Length == 0 ? "None" : effects)}";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or OverflowException or System.IO.IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            ResolvedOutputDetailsText.Text = "Review output settings: " + error.Message;
        }
    }
}

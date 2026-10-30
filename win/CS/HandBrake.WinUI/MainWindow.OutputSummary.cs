using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? summaryTimer;

    private void ScheduleResolvedSummary()
    {
        if (EditorTabs is null || SummaryTab is null || EditorTabs.SelectedItem != SummaryTab) return;
        ClearResolvedSummary();
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

    private void ClearResolvedSummary()
    {
        if (ConfiguredOutputText is null) return;
        ConfiguredOutputText.Text = FilterSummaryText.Text = SizeSummaryText.Text = "—";
        ResolvedOutputDetailsText.Text = string.Empty;
        SummaryStatusText.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(SummaryStatusText, null);
    }

    private void ResolveOutputSummary()
    {
        ClearResolvedSummary();
        if (scanInProgress || scanInstance is null || EditorTabs.SelectedItem != SummaryTab ||
            SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title } ||
            selectedSourcePaths.Count == 0) return;
        try
        {
            var draft = CreateDraft(selectedSourcePaths[0], allowVideoConfiguration: true);
            if (!draft.HasVideoConfiguration) return;
            var job = JsonNode.Parse(BuildNativeJob(draft, title, scanInstance))!;
            var filters = job["Filters"]!["FilterList"]!.AsArray();
            var scale = filters.FirstOrDefault(filter => filter?["ID"]?.GetValue<int>() == (int)hb_filter_ids.HB_FILTER_CROP_SCALE)?["Settings"];
            string size = SummaryDimensions(scale, job["PAR"], filters);
            var lines = new List<string>();
            foreach (var track in job["Audio"]?["AudioList"]?.AsArray() ?? new JsonArray())
            {
                string codec = track?["Encoder"]?.ToString() ?? "";
                var encoder = int.TryParse(codec, out int codecId)
                    ? HandBrakeEncoderHelpers.GetAudioEncoder(codecId)
                    : HandBrakeEncoderHelpers.GetAudioEncoder(codec);
                string mixdown = track?["Mixdown"]?.ToString() ?? "";
                if (int.TryParse(mixdown, out int mixdownId))
                    mixdown = HandBrakeEncoderHelpers.GetMixdown(mixdownId)?.ShortName ?? "";
                string quality = track?["Bitrate"] is { } bitrate && SummaryNumber(bitrate) > 0
                    ? $"{bitrate} kbps" : track?["Quality"] is { } q && SummaryNumber(q) >= 0 ? $"Quality {q}" : "";
                lines.Add(string.Join(", ", new[] { encoder?.DisplayName ?? codec, mixdown, quality }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
            }
            if (lines.Count == 0) lines.Add("No audio tracks");
            var subtitles = job["Subtitle"]?["SubtitleList"]?.AsArray() ?? new JsonArray();
            foreach (var track in subtitles)
            {
                int index = int.TryParse(track?["Track"]?.ToString(), out int sourceIndex) ? sourceIndex : -1;
                string? name = track?["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    name = track?["Import"] is { } import ? import["Language"]?.ToString() + " (SRT)"
                        : index >= 0 ? title.SubtitleList?.ElementAtOrDefault(index)?.Language : null;
                lines.Add((string.IsNullOrWhiteSpace(name) ? "Subtitle" : name) +
                    (track?["Burn"]?.GetValue<bool>() == true ? ", Burned" : "") +
                    (track?["Forced"]?.GetValue<bool>() == true ? ", Forced" : "") +
                    (track?["Default"]?.GetValue<bool>() == true ? ", Default" : ""));
            }
            var search = job["Subtitle"]?["Search"];
            if (search?["Enable"]?.GetValue<bool>() == true)
                lines.Add("Foreign Audio Scan" + (search["Burn"]?.GetValue<bool>() == true ? ", Burned" : "") +
                    (search["Forced"]?.GetValue<bool>() == true ? ", Forced" : ""));
            if (subtitles.Count == 0 && search?["Enable"]?.GetValue<bool>() != true) lines.Add("No subtitle tracks");
            if (job["Destination"]?["ChapterMarkers"]?.GetValue<bool>() == true) lines.Add("Chapter Markers");

            string rate = draft.VideoRateControl == "ConstantQuality"
                ? $"Quality {draft.VideoQuality:0.##}" : $"{draft.VideoBitrateKbps} kbps";
            string fps = draft.VideoFramerate is null or "auto" or "0"
                ? title.FrameRate is { Den: > 0 } sourceRate ? $"{(double)sourceRate.Num / sourceRate.Den:0.###}" : "Same as source"
                : draft.VideoFramerate;
            string mode = ChoiceValue(frameModePicker).ToUpperInvariant();
            string video = $"{HandBrakeEncoderHelpers.GetVideoEncoder(draft.VideoEncoder)?.DisplayName ?? draft.VideoEncoder}, {fps} FPS {mode}, {rate}";
            string effects = string.Join(", ", filters
                .Where(filter => filter?["ID"]?.GetValue<int>() is int id &&
                    id != (int)hb_filter_ids.HB_FILTER_CROP_SCALE && id != (int)hb_filter_ids.HB_FILTER_VFR)
                .Select(filter => SummaryFilterName(filter!["ID"]!.GetValue<int>())));
            // Publish together only after the complete output job has resolved.
            ConfiguredOutputText.Text = video;
            ResolvedOutputDetailsText.Text = string.Join("\n", lines);
            FilterSummaryText.Text = effects.Length == 0 ? "None" : effects;
            SizeSummaryText.Text = size;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or OverflowException or System.IO.IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            SummaryStatusText.Text = "Output summary unavailable. Review output settings.";
            SummaryStatusText.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(SummaryStatusText, Design.CompactUi.Help(error.Message));
        }
    }

    private static double SummaryNumber(JsonNode? value)
        => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN;

    private static string SummaryDimensions(JsonNode? scale, JsonNode? par, JsonArray filters)
    {
        double width = SummaryNumber(scale?["width"]), height = SummaryNumber(scale?["height"]);
        foreach (var filter in filters)
        {
            if (filter?["ID"] is not { } id || ((hb_filter_ids)id.GetValue<int>()).ToString() != "HB_FILTER_PAD") continue;
            var pad = filter["Settings"];
            double paddedWidth = SummaryNumber(pad?["width"]), paddedHeight = SummaryNumber(pad?["height"]);
            // Avoid reporting pre-border dimensions as the final output size.
            if (!(paddedWidth > 0 && paddedHeight > 0)) return "—";
            width = paddedWidth; height = paddedHeight;
        }
        if (!(width > 0 && height > 0)) return "—";
        double numerator = SummaryNumber(par?["Num"]), denominator = SummaryNumber(par?["Den"]);
        if (!(numerator > 0 && denominator > 0)) return $"{width:0} × {height:0} storage";
        return $"{width:0} × {height:0} storage, {width * numerator / denominator:0} × {height:0} display";
    }

    private static string SummaryFilterName(int id)
    {
        string name = ((hb_filter_ids)id).ToString().Replace("HB_FILTER_", "");
        return name switch
        {
            "COMB_DETECT" => "Interlace Detection", "CHROMA_SMOOTH" => "Chroma Smooth",
            "COLORSPACE" => "Colourspace", "NLMEANS" => "NLMeans", "HQDN3D" => "HQDN3D",
            "PAD" => "Borders", _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace("_", " ").ToLowerInvariant())
        };
    }

    private void InitializeSummaryTooltips()
    {
        // Verbatim help from HandBrakeWPF/Properties/ResourcesTooltips.resx.
        ToolTipService.SetToolTip(outputFormatPicker, Design.CompactUi.Help("Container format. Video, audio, and other tracks are combined into a single file of this type. Affects compatibility."));
        ToolTipService.SetToolTip(webOptimize, Design.CompactUi.Help("Optimize MP4 for progressive download. After encoding, data is reorganized and rewritten to allow immediate playback over a network, without needing to download the entire file."));
        ToolTipService.SetToolTip(alignAv, Design.CompactUi.Help("Aligns the initial timestamps of all audio and video streams by inserting blank frames or dropping frames. May improve audio/video sync for broken players that do not honor MP4 edit lists."));
        ToolTipService.SetToolTip(ipodSupport, Design.CompactUi.Help("Add a special MP4 marker to allow playback on vintage iPod 5th Generation devices circa 2006. Other settings may affect compatibility."));
    }
}

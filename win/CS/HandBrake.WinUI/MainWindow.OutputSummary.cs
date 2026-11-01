using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.WinUI.Resources;
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
        if (EditorTabs is null || SummaryTab is null || (EditorTabs.SelectedItem != SummaryTab && EditorTabs.SelectedItem != DimensionsTab)) return;
        ClearResolvedSummary();
        ClearDimensionsResult();
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
        ClearDimensionsResult();
        if (scanInProgress || scanInstance is null || (EditorTabs.SelectedItem != SummaryTab && EditorTabs.SelectedItem != DimensionsTab) ||
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
            if (lines.Count == 0) lines.Add(WpfText.Label("SummaryView_NoAudioTracks"));
            var subtitles = job["Subtitle"]?["SubtitleList"]?.AsArray() ?? new JsonArray();
            foreach (var track in subtitles)
            {
                int index = int.TryParse(track?["Track"]?.ToString(), out int sourceIndex) ? sourceIndex : -1;
                string? name = track?["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    name = track?["Import"] is { } import ? import["Language"]?.ToString() + " (SRT)"
                        : index >= 0 ? title.SubtitleList?.ElementAtOrDefault(index)?.Language : null;
                lines.Add((string.IsNullOrWhiteSpace(name) ? "Subtitle" : name) +
                    (track?["Burn"]?.GetValue<bool>() == true ? ", " + WpfText.Label("SummaryView_Burned") : "") +
                    (track?["Forced"]?.GetValue<bool>() == true ? ", " + WpfText.Label("SummaryView_Forced") : "") +
                    (track?["Default"]?.GetValue<bool>() == true ? ", " + WpfText.Label("SummaryView_Default") : ""));
            }
            var search = job["Subtitle"]?["Search"];
            if (search?["Enable"]?.GetValue<bool>() == true)
                lines.Add(WpfText.Label("Subtitle_ForeignAudioScan") + (search["Burn"]?.GetValue<bool>() == true ? ", " + WpfText.Label("SummaryView_Burned") : "") +
                    (search["Forced"]?.GetValue<bool>() == true ? ", " + WpfText.Label("SummaryView_Forced") : ""));
            if (subtitles.Count == 0 && search?["Enable"]?.GetValue<bool>() != true) lines.Add(WpfText.Label("SummaryView_NoSubtitleTracks"));
            if (job["Destination"]?["ChapterMarkers"]?.GetValue<bool>() == true) lines.Add(WpfText.Label("SummaryView_Chapters"));

            string rate = draft.VideoRateControl == "ConstantQuality"
                ? $"Quality {draft.VideoQuality:0.##}" : $"{draft.VideoBitrateKbps} kbps";
            string fps = draft.VideoFramerate is null or "auto" or "0"
                ? title.FrameRate is { Den: > 0 } sourceRate ? $"{(double)sourceRate.Num / sourceRate.Den:0.###}" : WpfText.Label("SummaryView_SameAsSource")
                : draft.VideoFramerate;
            string mode = frameMode.ToUpperInvariant();
            string video = $"{HandBrakeEncoderHelpers.GetVideoEncoder(draft.VideoEncoder)?.DisplayName ?? draft.VideoEncoder}, {fps} FPS {mode}, {rate}";
            string effects = string.Join(", ", filters
                .Where(filter => filter?["ID"]?.GetValue<int>() is int id &&
                    id != (int)hb_filter_ids.HB_FILTER_CROP_SCALE && id != (int)hb_filter_ids.HB_FILTER_VFR)
                .Select(filter => SummaryFilterName(filter!["ID"]!.GetValue<int>())));
            // Publish together only after the complete output job has resolved.
            ConfiguredOutputText.Text = video;
            ResolvedOutputDetailsText.Text = string.Join("\n", lines);
            FilterSummaryText.Text = effects.Length == 0 ? WpfText.Label("SummaryView_NoFilters") : effects;
            SizeSummaryText.Text = size;
            FinalDimensionsText.Text = SummaryDimensions(scale, job["PAR"], filters, includeAspect: true);
            PixelAspectSummaryText.Text = $"{WpfText.Label("PictureSettingsView_PAR")} {job["PAR"]?["Num"]}:{job["PAR"]?["Den"]}";
            if (CustomGeometryToggle.IsChecked != true && scale is not null)
            {
                bool wasUpdating = updatingVideoEditor;
                updatingVideoEditor = true;
                try
                {
                    OutputWidthBox.Value = SummaryNumber(scale["width"]);
                    OutputHeightBox.Value = SummaryNumber(scale["height"]);
                    CropTopBox.Value = SummaryNumber(scale["crop-top"]);
                    CropBottomBox.Value = SummaryNumber(scale["crop-bottom"]);
                    CropLeftBox.Value = SummaryNumber(scale["crop-left"]);
                    CropRightBox.Value = SummaryNumber(scale["crop-right"]);
                }
                finally { updatingVideoEditor = wasUpdating; }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException or OverflowException or System.IO.IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            DimensionsStatusText.Text = "Dimensions unavailable. Review output settings.";
            DimensionsStatusText.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DimensionsStatusText, Design.CompactUi.Help(error.Message));
            SummaryStatusText.Text = "Output summary unavailable. Review output settings.";
            SummaryStatusText.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(SummaryStatusText, Design.CompactUi.Help(error.Message));
        }
    }

    private static double SummaryNumber(JsonNode? value)
        => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN;

    private static string SummaryDimensions(JsonNode? scale, JsonNode? par, JsonArray filters, bool includeAspect = false)
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
        if (!(numerator > 0 && denominator > 0)) return $"{width:0} × {height:0} {WpfText.Label("SummaryView_storage")}";
        string dimensions = $"{width:0} × {height:0} {WpfText.Label("SummaryView_storage")}, {width * numerator / denominator:0} × {height:0} {WpfText.Label("SummaryView_display")}";
        return includeAspect ? dimensions + " · " + string.Format(CultureInfo.CurrentCulture,
            WpfText.Label("PictureSettingsViewModel_AspectRatioLabel"), DimensionAspect((long)width * (long)numerator, (long)height * (long)denominator)) : dimensions;
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
        outputFormatPicker.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, WpfText.Format);
        webOptimize.Content = WpfText.Label("MainView_WebOptimized");
        alignAv.Content = WpfText.Label("MainView_AlignAVStart");
        ipodSupport.Content = WpfText.Label("MainView_iPod5G");
        metadataPassthru.Content = WpfText.Label("SummaryView_MetadataPassthru");
        ToolTipService.SetToolTip(outputFormatPicker, Design.CompactUi.Help(WpfText.Tooltip("MainView_Mux")));
        ToolTipService.SetToolTip(webOptimize, Design.CompactUi.Help(WpfText.Tooltip("MainView_Optimise")));
        ToolTipService.SetToolTip(alignAv, Design.CompactUi.Help(WpfText.Tooltip("MainView_AlignAVStart")));
        ToolTipService.SetToolTip(ipodSupport, Design.CompactUi.Help(WpfText.Tooltip("MainView_IpodAtom")));
    }
}

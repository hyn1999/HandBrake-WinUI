using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Presets;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.Interop.Utilities;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private string BuildNativeJob(QueueDraft draft, SourceTitle title, HandBrakeInstance instance, bool preview = false)
    {
        var choice = nativePresetChoices.FirstOrDefault(item => item.Key == draft.PresetKey)
            ?? throw new InvalidOperationException("The selected preset is unavailable. Re-stage this job.");
        var preset = JsonNode.Parse(draft.NativePresetJson ?? choice.Json)?.AsObject()
            ?? throw new InvalidOperationException("The saved preset is invalid.");
        var originalPreset = preset.Deserialize<HBPreset>()
            ?? throw new InvalidOperationException("The saved preset is invalid.");
        var encoder = HandBrakeEncoderHelpers.GetVideoEncoder(draft.VideoEncoder);
        if (encoder is null || !IsContainerCompatible(FormatPresetContainer(originalPreset.FileFormat), encoder))
            throw new InvalidOperationException("The video encoder is unavailable or incompatible with this container.");
        bool quality = draft.VideoRateControl == "ConstantQuality";
        if ((quality && (!encoder.SupportsQuality || draft.VideoQuality is not double q || !double.IsFinite(q)) ||
            !quality && (draft.VideoRateControl != "AverageBitrate" || !encoder.SupportsBitrate || draft.VideoBitrateKbps is not > 0)))
            throw new InvalidOperationException("The selected video rate control is invalid.");

        if (quality)
        {
            var limits = HandBrakeEncoderHelpers.GetVideoQualityLimits(encoder);
            if (limits is not null && (draft.VideoQuality < limits.Low || draft.VideoQuality > limits.High))
                throw new InvalidOperationException("The quality value is outside the native encoder range.");
        }

        bool changedEncoder = !string.Equals(originalPreset.VideoEncoder, draft.VideoEncoder, StringComparison.OrdinalIgnoreCase);
        preset["VideoEncoder"] = draft.VideoEncoder;
        preset["VideoQualityType"] = quality ? 2 : 1;
        if (quality) preset["VideoQualitySlider"] = draft.VideoQuality;
        else preset["VideoAvgBitrate"] = draft.VideoBitrateKbps;
        if (!string.IsNullOrWhiteSpace(draft.VideoFramerate)) preset["VideoFramerate"] = draft.VideoFramerate;
        if (changedEncoder)
        {
            // A profile, tune or private argument from x264 must not leak into
            // another encoder. Its native defaults apply until explicitly edited.
            foreach (string key in new[] { "VideoPreset", "VideoProfile", "VideoLevel", "VideoTune", "VideoOptionExtra", "x264Option", "x264UseAdvancedOptions" })
                preset.Remove(key);
        }
        else if (!string.IsNullOrWhiteSpace(draft.VideoEncoderSpeed))
            preset["VideoPreset"] = draft.VideoEncoderSpeed;

        // Geometry is input to native preset resolution, not a replacement for
        // its generated filters. Rotation changes crop coordinates and PAR;
        // padding must be computed from the newly scaled image.
        if (draft.Geometry is { } manualGeometry)
            ApplyManualGeometry(preset, manualGeometry, title);
        var job = JsonNode.Parse(instance.CreatePresetJob(title.Index, preset.ToJsonString()))?.AsObject()
            ?? throw new InvalidOperationException("HandBrake returned no preset job.");
        if (draft.Geometry is { } requestedGeometry)
            VerifyResolvedGeometry(job, requestedGeometry);
        var destination = job["Destination"]!.AsObject();
        destination["File"] = preview ? string.Empty : draft.DestinationPath;
        if (draft.ChapterMarkers.HasValue) destination["ChapterMarkers"] = draft.ChapterMarkers.Value;
        if (draft.ChapterNames is not null)
        {
            if (draft.ChapterNames.Count != (title.ChapterList?.Count ?? 0))
                throw new InvalidOperationException("The source chapter list changed. Re-scan and stage this job again.");
            destination["ChapterList"] = JsonSerializer.SerializeToNode(draft.ChapterNames.Select(name => new { Name = name }));
        }
        var source = job["Source"]!.AsObject();
        source["Path"] = title.Path;
        source["Title"] = title.Index;
        if (!preview && draft.Selection is { } selection)
        {
            ValidateSelection(selection, title);
            source["Angle"] = selection.Angle;
            if (selection.Type != "all")
            {
                long start = selection.Type == "time" ? checked((long)Math.Round(selection.Start * 90000)) : (long)selection.Start;
                long end = selection.Type == "time" ? checked((long)Math.Round(selection.End * 90000)) : (long)selection.End;
                if (selection.Type == "time" && end <= start)
                    throw new InvalidOperationException("The selected time range is shorter than one native clock tick.");
                source["Range"] = new JsonObject { ["Type"] = selection.Type, ["Start"] = start, ["End"] = end };
            }
        }
        // Still-frame previews navigate scan frames independently of encode range.
        if (preview)
        {
            source["Range"] = new JsonObject { ["Type"] = "preview", ["Start"] = 0, ["End"] = 30 * 90000L, ["SeekPoints"] = Math.Max(1, instance.PreviewCount) };
            destination["ChapterMarkers"] = false;
        }

        if (preview) job["Audio"]!["AudioList"] = new JsonArray();
        else if (!draft.UsePresetAudio)
            job["Audio"]!["AudioList"] = JsonSerializer.SerializeToNode(BuildManualAudio(draft, title, originalPreset), JsonSettings.Options);
        if (!draft.UsePresetSubtitles)
        {
            if ((draft.Subtitles?.Count(track => track.Burn) ?? 0) + (draft.SubtitleImports?.Count(track => track.Burn) ?? 0) +
                (draft.ForeignAudioSearch && draft.ForeignAudioBurn ? 1 : 0) > 1)
                throw new InvalidOperationException("Only one subtitle track can be burned into the video.");
            job["Subtitle"] = new JsonObject
            {
                ["Search"] = new JsonObject { ["Enable"] = !preview && draft.ForeignAudioSearch, ["Forced"] = draft.ForeignAudioForced, ["Default"] = false, ["Burn"] = !preview && draft.ForeignAudioSearch && draft.ForeignAudioBurn },
                ["SubtitleList"] = JsonSerializer.SerializeToNode(BuildSubtitleTracks(draft.Subtitles, title, originalPreset.FileFormat)
                    .Concat(BuildImportedSubtitles(draft.SubtitleImports, originalPreset.FileFormat)), JsonSettings.Options)
            };
        }
        if (preview && job["Subtitle"]?["Search"] is JsonObject search) search["Enable"] = false;
        if (preset["MetadataPassthru"]?.GetValue<bool>() == false)
        {
            job["Metadata"] = new JsonObject();
            job["CoverArts"] = new JsonArray();
        }
        if (draft.MetadataOverrides is not null)
        {
            job["Metadata"] ??= new JsonObject();
            foreach (var field in draft.MetadataOverrides) job["Metadata"]![field.Key] = field.Value;
        }

        var filters = job["Filters"]!["FilterList"]!.AsArray();
        if (!draft.UsePresetFilters)
        {
            // Keep geometry and engine infrastructure; the manual editor owns
            // effect filters only. Native preset mode preserves every filter.
            var structural = new HashSet<int> { (int)hb_filter_ids.HB_FILTER_ADAPTER, (int)hb_filter_ids.HB_FILTER_VFR,
                (int)hb_filter_ids.HB_FILTER_ROTATE, (int)hb_filter_ids.HB_FILTER_RENDER_SUB,
                (int)hb_filter_ids.HB_FILTER_CROP_SCALE, (int)hb_filter_ids.HB_FILTER_PAD,
                (int)hb_filter_ids.HB_FILTER_FORMAT, (int)hb_filter_ids.HB_FILTER_RPU };
            foreach (var filter in filters.ToList())
                if (filter is not null && !structural.Contains(filter["ID"]!.GetValue<int>())) filters.Remove(filter);
            foreach (var filter in BuildFilterOverrides(draft.FilterOverrides))
            {
                try { filters.Add(JsonSerializer.SerializeToNode(filter, JsonSettings.Options)); }
                finally { filter.Settings?.Dispose(); }
            }
        }
        job["Filters"]!["FilterList"] = new JsonArray(filters.OrderBy(filter => filter!["ID"]!.GetValue<int>()).Select(filter => filter!.DeepClone()).ToArray());
        // Empty strings are not valid presets for every encoder; absence means default.
        foreach (string key in new[] { "Preset", "Profile", "Level", "Tune", "Options" })
            if (job["Video"]?[key] is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                job["Video"]!.AsObject().Remove(key);
        return job.ToJsonString(JsonSettings.Options);
    }
}

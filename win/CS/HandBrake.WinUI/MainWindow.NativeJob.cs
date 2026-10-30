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

        var job = JsonNode.Parse(instance.CreatePresetJob(title.Index, preset.ToJsonString()))?.AsObject()
            ?? throw new InvalidOperationException("HandBrake returned no preset job.");
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
        // The engine uses one-based angles. Keep the native default angle.
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
        if (draft.Geometry is { } geometry)
        {
            ValidateGeometry(geometry, title);
            string settings = $"width={geometry.Width}:height={geometry.Height}:crop-top={geometry.Top}:crop-bottom={geometry.Bottom}:crop-left={geometry.Left}:crop-right={geometry.Right}";
            string json = HandBrakeFilterHelpers.GenerateFilterSettingJson((int)hb_filter_ids.HB_FILTER_CROP_SCALE, null, null, settings);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") throw new InvalidOperationException("HandBrake rejected the manual geometry.");
            foreach (var filter in filters.ToList())
                if (filter?["ID"]?.GetValue<int>() is int id &&
                    (id == (int)hb_filter_ids.HB_FILTER_CROP_SCALE || id == (int)hb_filter_ids.HB_FILTER_ROTATE || id == (int)hb_filter_ids.HB_FILTER_PAD))
                    filters.Remove(filter);
            filters.Add(new JsonObject { ["ID"] = (int)hb_filter_ids.HB_FILTER_CROP_SCALE, ["Settings"] = JsonNode.Parse(json) });
            job["PAR"] = new JsonObject { ["Num"] = Math.Max(1, title.Geometry?.PAR?.Num ?? 1), ["Den"] = Math.Max(1, title.Geometry?.PAR?.Den ?? 1) };
        }
        job["Filters"]!["FilterList"] = new JsonArray(filters.OrderBy(filter => filter!["ID"]!.GetValue<int>()).Select(filter => filter!.DeepClone()).ToArray());
        // Empty strings are not valid presets for every encoder; absence means default.
        foreach (string key in new[] { "Preset", "Profile", "Level", "Tune", "Options" })
            if (job["Video"]?[key] is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                job["Video"]!.AsObject().Remove(key);
        return job.ToJsonString(JsonSettings.Options);
    }
}

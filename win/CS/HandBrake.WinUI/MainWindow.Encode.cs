using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Interfaces.EventArgs;
using HandBrake.Interop.Interop.Json.Encode;
using HandBrake.Interop.Interop.Json.Presets;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.Interop.Interop.Json.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EncodeRange = HandBrake.Interop.Interop.Json.Encode.Range;
using EncodeSource = HandBrake.Interop.Interop.Json.Encode.Source;
using EncodeAudioTrack = HandBrake.Interop.Interop.Json.Encode.AudioTrack;
using EncodeSubtitleTrack = HandBrake.Interop.Interop.Json.Encode.SubtitleTrack;

namespace HandBrake.WinUI;

/// <summary>
/// Native sequential encode runner. Each queue item is rescanned with the matching
/// libhb instance before its job is built, so persisted drafts never rely on stale
/// title metadata from a previous application session.
/// </summary>
public sealed partial class MainWindow
{
    private HandBrakeInstance? encodeInstance;
    private List<QueueDraft> activeEncodeQueue = new();
    private int activeEncodeQueueIndex;
    private bool encodeRunning;
    private bool encodeScanning;
    private bool encodeStopRequested;

    private void InitializeEncodeRunner()
    {
        Closed += (_, _) =>
        {
            var instance = encodeInstance;
            if (instance is null) return;
            try
            {
                if (encodeScanning) instance.StopScan();
                else if (encodeRunning) instance.StopEncode();
                else instance.Dispose();
            }
            catch
            {
                // The process is closing; do not let native teardown block shutdown.
            }
        };
    }

    private async void StartEncode_Click(object sender, RoutedEventArgs e)
    {
        if (encodeRunning) return;

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "hb.dll")))
        {
            QueueStatusText.Text = "The matching native HandBrake engine (hb.dll) is missing. Build the full native package before encoding.";
            return;
        }

        if (nativePresetChoices.Count == 0)
        {
            QueueStatusText.Text = "Native presets are not loaded. Verify hb.dll and restart the application.";
            return;
        }

        List<QueueDraft> jobs = draftQueue.Where(item => item.HasVideoConfiguration).ToList();
        if (jobs.Count == 0)
        {
            QueueStatusText.Text = "There are no configured jobs. Scan a title, choose a native preset, then select Add to queue.";
            return;
        }

        foreach (QueueDraft draft in jobs)
        {
            if (!File.Exists(draft.SourcePath) && !Directory.Exists(draft.SourcePath))
            {
                QueueStatusText.Text = $"Source no longer exists: {draft.SourcePath}";
                return;
            }

            if (string.IsNullOrWhiteSpace(draft.DestinationPath) ||
                !Directory.Exists(Path.GetDirectoryName(draft.DestinationPath)))
            {
                QueueStatusText.Text = $"Output folder is unavailable for {draft.DisplayName}. Choose a valid destination folder.";
                return;
            }

            if (!nativePresetChoices.Any(choice => choice.Key == draft.PresetKey))
            {
                QueueStatusText.Text = $"Preset '{draft.PresetName}' is not available in the current native engine. Re-stage this item with a current preset.";
                return;
            }

            string sourceFullPath = Path.GetFullPath(draft.SourcePath);
            string outputFullPath = Path.GetFullPath(draft.DestinationPath);
            if (string.Equals(sourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                QueueStatusText.Text = $"The output path cannot overwrite its source: {draft.SourcePath}";
                return;
            }
        }

        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = jobs.Select(job => Path.GetFullPath(job.SourcePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (QueueDraft draft in jobs)
        {
            string output = Path.GetFullPath(draft.DestinationPath!);
            if (!destinations.Add(output) || sourcePaths.Contains(output))
            {
                QueueStatusText.Text = "Queue destinations must be unique and must not overwrite any queued source.";
                return;
            }
        }

        var existingOutputs = jobs.Where(job => File.Exists(job.DestinationPath)).ToList();
        if (existingOutputs.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = WindowRoot.XamlRoot,
                Title = "Replace existing output files?",
                Content = $"{existingOutputs.Count} output file(s) already exist. HandBrake will replace them if you continue.",
                PrimaryButtonText = "Replace and encode",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        activeEncodeQueue = jobs;
        activeEncodeQueueIndex = 0;
        encodeStopRequested = false;
        encodeRunning = true;
        encodeScanning = false;
        UpdateEncodeControls();
        QueueEncodeProgress.Visibility = Visibility.Visible;
        QueueEncodeProgress.IsIndeterminate = true;
        QueueEncodeProgress.Value = 0;
        QueueStatusText.Text = $"Preparing native encode queue · {jobs.Count} job(s).";
        AppNavigation.SelectedItem = AppNavigation.MenuItems[1];
        StartNextQueueItem();
    }

    private void StartNextQueueItem()
    {
        if (!encodeRunning || encodeStopRequested) return;

        if (activeEncodeQueueIndex >= activeEncodeQueue.Count)
        {
            FinishEncodeRun($"Completed {activeEncodeQueue.Count} encode job(s).");
            return;
        }

        QueueDraft draft = activeEncodeQueue[activeEncodeQueueIndex];
        if (!File.Exists(draft.SourcePath) && !Directory.Exists(draft.SourcePath))
        {
            FinishEncodeRun($"Queue stopped: source is missing for {draft.DisplayName}.");
            return;
        }

        try
        {
            encodeInstance = new HandBrakeInstance();
            encodeInstance.Initialize(SettingsPanel.Preferences.ScanVerbosity, noHardware: true);
            encodeInstance.ScanProgress += EncodeScanProgress;
            encodeInstance.ScanCompleted += EncodeScanCompleted;
            encodeScanning = true;
            QueueEncodeProgress.IsIndeterminate = true;
            QueueEncodeProgress.Value = 0;
            QueueStatusText.Text =
                $"Scanning {activeEncodeQueueIndex + 1}/{activeEncodeQueue.Count} · {draft.DisplayName}";
            encodeInstance.StartScan(
                new List<string> { draft.SourcePath },
                previewCount: SettingsPanel.Preferences.ScanPreviewCount,
                minDuration: TimeSpan.FromSeconds(SettingsPanel.Preferences.MinTitleDurationSeconds),
                maxDuration: TimeSpan.Zero,
                titleIndex: 0,
                excludedExtensions: new List<string>(),
                hwDecode: 0,
                keepDuplicateTitles: SettingsPanel.Preferences.KeepDuplicateTitles);
        }
        catch (Exception ex)
        {
            FinishEncodeRun($"Unable to scan {draft.DisplayName}: {ex.Message}");
        }
    }

    private void EncodeScanProgress(object? sender, ScanProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!encodeRunning || !encodeScanning) return;
            QueueEncodeProgress.IsIndeterminate = false;
            QueueEncodeProgress.Value = Math.Clamp(e.Progress * 100, 0, 100);
            QueueStatusText.Text =
                $"Scanning {activeEncodeQueueIndex + 1}/{activeEncodeQueue.Count} · " +
                $"title {e.CurrentTitle}/{e.Titles} · {QueueEncodeProgress.Value:0}%";
        });
    }

    private void EncodeScanCompleted(object? sender, EventArgs e)
    {
        var instance = sender as HandBrakeInstance;
        List<SourceTitle> titles = instance?.Titles?.TitleList?.ToList() ?? new List<SourceTitle>();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!encodeRunning) return;
            encodeScanning = false;

            if (encodeStopRequested)
            {
                FinishEncodeRun("Encode queue stopped by user.");
                return;
            }

            QueueDraft draft = activeEncodeQueue[activeEncodeQueueIndex];
            SourceTitle? title = titles.FirstOrDefault(item => item.Index == draft.TitleIndex);
            if (title is null)
            {
                FinishEncodeRun(
                    $"Queue stopped: title {draft.TitleIndex} was not found in {draft.DisplayName}. Re-scan and stage the job again.");
                return;
            }

            try
            {
                if (encodeInstance is null)
                    throw new InvalidOperationException("The native HandBrake instance is unavailable.");

                encodeInstance.ScanProgress -= EncodeScanProgress;
                encodeInstance.ScanCompleted -= EncodeScanCompleted;
                encodeInstance.EncodeProgress += EncodeProgressChanged;
                encodeInstance.EncodeCompleted += EncodeJobCompleted;

                JsonEncodeObject job = BuildEncodeJob(draft, title);
                QueueEncodeProgress.IsIndeterminate = true;
                QueueStatusText.Text =
                    $"Starting {activeEncodeQueueIndex + 1}/{activeEncodeQueue.Count} · {draft.DisplayName}";
                encodeInstance.StartEncode(job);
            }
            catch (Exception ex)
            {
                FinishEncodeRun($"Unable to start {draft.DisplayName}: {ex.Message}");
            }
        });
    }

    private void EncodeProgressChanged(object? sender, EncodeProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!encodeRunning || encodeStopRequested) return;
            QueueEncodeProgress.IsIndeterminate = false;
            QueueEncodeProgress.Value = Math.Clamp(e.FractionComplete * 100, 0, 100);
            QueueDraft draft = activeEncodeQueue[activeEncodeQueueIndex];
            string fps = e.AverageFrameRate > 0 ? $" · {e.AverageFrameRate:0.0} fps" : string.Empty;
            string eta = e.EstimatedTimeLeft > TimeSpan.Zero
                ? $" · ETA {FormatEncodeTime(e.EstimatedTimeLeft)}"
                : string.Empty;
            QueueStatusText.Text =
                $"Encoding {activeEncodeQueueIndex + 1}/{activeEncodeQueue.Count} · " +
                $"{draft.DisplayName} · {QueueEncodeProgress.Value:0}%{fps}{eta}";
        });
    }

    private void EncodeJobCompleted(object? sender, EncodeCompletedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!encodeRunning) return;
            if (encodeStopRequested)
            {
                FinishEncodeRun("Encode queue stopped by user.");
                return;
            }

            QueueDraft draft = activeEncodeQueue[activeEncodeQueueIndex];
            if (e.Error != 0)
            {
                FinishEncodeRun(
                    $"Encoding failed for {draft.DisplayName} (native error {e.Error}). Check the HandBrake log and output path.");
                return;
            }

            if (string.IsNullOrWhiteSpace(draft.DestinationPath) ||
                !File.Exists(draft.DestinationPath) ||
                new FileInfo(draft.DestinationPath).Length == 0)
            {
                FinishEncodeRun(
                    $"HandBrake reported completion, but the output file is missing or empty: {draft.DestinationPath}");
                return;
            }

            activeEncodeQueueIndex++;
            ReleaseEncodeInstance();
            StartNextQueueItem();
        });
    }

    private void CancelEncode_Click(object sender, RoutedEventArgs e)
    {
        if (!encodeRunning) return;
        encodeStopRequested = true;
        QueueStatusText.Text = "Stopping the current native task…";

        try
        {
            if (encodeScanning)
            {
                encodeInstance?.StopScan();
                FinishEncodeRun("Encode queue stopped by user.");
            }
            else
            {
                // Let the native poller report WorkDone before releasing its instance.
                encodeInstance?.StopEncode();
                QueueStatusText.Text = "Stop requested. Waiting for the native engine to finish shutting down…";
                CancelEncodeButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            FinishEncodeRun($"Unable to stop the native task cleanly: {ex.Message}");
        }
    }

    private JsonEncodeObject BuildEncodeJob(QueueDraft draft, SourceTitle title)
    {
        NativePresetChoice choice = nativePresetChoices.FirstOrDefault(item => item.Key == draft.PresetKey)
            ?? throw new InvalidOperationException("The selected native preset is no longer available.");
        HBPreset preset = choice.Preset;

        var audioTracks = new List<EncodeAudioTrack>();
        var audioRule = preset.AudioList?.FirstOrDefault();
        string audioEncoder = !string.IsNullOrWhiteSpace(audioRule?.AudioEncoder)
            ? audioRule.AudioEncoder
            : !string.IsNullOrWhiteSpace(preset.AudioEncoderFallback)
                ? preset.AudioEncoderFallback
                : "av_aac";

        var selectedAudio = SelectAudioTracks(draft, title);
        if (selectedAudio.Count > 0)
        {
            var nativeAudioEncoder = HandBrakeEncoderHelpers.AudioEncoders.FirstOrDefault(encoder => encoder.ShortName == audioEncoder);
            bool compatible = nativeAudioEncoder is not null && (FormatPresetContainer(preset.FileFormat) switch
            {
                "MP4" => nativeAudioEncoder.SupportsMP4,
                "MKV" => nativeAudioEncoder.SupportsMkv,
                "WebM" => nativeAudioEncoder.SupportsWebM,
                _ => false
            });
            if (!compatible)
                throw new InvalidOperationException("The preset audio encoder is unavailable or incompatible with the output container. Choose another preset.");
        }

        foreach (var sourceTrack in selectedAudio)
        {
            var mixdown = HandBrakeEncoderHelpers.GetMixdown(audioRule?.AudioMixdown ?? "stereo");
            bool audioQuality = audioRule?.AudioTrackQualityEnable ?? false;
            audioTracks.Add(new EncodeAudioTrack
            {
                Track = Math.Max(0, sourceTrack.TrackNumber - 1),
                Encoder = audioEncoder,
                Bitrate = audioQuality ? null : Math.Max(32, audioRule?.AudioBitrate ?? 160),
                Quality = audioQuality ? audioRule!.AudioTrackQuality : null,
                DRC = audioRule?.AudioTrackDRCSlider ?? 0,
                Gain = audioRule?.AudioTrackGainSlider ?? 0,
                Mixdown = mixdown?.Id ?? -1,
                NormalizeMixLevel = audioRule?.AudioNormalizeMixLevel ?? false,
                Samplerate = ResolveSampleRate(audioRule?.AudioSamplerate),
                Name = draft.AudioTracks?.FirstOrDefault(track => track.TrackNumber == sourceTrack.TrackNumber)?.Name ?? sourceTrack.Name,
                FilterList = new List<Filter>()
            });
        }

        int cropTop = title.Crop?.ElementAtOrDefault(0) ?? 0;
        int cropBottom = title.Crop?.ElementAtOrDefault(1) ?? 0;
        int cropLeft = title.Crop?.ElementAtOrDefault(2) ?? 0;
        int cropRight = title.Crop?.ElementAtOrDefault(3) ?? 0;
        int sourceWidth = title.Geometry?.Width ?? 0;
        int sourceHeight = title.Geometry?.Height ?? 0;
        int width = preset.PictureWidth is > 0
            ? preset.PictureWidth.Value
            : Math.Max(2, sourceWidth - cropLeft - cropRight);
        int height = preset.PictureHeight is > 0
            ? preset.PictureHeight.Value
            : Math.Max(2, sourceHeight - cropTop - cropBottom);

        if (draft.Geometry is { } geometry)
        {
            ValidateGeometry(geometry, title);
            width = geometry.Width;
            height = geometry.Height;
            cropTop = geometry.Top;
            cropBottom = geometry.Bottom;
            cropLeft = geometry.Left;
            cropRight = geometry.Right;
        }

        var filters = new List<Filter>();
        if (sourceWidth > 0 && sourceHeight > 0)
        {
            string cropSettings =
                $"width={width}:height={height}:crop-top={cropTop}:crop-bottom={cropBottom}:" +
                $"crop-left={cropLeft}:crop-right={cropRight}";
            string cropJson = HandBrakeFilterHelpers.GenerateFilterSettingJson(
                (int)hb_filter_ids.HB_FILTER_CROP_SCALE, null, null, cropSettings);
            if (!string.IsNullOrWhiteSpace(cropJson))
            {
                filters.Add(new Filter
                {
                    ID = (int)hb_filter_ids.HB_FILTER_CROP_SCALE,
                    Settings = JsonDocument.Parse(cropJson)
                });
            }
        }

        int frameRateMode = preset.VideoFramerateMode?.ToLowerInvariant() switch
        {
            "cfr" => 1,
            "pfr" => 2,
            _ => 0
        };
        string? frameRateName = string.IsNullOrWhiteSpace(draft.VideoFramerate)
            ? preset.VideoFramerate : draft.VideoFramerate;
        int nativeFrameRate = string.IsNullOrWhiteSpace(frameRateName) ||
            frameRateName.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? 0 : HandBrakeUnitConversionHelpers.GetFramerateFromName(frameRateName);
        string frameRateSettings = nativeFrameRate > 0
            ? $"mode={frameRateMode}:rate=27000000/{nativeFrameRate}"
            : $"mode={frameRateMode}";
        string frameRateJson = HandBrakeFilterHelpers.GenerateFilterSettingJson(
            (int)hb_filter_ids.HB_FILTER_VFR, null, null, frameRateSettings);
        if (!string.IsNullOrWhiteSpace(frameRateJson))
        {
            filters.Add(new Filter
            {
                ID = (int)hb_filter_ids.HB_FILTER_VFR,
                Settings = JsonDocument.Parse(frameRateJson)
            });
        }

        int parNum = draft.Geometry is null && preset.PicturePARWidth > 0
            ? preset.PicturePARWidth : title.Geometry?.PAR?.Num ?? 1;
        int parDen = draft.Geometry is null && preset.PicturePARHeight > 0
            ? preset.PicturePARHeight : title.Geometry?.PAR?.Den ?? 1;
        int chapterCount = Math.Max(1, title.ChapterList?.Count ?? 1);
        bool constantQuality = draft.VideoRateControl == "ConstantQuality";

        var metadata = title.MetaData is null
            ? new Dictionary<string, string>() : new Dictionary<string, string>(title.MetaData);
        if (draft.MetadataOverrides is not null)
            foreach (var entry in draft.MetadataOverrides) metadata[entry.Key] = entry.Value;

        return new JsonEncodeObject
        {
            SequenceID = 0,
            Audio = new Audio
            {
                AudioList = audioTracks,
                CopyMask = preset.AudioCopyMask?.ToArray() ?? Array.Empty<string>(),
                FallbackEncoder = preset.AudioEncoderFallback
            },
            Destination = new Destination
            {
                File = draft.DestinationPath!,
                Mux = string.IsNullOrWhiteSpace(preset.FileFormat)
                    ? ContainerToMux(draft.Container)
                    : preset.FileFormat,
                Options = new Options
                {
                    Optimize = preset.Optimize,
                    IpodAtom = preset.Mp4iPodCompatible
                },
                ChapterMarkers = draft.ChapterMarkers ?? preset.ChapterMarkers,
                AlignAVStart = preset.AlignAVStart,
                ChapterList = (title.ChapterList ?? new List<SourceChapter>())
                    .Select(chapter => new Chapter { Name = chapter.Name })
                    .ToList()
            },
            Filters = new Filters { FilterList = filters.Concat(BuildFilterOverrides(draft.FilterOverrides)).OrderBy(filter => filter.ID).ToList() },
            PAR = new PAR { Num = Math.Max(1, parNum), Den = Math.Max(1, parDen) },
            Metadata = metadata,
            CoverArts = title.CoverArts ?? new List<CoverArt>(),
            Source = new EncodeSource
            {
                Path = title.Path,
                Title = title.Index,
                Angle = 0,
                HWDecode = 0,
                KeepDuplicateTitles = title.KeepDuplicateTitles,
                Range = new EncodeRange { Type = "chapter", Start = 1, End = chapterCount }
            },
            Subtitle = new Subtitles
            {
                Search = new SubtitleSearch { Enable = false, Default = false, Burn = false, Forced = false },
                SubtitleList = new List<EncodeSubtitleTrack>()
            },
            Video = new Video
            {
                Encoder = draft.VideoEncoder,
                Level = preset.VideoLevel,
                Preset = draft.VideoEncoderSpeed ?? preset.VideoPreset,
                Profile = preset.VideoProfile,
                Tune = preset.VideoTune,
                Options = preset.VideoOptionExtra,
                Quality = constantQuality && draft.VideoQuality.HasValue
                    ? (decimal?)draft.VideoQuality.Value : null,
                Bitrate = !constantQuality ? draft.VideoBitrateKbps : null,
                MultiPass = !constantQuality && preset.VideoMultiPass,
                Turbo = !constantQuality && preset.VideoTurboMultiPass,
                ColorMatrixCode = preset.VideoColorMatrixCode,
                HardwareDecode = 0
            }
        };
    }

    private static int ResolveSampleRate(string? sampleRate)
    {
        if (string.IsNullOrWhiteSpace(sampleRate) ||
            sampleRate.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return 0;

        var match = HandBrakeEncoderHelpers.AudioSampleRates.FirstOrDefault(
            item => string.Equals(item.Name, sampleRate, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Rate;
        return int.TryParse(sampleRate, out int rate) ? rate : 0;
    }

    private static string ContainerToMux(string? container)
        => container?.ToUpperInvariant() switch
        {
            "MKV" => "av_mkv",
            "WEBM" => "av_webm",
            _ => "av_mp4"
        };

    private static string FormatEncodeTime(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private void FinishEncodeRun(string message)
    {
        encodeRunning = false;
        encodeScanning = false;
        encodeStopRequested = false;
        ReleaseEncodeInstance();
        QueueEncodeProgress.IsIndeterminate = false;
        QueueEncodeProgress.Value = 0;
        QueueEncodeProgress.Visibility = Visibility.Collapsed;
        QueueStatusText.Text = message;
        UpdateEncodeControls();
        UpdateQueueUi();
    }

    private void ReleaseEncodeInstance()
    {
        var instance = encodeInstance;
        encodeInstance = null;
        if (instance is null) return;

        instance.ScanProgress -= EncodeScanProgress;
        instance.ScanCompleted -= EncodeScanCompleted;
        instance.EncodeProgress -= EncodeProgressChanged;
        instance.EncodeCompleted -= EncodeJobCompleted;
        try { instance.Dispose(); }
        catch { /* Preserve the completed/failed job status if cleanup reports an error. */ }
    }

    private void UpdateEncodeControls()
    {
        bool nativeReady = File.Exists(Path.Combine(AppContext.BaseDirectory, "hb.dll")) &&
            nativePresetChoices.Count > 0;
        bool hasRunnableJobs = draftQueue.Any(item => item.HasVideoConfiguration);
        StartEncodeButton.IsEnabled = !encodeRunning && nativeReady && hasRunnableJobs;
        StartQueueEncodeButton.IsEnabled = !encodeRunning && nativeReady && hasRunnableJobs;
        CancelEncodeButton.IsEnabled = encodeRunning;
        RemoveDraftButton.IsEnabled = !encodeRunning && DraftQueueList.SelectedItem is QueueDraft;
        ClearDraftsButton.IsEnabled = !encodeRunning && draftQueue.Count > 0;
        StageQueueButton.IsEnabled = !encodeRunning && selectedSourcePaths.Count > 0;
    }
}


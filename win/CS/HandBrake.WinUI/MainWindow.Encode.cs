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
            BeginEncodeDiagnostic();
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

                string job = BuildEncodeJob(draft, title);
                QueueEncodeProgress.IsIndeterminate = true;
                QueueStatusText.Text =
                    $"Starting {activeEncodeQueueIndex + 1}/{activeEncodeQueue.Count} · {draft.DisplayName}";
                SaveEncodeJobDiagnostic(job);
                encodeInstance.StartEncode(job);
            }
            catch (Exception ex)
            {
                FinishEncodeRun(SaveEncodeFailureDiagnostic($"Unable to start {draft.DisplayName}: {ex.Message}", ex));
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
                    SaveEncodeFailureDiagnostic($"Encoding failed for {draft.DisplayName} (native error {e.Error})."));
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

    private string BuildEncodeJob(QueueDraft draft, SourceTitle title)
    {
        if (encodeInstance is null) throw new InvalidOperationException("The native HandBrake instance is unavailable.");
        return BuildNativeJob(draft, title, encodeInstance);
    }

    private List<EncodeAudioTrack> BuildManualAudio(QueueDraft draft, SourceTitle title, HBPreset preset)
    {
        var audioTracks = new List<EncodeAudioTrack>();
        var audioRule = preset.AudioList?.FirstOrDefault();
        string audioEncoder = !string.IsNullOrWhiteSpace(audioRule?.AudioEncoder)
            ? audioRule.AudioEncoder
            : !string.IsNullOrWhiteSpace(preset.AudioEncoderFallback)
                ? preset.AudioEncoderFallback
                : "av_aac";

        var selectedAudio = SelectAudioTracks(draft, title);
        foreach (var sourceTrack in selectedAudio)
        {
            var trackChoice = draft.AudioTracks?.FirstOrDefault(track => track.TrackNumber == sourceTrack.TrackNumber);
            bool customAudio = trackChoice?.Encoder is not null;
            string selectedEncoder = trackChoice?.Encoder ?? audioEncoder;
            var nativeAudioEncoder = HandBrakeEncoderHelpers.GetAudioEncoder(selectedEncoder);
            if (nativeAudioEncoder is null || !SupportsAudioContainer(nativeAudioEncoder, FormatPresetContainer(preset.FileFormat)))
                throw new InvalidOperationException("The audio encoder is unavailable or incompatible with the output container. Re-stage this job.");
            if (customAudio && (nativeAudioEncoder.IsPassthru || nativeAudioEncoder.IsLosslessEncoder))
                throw new InvalidOperationException("Custom bitrate editing does not support passthrough or lossless encoders.");
            var mixdown = customAudio
                ? HandBrakeEncoderHelpers.GetDefaultMixdown(nativeAudioEncoder, sourceTrack.ChannelLayout)
                : HandBrakeEncoderHelpers.GetMixdown(audioRule?.AudioMixdown ?? "stereo");
            int sampleRate = customAudio
                ? HandBrakeEncoderHelpers.SanitizeSampleRate(nativeAudioEncoder, sourceTrack.SampleRate)
                : ResolveSampleRate(audioRule?.AudioSamplerate);
            int requestedBitrate = trackChoice?.BitrateKbps ?? Math.Max(32, audioRule?.AudioBitrate ?? 160);
            if (customAudio && (mixdown is null || requestedBitrate < 6 || requestedBitrate > 1536 ||
                HandBrakeEncoderHelpers.SanitizeAudioBitrate(requestedBitrate, nativeAudioEncoder, sampleRate, mixdown) != requestedBitrate))
                throw new InvalidOperationException("The selected audio bitrate is not supported for this encoder and source layout. Re-stage with a supported bitrate.");
            bool audioQuality = !customAudio && (audioRule?.AudioTrackQualityEnable ?? false);
            audioTracks.Add(new EncodeAudioTrack
            {
                Track = Math.Max(0, sourceTrack.TrackNumber - 1),
                Encoder = selectedEncoder,
                Bitrate = audioQuality ? null : requestedBitrate,
                Quality = audioQuality ? audioRule!.AudioTrackQuality : null,
                DRC = customAudio ? 0 : audioRule?.AudioTrackDRCSlider ?? 0,
                Gain = customAudio ? 0 : audioRule?.AudioTrackGainSlider ?? 0,
                Mixdown = mixdown?.Id ?? -1,
                NormalizeMixLevel = !customAudio && (audioRule?.AudioNormalizeMixLevel ?? false),
                Samplerate = sampleRate,
                Name = draft.AudioTracks?.FirstOrDefault(track => track.TrackNumber == sourceTrack.TrackNumber)?.Name ?? sourceTrack.Name,
                FilterList = new List<Filter>()
            });
        }

        return audioTracks;
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

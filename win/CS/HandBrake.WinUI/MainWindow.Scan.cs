using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.Interfaces.EventArgs;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.Interop.Interop.Json.Encode;
using HandBrake.Interop.Interop.Json.Shared;
using HandBrake.Interop.Interop.Interfaces.Model.Preview;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

// The native HandBrake scanner runs asynchronously and reports events on a timer thread.
// All WinUI updates are marshalled to the window's DispatcherQueue.
public sealed partial class MainWindow
{
    private HandBrakeInstance? scanInstance;
    private bool scanInProgress;
    private bool scanWasCancelled;
    private bool encodeInProgress;
    private string? activeEncodeDestination;
    private readonly List<SourceTitle> scannedTitles = new();

    private void InitializeSourceScanner()
    {
        Closed += (_, _) =>
        {
            // Native cleanup must happen after the scan has stopped; avoid disposing
            // a live instance while its poll timer is still accessing libhb.
            if (scanInProgress)
            {
                try { scanInstance?.StopScan(); }
                catch { /* App is shutting down. */ }
            }
            else if (encodeInProgress)
            {
                try { scanInstance?.StopEncode(); }
                catch { /* App is shutting down. */ }
            }
            else
            {
                scanInstance?.Dispose();
            }
        };
    }

    private void StartSourceScan(IReadOnlyList<string> paths)
    {
        ResetScanDetails();
        if (paths.Count == 0) return;

        // A managed HandBrake.Interop reference does not ship native libhb.
        // Never silently substitute MediaInfo or fabricated video properties.
        string nativeDll = Path.Combine(AppContext.BaseDirectory, "hb.dll");
        if (!File.Exists(nativeDll))
        {
            SourceStatusText.Text =
                "Source selected. Native HandBrake scanner unavailable: hb.dll is not bundled " +
                "with this preview. The matching native libhb build must be packaged before scanning can run.";
            return;
        }

        try
        {
            scanInstance ??= CreateScanInstance();
            scanWasCancelled = false;
            scanInProgress = true;
            SourceScanProgress.Value = 0;
            SourceScanProgress.IsIndeterminate = true;
            SourceScanProgress.Visibility = Visibility.Visible;
            CancelScanButton.Visibility = Visibility.Visible;
            SourceStatusText.Text = "Scanning source with HandBrake...";

            scanInstance.StartScan(
                paths.ToList(),
                previewCount: SettingsPanel.Preferences.ScanPreviewCount,
                minDuration: TimeSpan.FromSeconds(SettingsPanel.Preferences.MinTitleDurationSeconds),
                maxDuration: TimeSpan.Zero,
                titleIndex: 0,
                excludedExtensions: new List<string>(),
                hwDecode: 0,
                keepDuplicateTitles: SettingsPanel.Preferences.KeepDuplicateTitles);
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                    or BadImageFormatException
                                    or EntryPointNotFoundException
                                    or InvalidOperationException)
        {
            ScanStartFailed(ex);
        }
        catch (Exception ex)
        {
            ScanStartFailed(ex);
        }
    }

    private HandBrakeInstance CreateScanInstance()
    {
        var instance = new HandBrakeInstance();
        instance.Initialize(verbosity: SettingsPanel.Preferences.ScanVerbosity, noHardware: true);
        instance.ScanProgress += Scanner_ScanProgress;
        instance.ScanCompleted += Scanner_ScanCompleted;
        return instance;
    }

    private void Scanner_ScanProgress(object? sender, ScanProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!scanInProgress) return;
            SourceScanProgress.IsIndeterminate = false;
            // HandBrake's scan progress is a fraction in [0, 1].
            SourceScanProgress.Value = Math.Clamp(e.Progress * 100.0, 0, 100);
            SourceStatusText.Text = $"Scanning title {e.CurrentTitle}/{e.Titles} · " +
                                    $"{SourceScanProgress.Value:0}%";
        });
    }

    private void Scanner_ScanCompleted(object? sender, EventArgs e)
    {
        var completedInstance = sender as HandBrakeInstance;
        // The interop layer fills Titles before raising ScanCompleted.
        var titles = completedInstance?.Titles?.TitleList?.ToList() ?? new List<SourceTitle>();
        int mainFeature = completedInstance?.FeatureTitle ?? 0;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!scanInProgress) return; // Ignore completion after cancellation.
            scanInProgress = false;
            SourceScanProgress.Visibility = Visibility.Collapsed;
            CancelScanButton.Visibility = Visibility.Collapsed;

            if (scanWasCancelled)
            {
                SourceStatusText.Text = "Source scan cancelled.";
                return;
            }

            scannedTitles.Clear();
            scannedTitles.AddRange(titles);
            SourceTitlePicker.Items.Clear();

            foreach (var title in scannedTitles)
            {
                var duration = FormatDuration(title.Duration);
                SourceTitlePicker.Items.Add(new ComboBoxItem
                {
                    Content = $"Title {title.Index}: {title.Name ?? Path.GetFileName(title.Path)} · {duration}",
                    Tag = title
                });
            }

            SourceTitlePicker.Visibility = scannedTitles.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

            if (scannedTitles.Count == 0)
            {
                SourceStatusText.Text = "HandBrake scan completed, but no usable video titles were found.";
                return;
            }

            int index = scannedTitles.FindIndex(t => t.Index == mainFeature);
            SourceTitlePicker.SelectedIndex = index >= 0 ? index : 0;
            SourceStatusText.Text = $"Scan complete · {scannedTitles.Count} title(s) detected by HandBrake.";
        });
    }

    private void SourceTitlePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle title })
        {
            DisplayScannedTitle(title);
        }
    }

    private void DisplayScannedTitle(SourceTitle title)
    {
        GeneratePreviewButton.IsEnabled = true;
        UpdateEncodeAvailability();
        selectedScannedTitleName = title.Name;
        UpdateSuggestedDestination();
        string resolution = title.Geometry is null
            ? "Unknown resolution"
            : $"{title.Geometry.Width} × {title.Geometry.Height}";
        string fps = title.FrameRate is { Den: > 0 } rate
            ? $"{(double)rate.Num / rate.Den:0.###} fps"
            : "Unknown frame rate";
        string duration = FormatDuration(title.Duration);
        int audioCount = title.AudioList?.Count ?? 0;
        int subtitleCount = title.SubtitleList?.Count ?? 0;
        int chapterCount = title.ChapterList?.Count ?? 0;

        ScanSummaryText.Text = $"Title {title.Index} · {duration}\n" +
            $"{resolution} · {fps}\n" +
            $"{audioCount} audio track(s) · {subtitleCount} subtitle track(s) · {chapterCount} chapter(s)";
        SourceDimensionsText.Text = $"Source: {resolution}";
        SourceVideoText.Text = $"Source codec: {title.VideoCodec ?? "Unknown"}\n" +
                               $"Frame rate: {fps}\nContainer: {title.Container ?? "Unknown"}";
        PreviewMetadataText.Text = $"{resolution} · {fps}\n" +
                                   $"{duration} · {title.VideoCodec ?? "Unknown codec"}";
        PreviewSourceText.Text = title.Name ?? Path.GetFileName(title.Path) ?? "Video source";
        ChapterListText.Text = chapterCount > 0
            ? $"{chapterCount} chapter(s) detected in the source."
            : "No chapters detected.";

        AudioTrackList.Children.Clear();
        if (audioCount == 0) AddDetailLine(AudioTrackList, "No audio tracks detected.");
        else foreach (var track in title.AudioList!)
            AddDetailLine(AudioTrackList,
                $"{track.TrackNumber}. {track.Language ?? "Unknown language"} · " +
                $"{track.CodecName ?? track.Description ?? "Unknown codec"} · " +
                $"{track.ChannelCount} channels");

        SubtitleTrackList.Children.Clear();
        if (subtitleCount == 0) AddDetailLine(SubtitleTrackList, "No subtitle tracks detected.");
        else foreach (var track in title.SubtitleList!)
            AddDetailLine(SubtitleTrackList,
                $"{track.TrackNumber}. {track.Language ?? "Unknown language"} · " +
                $"{track.SourceName ?? track.Format ?? "Subtitle"}");
    }

    private static void AddDetailLine(StackPanel panel, string content)
    {
        panel.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });
    }

    private static string FormatDuration(HandBrake.Interop.Interop.Json.Scan.Duration? duration)
    {
        if (duration is null) return "Unknown duration";
        return $"{duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void UpdateEncodeAvailability()
    {
        bool hasTitle = SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle _ };
        bool hasNativeEngine = File.Exists(Path.Combine(AppContext.BaseDirectory, "hb.dll"));
        StartEncodeButton.IsEnabled = hasTitle && activePreset is not null &&
            hasNativeEngine && !scanInProgress && !encodeInProgress;
    }

    private void StartEncode_Click(object sender, RoutedEventArgs e)
    {
        if (encodeInProgress) return;
        if (scanInstance is null ||
            SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title } ||
            activePreset is null)
        {
            SourceStatusText.Text = "Scan a source and select a native HandBrake preset before encoding.";
            return;
        }

        string? destination = GetSuggestedOutputPath();
        if (string.IsNullOrWhiteSpace(destination) ||
            string.IsNullOrWhiteSpace(Path.GetDirectoryName(destination)) ||
            !Directory.Exists(Path.GetDirectoryName(destination)))
        {
            SourceStatusText.Text = "Choose a valid output folder before starting the encode.";
            return;
        }

        try
        {
            JsonEncodeObject job = CreateEncodeJob(title, destination);
            activeEncodeDestination = destination;
            encodeInProgress = true;
            StartEncodeButton.IsEnabled = false;
            GeneratePreviewButton.IsEnabled = false;
            SourceScanProgress.IsIndeterminate = true;
            SourceScanProgress.Value = 0;
            SourceScanProgress.Visibility = Visibility.Visible;
            SourceStatusText.Text = "Starting native HandBrake encode…";

            scanInstance.EncodeProgress -= NativeEncodeProgress;
            scanInstance.EncodeCompleted -= NativeEncodeCompleted;
            scanInstance.EncodeProgress += NativeEncodeProgress;
            scanInstance.EncodeCompleted += NativeEncodeCompleted;
            scanInstance.StartEncode(job);
        }
        catch (Exception ex)
        {
            encodeInProgress = false;
            SourceScanProgress.Visibility = Visibility.Collapsed;
            SourceStatusText.Text = "HandBrake could not start encoding: " + ex.Message;
            UpdateEncodeAvailability();
        }
    }

    private JsonEncodeObject CreateEncodeJob(SourceTitle title, string destination)
    {
        var preset = activePreset!;
        var audioPreset = preset.AudioList?.FirstOrDefault();
        string encoder = ActiveVideoEncoder?.ShortName ?? preset.VideoEncoder ?? "x264";
        bool useQuality = VideoRateModePicker.SelectedIndex == 0;
        decimal quality = (decimal)Math.Clamp(VideoQualitySlider.Value, 0, 51);
        int bitrate = (int)Math.Clamp(VideoBitrateBox.Value, 100, 200000);
        var audioTracks = new List<AudioTrack>();

        // Use the active HandBrake preset's audio template for each source track.
        // Track selection remains explicit; no audio tracks are invented if the source has none.
        if (title.AudioList is not null)
        {
            foreach (var sourceTrack in title.AudioList)
            {
                audioTracks.Add(new AudioTrack
                {
                    Track = sourceTrack.TrackNumber,
                    Encoder = audioPreset?.AudioEncoder ?? preset.AudioEncoderFallback ?? "av_aac",
                    Bitrate = (audioPreset?.AudioBitrate ?? 0) > 0 ? audioPreset!.AudioBitrate : 160,
                    CompressionLevel = audioPreset?.AudioCompressionLevel,
                    DRC = audioPreset?.AudioTrackDRCSlider ?? 0,
                    Gain = audioPreset?.AudioTrackGainSlider ?? 0,
                    Mixdown = 0,
                    Samplerate = int.TryParse(audioPreset?.AudioSamplerate, out int sampleRate) ? sampleRate : 0,
                    Quality = audioPreset?.AudioTrackQualityEnable == true ? audioPreset.AudioTrackQuality : null,
                    NormalizeMixLevel = audioPreset?.AudioNormalizeMixLevel ?? false,
                    FilterList = new List<Filter>()
                });
            }
        }

        return new JsonEncodeObject
        {
            SequenceID = 0,
            Source = new HandBrake.Interop.Interop.Json.Encode.Source
            {
                Path = title.Path,
                Title = title.Index,
                Angle = 1,
                HWDecode = 0,
                KeepDuplicateTitles = SettingsPanel.Preferences.KeepDuplicateTitles,
                Range = new HandBrake.Interop.Interop.Json.Encode.Range
                {
                    Type = "chapter",
                    Start = 1,
                    End = Math.Max(1, title.ChapterList?.Count ?? 1)
                }
            },
            Destination = new Destination
            {
                File = destination,
                Mux = preset.FileFormat ?? "av_mp4",
                ChapterMarkers = preset.ChapterMarkers,
                AlignAVStart = preset.AlignAVStart,
                Options = new Options { Optimize = preset.Optimize, IpodAtom = preset.Mp4iPodCompatible },
                ChapterList = title.ChapterList?.Select(chapter => new Chapter { Name = chapter.Name }).ToList()
                    ?? new List<Chapter>()
            },
            Video = new HandBrake.Interop.Interop.Json.Encode.Video
            {
                Encoder = encoder,
                Preset = preset.VideoPreset ?? "medium",
                Tune = preset.VideoTune ?? string.Empty,
                Profile = preset.VideoProfile ?? string.Empty,
                Level = preset.VideoLevel ?? string.Empty,
                Options = preset.VideoOptionExtra ?? string.Empty,
                Quality = useQuality ? quality : null,
                Bitrate = useQuality ? null : bitrate,
                MultiPass = !useQuality && preset.VideoMultiPass,
                Turbo = !useQuality && preset.VideoTurboMultiPass,
                HardwareDecode = 0
            },
            Audio = new Audio
            {
                AudioList = audioTracks,
                CopyMask = preset.AudioCopyMask?.ToArray() ?? Array.Empty<string>(),
                FallbackEncoder = preset.AudioEncoderFallback ?? "av_aac"
            },
            Subtitle = new Subtitles
            {
                Search = new SubtitleSearch
                {
                    Enable = preset.SubtitleAddForeignAudioSearch,
                    Default = false,
                    Burn = preset.SubtitleAddForeignAudioSubtitle,
                    Forced = false
                },
                SubtitleList = new List<SubtitleTrack>()
            },
            Filters = new Filters { FilterList = new List<Filter>() },
            PAR = new PAR { Num = 1, Den = 1 },
            Metadata = new Dictionary<string, string>(),
            CoverArts = new List<CoverArt>()
        };
    }

    private void NativeEncodeProgress(object? sender, EncodeProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!encodeInProgress) return;
            SourceScanProgress.IsIndeterminate = false;
            SourceScanProgress.Value = Math.Clamp(e.FractionComplete * 100.0, 0, 100);
            SourceScanProgress.Visibility = Visibility.Visible;
            SourceStatusText.Text = $"Encoding · {SourceScanProgress.Value:0}% · " +
                $"{e.AverageFrameRate:0.0} fps average · {e.EstimatedTimeLeft:hh\\:mm\\:ss} remaining";
        });
    }

    private void NativeEncodeCompleted(object? sender, EncodeCompletedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            encodeInProgress = false;
            SourceScanProgress.Visibility = Visibility.Collapsed;
            if (scanInstance is not null)
            {
                scanInstance.EncodeProgress -= NativeEncodeProgress;
                scanInstance.EncodeCompleted -= NativeEncodeCompleted;
            }

            if (e.Error == 0 && !string.IsNullOrWhiteSpace(activeEncodeDestination) &&
                File.Exists(activeEncodeDestination))
            {
                SourceStatusText.Text = "Encode completed · " + activeEncodeDestination;
            }
            else
            {
                SourceStatusText.Text = $"HandBrake encode ended with error code {e.Error}. Check the output path and selected preset.";
            }

            UpdateEncodeAvailability();
        });
    }

    private async void GeneratePreview_Click(object sender, RoutedEventArgs e)
    {
        if (scanInstance is null ||
            SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title })
        {
            PreviewStatusText.Text = "Scan a source and select a title before generating a preview.";
            return;
        }

        GeneratePreviewButton.IsEnabled = false;
        PreviewStatusText.Text = "Rendering a frame with the HandBrake engine…";
        PreviewPlaceholderText.Text = "Rendering preview…";
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewFrameImage.Visibility = Visibility.Collapsed;

        try
        {
            JsonEncodeObject job = CreatePreviewJob(title);
            RawPreviewData raw = await Task.Run(() => scanInstance.GetPreview(job, 0));
            if (raw is null || raw.Width <= 0 || raw.Height <= 0 ||
                raw.RawBitmapData is null || raw.RawBitmapData.Length < raw.StrideWidth * raw.Height)
            {
                throw new InvalidOperationException("HandBrake did not return a valid preview frame.");
            }

            var buffer = CryptographicBuffer.CreateFromByteArray(raw.RawBitmapData);
            var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer, BitmapPixelFormat.Bgra8, raw.Width, raw.Height,
                BitmapAlphaMode.Ignore, raw.StrideWidth, 0);
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(bitmap);

            PreviewFrameImage.Source = source;
            PreviewFrameImage.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            PreviewStatusText.Text = $"Preview ready · {raw.Width} × {raw.Height}";
        }
        catch (Exception ex)
        {
            PreviewPlaceholderText.Text = "The native preview could not be rendered.";
            PreviewStatusText.Text = "Preview failed: " + ex.Message;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewFrameImage.Visibility = Visibility.Collapsed;
        }
        finally
        {
            GeneratePreviewButton.IsEnabled = SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle };
        }
    }

    private JsonEncodeObject CreatePreviewJob(SourceTitle title)
    {
        var preset = activePreset;
        string encoder = ActiveVideoEncoder?.ShortName ?? preset?.VideoEncoder ?? "x264";
        bool useQuality = VideoRateModePicker.SelectedIndex == 0;
        decimal quality = (decimal)Math.Clamp(VideoQualitySlider.Value, 0, 51);
        int bitrate = (int)Math.Clamp(VideoBitrateBox.Value, 100, 200000);
        string mux = preset?.FileFormat ?? "av_mp4";

        return new JsonEncodeObject
        {
            SequenceID = 0,
            Source = new HandBrake.Interop.Interop.Json.Encode.Source
            {
                Path = title.Path,
                Title = title.Index,
                Angle = 1,
                HWDecode = 0,
                KeepDuplicateTitles = SettingsPanel.Preferences.KeepDuplicateTitles,
                Range = new HandBrake.Interop.Interop.Json.Encode.Range
                {
                    Type = "preview",
                    Start = 1,
                    End = 30 * 90000,
                    SeekPoints = Math.Max(1, SettingsPanel.Preferences.ScanPreviewCount)
                }
            },
            Destination = new Destination
            {
                File = Path.Combine(Path.GetTempPath(), "HandBrake-WinUI-preview.mp4"),
                Mux = mux,
                ChapterMarkers = false,
                AlignAVStart = preset?.AlignAVStart ?? false,
                Options = new Options
                {
                    Optimize = preset?.Optimize ?? false,
                    IpodAtom = preset?.Mp4iPodCompatible ?? false
                },
                ChapterList = new List<Chapter>()
            },
            Video = new HandBrake.Interop.Interop.Json.Encode.Video
            {
                Encoder = encoder,
                Preset = preset?.VideoPreset ?? "medium",
                Tune = preset?.VideoTune ?? string.Empty,
                Profile = preset?.VideoProfile ?? string.Empty,
                Level = preset?.VideoLevel ?? string.Empty,
                Options = preset?.VideoOptionExtra ?? string.Empty,
                Quality = useQuality ? quality : null,
                Bitrate = useQuality ? null : bitrate,
                MultiPass = false,
                Turbo = false,
                HardwareDecode = 0
            },
            Audio = new Audio
            {
                AudioList = new List<AudioTrack>(),
                CopyMask = preset?.AudioCopyMask?.ToArray() ?? Array.Empty<string>(),
                FallbackEncoder = preset?.AudioEncoderFallback ?? "av_aac"
            },
            Subtitle = new Subtitles
            {
                Search = new SubtitleSearch { Enable = false, Default = false, Burn = false, Forced = false },
                SubtitleList = new List<SubtitleTrack>()
            },
            Filters = new Filters { FilterList = new List<Filter>() },
            PAR = new PAR { Num = 1, Den = 1 },
            Metadata = new Dictionary<string, string>(),
            CoverArts = new List<CoverArt>()
        };
    }

    private void ResetScanDetails()
    {
        scannedTitles.Clear();
        SourceTitlePicker.Items.Clear();
        SourceTitlePicker.Visibility = Visibility.Collapsed;
        ScanSummaryText.Text = "Scanning has not produced any title information yet.";
        SourceDimensionsText.Text = "Source dimensions are not available.";
        SourceVideoText.Text = "Source video properties are not available.";
        PreviewMetadataText.Text = "No decoded preview frame yet.";
        PreviewStatusText.Text = "Preview is waiting for a scanned title.";
        PreviewPlaceholderText.Text = "Scan a source, then generate a frame.";
        PreviewFrameImage.Source = null;
        PreviewFrameImage.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        GeneratePreviewButton.IsEnabled = false;
        StartEncodeButton.IsEnabled = false;
        AudioTrackList.Children.Clear();
        SubtitleTrackList.Children.Clear();
        AddDetailLine(AudioTrackList, "Waiting for source scan.");
        AddDetailLine(SubtitleTrackList, "Waiting for source scan.");
        ChapterListText.Text = "Waiting for source scan.";
    }

    private void ScanStartFailed(Exception error)
    {
        scanInProgress = false;
        SourceScanProgress.Visibility = Visibility.Collapsed;
        CancelScanButton.Visibility = Visibility.Collapsed;
        SourceStatusText.Text = "HandBrake native scan could not start: " + error.Message +
            " Check that hb.dll and its dependencies match this application build.";
        // A failed init may leave an unusable instance; don't reuse it.
        // Avoid reusing a native instance whose initialization failed.
        // Do not dispose it here: native initialization may have failed part-way.
        scanInstance = null;
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (!scanInProgress || scanInstance is null) return;
        scanWasCancelled = true;
        SourceStatusText.Text = "Stopping scan...";
        CancelScanButton.IsEnabled = false;
        try
        {
            scanInstance.StopScan();
            scanInProgress = false;
            SourceScanProgress.Visibility = Visibility.Collapsed;
            CancelScanButton.Visibility = Visibility.Collapsed;
            SourceStatusText.Text = "Source scan cancelled.";
        }
        catch (Exception ex)
        {
            SourceStatusText.Text = "Unable to stop scan: " + ex.Message;
        }
        finally
        {
            CancelScanButton.IsEnabled = true;
        }
    }
}

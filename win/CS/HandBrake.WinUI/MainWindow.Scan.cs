using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.Interfaces.EventArgs;
using HandBrake.Interop.Interop.Json.Scan;
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
                previewCount: 10,
                minDuration: TimeSpan.Zero,
                maxDuration: TimeSpan.Zero,
                titleIndex: 0,
                excludedExtensions: new List<string>(),
                hwDecode: 0,
                keepDuplicateTitles: false);
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
        instance.Initialize(verbosity: 1, noHardware: true);
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

    private static string FormatDuration(Duration? duration)
    {
        if (duration is null) return "Unknown duration";
        return $"{duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
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
        scanInstance = null;
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (!scanInProgress || scanInstance is null) return;
        scanWasCancelled = true;
        SourceStatusText.Text = "Stopping scan...";
        CancelScanButton.IsEnabled = false;
        try { scanInstance.StopScan(); }
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

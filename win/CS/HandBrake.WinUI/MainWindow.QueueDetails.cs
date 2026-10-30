using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private bool encodePaused;
    private bool preparingEncodeQueue;
    private DateTimeOffset lastQueueCheckpoint;
    private static string? activeQueueLogPath;
    private QueueDraft? ActiveQueueJob => encodeRunning && activeEncodeQueueIndex < activeEncodeQueue.Count
        ? activeEncodeQueue[activeEncodeQueueIndex] : null;

    private void InitializeQueueLogRefresh()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (QueuePanel.Visibility == Visibility.Visible && QueueDetailTabs.SelectedIndex == 2)
                RefreshSelectedQueueLog();
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void QueueBody_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool narrow = args.NewSize.Width < 900;
        QueueListColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0.65, GridUnitType.Star);
        QueueDetailColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        QueueListRow.Height = narrow ? new GridLength(0.55, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        QueueDetailRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(QueueDetailsCard, narrow ? 0 : 1);
        Grid.SetRow(QueueDetailsCard, narrow ? 1 : 0);
        QueueBody.ColumnSpacing = narrow ? 0 : 12;
        QueueBody.RowSpacing = narrow ? 12 : 0;
    }

    private void QueueDetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs args) => RefreshQueueDetails();
    private void RefreshQueueLog_Click(object sender, RoutedEventArgs args) => RefreshSelectedQueueLog();

    private void RefreshQueueDetails()
    {
        if (QueueJobLogText is null || QueueJobStatsText is null) return;
        QueueSummaryFields.Children.Clear();
        if (DraftQueueList.SelectedItem is not QueueDraft job)
        {
            QueueSelectedTitle.Text = "Select a job";
            QueueJobError.IsOpen = false;
            QueueJobProgress.Value = 0;
            QueueJobStatsText.Text = "No job selected.";
            QueueJobLogText.Text = "No job selected.";
            return;
        }
        QueueSelectedTitle.Text = job.OutputName;
        QueueJobError.IsOpen = !string.IsNullOrWhiteSpace(job.LastError);
        QueueJobError.Message = job.LastError ?? "";
        QueueDetail("Status", job.StateText);
        QueueDetail("Source", job.SourcePath);
        QueueDetail("Destination", job.DestinationPath ?? "Not configured");
        QueueDetail("Title / range", $"Title {job.TitleIndex?.ToString() ?? "—"} · " +
            (job.Selection is null ? "Full title" : $"Angle {job.Selection.Angle} · {RangeName(job.Selection.Type)}" +
                (job.Selection.Type == "all" ? "" : $" · {job.Selection.Start:0.###} – {job.Selection.End:0.###}")));
        QueueDetail("Preset", job.PresetName ?? "Not configured");
        QueueDetail("Video", !job.HasVideoConfiguration ? "Not configured" : $"{job.VideoEncoder} · " +
            (job.VideoRateControl == "ConstantQuality" ? $"Quality {job.VideoQuality:0.##}" : $"{job.VideoBitrateKbps} kbps") + $" · {job.Container}");
        if (job.NativePresetJson is { Length: > 0 })
        {
            try
            {
                var preset = JsonNode.Parse(job.NativePresetJson)!;
                QueueDetail("Video options", $"Speed: {SavedOption(preset, "VideoPreset", "Default")} · Tune: {SavedOption(preset, "VideoTune", "None")}\n" +
                    $"Profile: {SavedOption(preset, "VideoProfile", "Auto")} · Level: {SavedOption(preset, "VideoLevel", "Auto")}\n" +
                    $"Frame rate: {job.VideoFramerate ?? "auto"} ({preset["VideoFramerateMode"]}) · Colour: {SavedOption(preset, "VideoColorRange", "Default")}\n" +
                    $"Arguments: {SavedOption(preset, "VideoOptionExtra", "None")}");
            }
            catch (System.Text.Json.JsonException) { QueueDetail("Preset snapshot", "The saved preset JSON is invalid."); }
        }
        QueueDetail("Audio", job.UsePresetAudio ? "Native preset rules" : $"{job.AudioTracks?.Count.ToString() ?? "Legacy selection"} selected track(s)");
        QueueDetail("Subtitles", job.UsePresetSubtitles ? "Native preset rules" : $"{job.Subtitles?.Count ?? 0} internal · {job.SubtitleImports?.Count ?? 0} imported" + (job.ForeignAudioSearch ? " · foreign-audio search" : ""));
        QueueDetail("Geometry", job.Geometry is { } geometry
            ? $"{geometry.Width} × {geometry.Height} · crop T{geometry.Top} B{geometry.Bottom} L{geometry.Left} R{geometry.Right}"
            : "Native preset geometry");
        QueueDetail("Filters", job.UsePresetFilters ? "Native preset filters" : $"{job.FilterOverrides?.Count ?? 0} manual effect filter(s)");
        if (!string.IsNullOrWhiteSpace(job.ResolvedSummary)) QueueDetail("Last resolved job", job.ResolvedSummary);
        UpdateQueueStatistics(job);
        if (QueueDetailTabs.SelectedIndex == 2) RefreshSelectedQueueLog();
    }

    private static string SavedOption(JsonNode preset, string key, string fallback)
        => preset[key]?.ToString() is { Length: > 0 } text ? text : fallback;

    private static string RangeName(string type) => type switch
    {
        "all" => "Full title", "chapter" => "Chapters", "time" => "Seconds", "frame" => "Frames", _ => "Unsupported range"
    };

    private void QueueDetail(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        QueueSummaryFields.Children.Add(row);
    }

    private void UpdateQueueStatistics(QueueDraft job)
    {
        QueueJobProgress.Value = Math.Clamp(job.Progress, 0, 100);
        string elapsed = job.StartedAt is { } start ? FormatEncodeTime((job.FinishedAt ?? DateTimeOffset.Now) - start) : "—";
        QueueJobStatsText.Text = $"Status: {job.StateText}\nProgress: {job.Progress:0.0}%\n" +
            $"Average: {(job.AverageFps > 0 ? job.AverageFps.ToString("0.0") + " fps" : "—")}\n" +
            $"Remaining: {(job.RemainingSeconds is > 0 ? FormatEncodeTime(TimeSpan.FromSeconds(job.RemainingSeconds.Value)) : "—")}\n" +
            $"Elapsed since start: {elapsed}\nStarted: {job.StartedAt?.ToLocalTime().ToString("g") ?? "—"}\n" +
            $"Finished: {job.FinishedAt?.ToLocalTime().ToString("g") ?? "—"}\n" +
            $"Output size: {(job.OutputBytes is { } bytes ? (bytes / 1048576d).ToString("0.##") + " MiB" : "—")}" +
            (job.NativeErrorCode.HasValue ? $"\nNative error: {job.NativeErrorCode}" : "");
    }

    private void RefreshSelectedQueueLog()
    {
        if (DraftQueueList.SelectedItem is not QueueDraft job) return;
        try
        {
            if (!File.Exists(job.JobLogPath)) { QueueJobLogText.Text = "This job has no recorded activity yet."; return; }
            using var stream = new FileStream(job.JobLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool tailOnly = stream.Length > 65536;
            if (tailOnly) stream.Seek(-65536, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (tailOnly) reader.ReadLine();
            string content = reader.ReadToEnd();
            if (QueueJobLogText.Text != content) QueueJobLogText.Text = content;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { QueueJobLogText.Text = "Unable to read this job's activity: " + error.Message; }
    }

    private void BeginQueueJob(QueueDraft job)
    {
        job.State = "Scanning";
        job.Progress = job.AverageFps = 0;
        job.RemainingSeconds = null;
        job.StartedAt = DateTimeOffset.Now;
        job.FinishedAt = null;
        job.LastError = null;
        job.NativeErrorCode = null;
        job.OutputBytes = null;
        job.ResolvedSummary = null;
        encodePaused = false;
        lock (NativeLogLock) activeQueueLogPath = job.JobLogPath;
        try
        {
            File.Delete(job.JobJsonPath);
            File.Delete(Path.ChangeExtension(job.JobLogPath, ".error.txt"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { QueueJobNote(job, "Unable to clear previous attempt diagnostics: " + error.Message); }
        lastQueueCheckpoint = DateTimeOffset.Now;
        QueueJobNote(job, "Starting job");
        DraftQueueList.SelectedItem = job;
        job.NotifyRuntimeChanged();
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void CheckpointQueueProgress()
    {
        if (DateTimeOffset.Now - lastQueueCheckpoint < TimeSpan.FromSeconds(5)) return;
        lastQueueCheckpoint = DateTimeOffset.Now;
        SaveDraftQueue();
    }

    private static void QueueJobNote(QueueDraft job, string message)
    {
        try
        {
            lock (NativeLogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.JobLogPath)!);
                File.AppendAllText(job.JobLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine(error.Message); }
    }

    private void SaveQueueJobJson(QueueDraft job, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.JobJsonPath)!);
            File.WriteAllText(job.JobJsonPath, json);
            var resolved = JsonNode.Parse(json)!;
            job.ResolvedSummary = $"Audio outputs: {resolved["Audio"]?["AudioList"]?.AsArray().Count ?? 0} · " +
                $"Subtitle outputs: {resolved["Subtitle"]?["SubtitleList"]?.AsArray().Count ?? 0}\n" +
                $"Video: {resolved["Video"]?["Encoder"]} · Preset: {resolved["Video"]?["Preset"]}\n" +
                $"Filters: {string.Join(", ", resolved["Filters"]!["FilterList"]!.AsArray().Select(filter => ((HandBrake.Interop.Interop.HbLib.hb_filter_ids)filter!["ID"]!.GetValue<int>()).ToString().Replace("HB_FILTER_", "").Replace("_", " ").ToLowerInvariant()))}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { QueueJobNote(job, "Unable to save job details: " + error.Message); }
    }

    private void FailQueueValidation(QueueDraft job, string message)
    {
        job.State = "Failed";
        job.LastError = message;
        job.FinishedAt = DateTimeOffset.Now;
        job.NotifyRuntimeChanged();
        QueueStatusText.Text = message;
        QueueJobNote(job, message);
        DraftQueueList.SelectedItem = job;
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void ResetQueueJob_Click(object sender, RoutedEventArgs args)
    {
        if (encodeRunning || preparingEncodeQueue || DraftQueueList.SelectedItem is not QueueDraft { HasVideoConfiguration: true } job) return;
        job.State = "Waiting";
        job.Progress = job.AverageFps = 0;
        job.RemainingSeconds = null;
        job.LastError = null;
        job.NativeErrorCode = null;
        job.StartedAt = job.FinishedAt = null;
        job.OutputBytes = null;
        job.NotifyRuntimeChanged();
        QueueJobNote(job, "Reset to waiting");
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void MoveQueueUp_Click(object sender, RoutedEventArgs args) => MoveSelectedQueueJob(-1);
    private void MoveQueueDown_Click(object sender, RoutedEventArgs args) => MoveSelectedQueueJob(1);
    private void MoveSelectedQueueJob(int offset)
    {
        if (encodeRunning || preparingEncodeQueue || DraftQueueList.SelectedItem is not QueueDraft job) return;
        int index = draftQueue.IndexOf(job), target = index + offset;
        if (target < 0 || target >= draftQueue.Count) return;
        draftQueue.Move(index, target);
        DraftQueueList.SelectedItem = job;
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void PauseQueue_Click(object sender, RoutedEventArgs args)
    {
        if (!encodeRunning || encodeScanning || encodeStopRequested || encodeInstance is null || ActiveQueueJob is not { } job) return;
        try
        {
            if (encodePaused) encodeInstance.ResumeEncode(); else encodeInstance.PauseEncode();
            encodePaused = !encodePaused;
            job.State = encodePaused ? "Paused" : "Encoding";
            job.NotifyRuntimeChanged();
            QueueJobNote(job, job.State);
            QueueStatusText.Text = encodePaused ? "Queue paused." : "Queue resumed.";
            SaveDraftQueue();
            UpdateQueueUi();
        }
        catch (Exception error) { QueueStatusText.Text = "Unable to change pause state: " + error.Message; }
    }

    private void OpenQueueSource_Click(object sender, RoutedEventArgs args)
    {
        if (encodeRunning || preparingEncodeQueue || DraftQueueList.SelectedItem is not QueueDraft job) return;
        AcceptSourcePaths(new[] { job.SourcePath });
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
    }

    private async void OpenQueueOutput_Click(object sender, RoutedEventArgs args)
    {
        if (DraftQueueList.SelectedItem is not QueueDraft job) return;
        try
        {
            string? path = Path.GetDirectoryName(job.DestinationPath);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            { QueueStatusText.Text = "The output folder is unavailable."; return; }
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception error) { QueueStatusText.Text = "Unable to open output folder: " + error.Message; }
    }
}

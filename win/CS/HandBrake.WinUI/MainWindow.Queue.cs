using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

/// <summary>
/// A persisted configuration snapshot, not an executable HandBrake job.
/// The complete audio, subtitle, geometry and filter job graph must be built
/// and validated before the encode action can be enabled.
/// </summary>
public sealed class QueueDraft : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = "Waiting";
    public double Progress { get; set; }
    public double AverageFps { get; set; }
    public double? RemainingSeconds { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? OutputBytes { get; set; }
    public string? LastError { get; set; }
    public int? NativeErrorCode { get; set; }
    public string? ResolvedSummary { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyRuntimeChanged()
    {
        foreach (string name in new[] { nameof(State), nameof(StateText), nameof(StateIcon), nameof(Progress), nameof(RowSummary), nameof(OutputName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    [JsonIgnore] public bool IsPending => HasVideoConfiguration && State == "Waiting";
    [JsonIgnore] public Symbol StateIcon => !HasVideoConfiguration ? Symbol.OpenFile : State switch
    {
        "Completed" => Symbol.Accept, "Failed" or "Interrupted" => Symbol.Important,
        "Paused" => Symbol.Pause, "Encoding" => Symbol.Play, "Scanning" => Symbol.Find,
        "Canceled" => Symbol.Cancel, _ => Symbol.Clock
    };
    [JsonIgnore] public string StateText => !HasVideoConfiguration ? "Needs setup" : State;
    [JsonIgnore] public string OutputName => string.IsNullOrWhiteSpace(DestinationPath) ? DisplayName : Path.GetFileName(DestinationPath);
    [JsonIgnore] public string RowSummary => StateText + (State is "Scanning" or "Encoding" or "Paused" ? $" · {Progress:0}%" : "") + (string.IsNullOrEmpty(PresetName) ? "" : " · " + PresetName);
    [JsonIgnore] public string JobLogPath => Path.Combine(Path.GetDirectoryName(AppPreferences.StorageFile)!, "diagnostics", "jobs", Id.ToString("N") + ".log");
    [JsonIgnore] public string JobJsonPath => Path.ChangeExtension(JobLogPath, ".json");

    public string? NativePresetJson { get; set; }
    public EncodeSelection? Selection { get; set; }
    public bool UsePresetAudio { get; set; }
    public bool UsePresetSubtitles { get; set; }
    public bool UsePresetFilters { get; set; }
    public bool ForeignAudioSearch { get; set; }
    public bool ForeignAudioForced { get; set; } = true;
    public bool ForeignAudioBurn { get; set; }
    public List<ImportedSubtitle>? SubtitleImports { get; set; }
    public List<SubtitleChoice>? Subtitles { get; set; }
    public List<AudioTrackChoice>? AudioTracks { get; set; }
    public List<FilterOverride>? FilterOverrides { get; set; }
    public GeometryOverride? Geometry { get; set; }
    public bool? ChapterMarkers { get; set; }
    public List<string>? ChapterNames { get; set; }
    public Dictionary<string, string>? MetadataOverrides { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public int? TitleIndex { get; set; }
    public string? PresetKey { get; set; }
    public string? PresetName { get; set; }
    public string? Container { get; set; }
    public string? VideoEncoder { get; set; }
    public string? VideoRateControl { get; set; }
    public double? VideoQuality { get; set; }
    public int? VideoBitrateKbps { get; set; }
    public string? VideoFramerate { get; set; }
    public string? VideoEncoderSpeed { get; set; }
    public string? DestinationPath { get; set; }

    [JsonIgnore]
    public bool HasVideoConfiguration => TitleIndex.HasValue &&
        !string.IsNullOrWhiteSpace(PresetKey) &&
        !string.IsNullOrWhiteSpace(VideoEncoder) &&
        !string.IsNullOrWhiteSpace(DestinationPath);

    [JsonIgnore]
    public string ConfigurationSummary => HasVideoConfiguration
        ? $"Title {TitleIndex} · {PresetName} · {VideoEncoder} · " +
          (VideoRateControl == "ConstantQuality"
              ? $"quality {VideoQuality:0.##}" : $"{VideoBitrateKbps} kbps") +
          $" · {Container} · {StateText}"
        : "Open the source in Workspace to configure this job.";

    [JsonIgnore]
    public string DestinationSummary => string.IsNullOrWhiteSpace(DestinationPath)
        ? "No destination configured"
        : "Destination: " + DestinationPath;
}

public sealed partial class MainWindow
{
    private readonly ObservableCollection<QueueDraft> draftQueue = new();

    private static string DraftQueueFile => Path.Combine(
        Path.GetDirectoryName(AppPreferences.StorageFile)!, "queue-drafts.json");

    private void InitializeDraftQueue()
    {
        DraftQueueList.ItemsSource = draftQueue;
        bool loadedQueue = false;
        try
        {
            if (File.Exists(DraftQueueFile))
            {
                var saved = JsonSerializer.Deserialize<QueueDraft[]>(File.ReadAllText(DraftQueueFile));
                if (saved is not null)
                {
                    loadedQueue = true;
                    foreach (var item in saved.Where(x => x is not null && !string.IsNullOrWhiteSpace(x.SourcePath)))
                    {
                        if (item.Id == Guid.Empty || draftQueue.Any(previous => previous.Id == item.Id)) item.Id = Guid.NewGuid();
                        if (item.State is "Scanning" or "Encoding" or "Paused")
                        {
                            item.State = "Interrupted";
                            item.FinishedAt = DateTimeOffset.Now;
                            item.LastError = "The previous session ended before this job completed. Reset it to retry.";
                        }
                        if (item.State is not ("Waiting" or "Completed" or "Failed" or "Canceled" or "Interrupted")) item.State = "Waiting";
                        draftQueue.Add(item);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            QueueStatusText.Text = "Unable to load saved queue: " + ex.Message;
            QueueStorageWarning.Message = QueueStatusText.Text;
            QueueStorageWarning.IsOpen = true;
        }
        InitializeQueueLogRefresh();
        if (loadedQueue) SaveDraftQueue();
        UpdateQueueUi();
        UpdateEncodeControls();
    }

    private QueueDraft CreateDraft(string path, bool allowVideoConfiguration)
    {
        var draft = new QueueDraft
        {
            SourcePath = path,
            DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
        };

        if (!allowVideoConfiguration || scanInProgress ||
            SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title } ||
            !TryGetVideoDraftSettings(out var settings))
            return draft;

        string? destination = GetSuggestedOutputPath();
        if (destinationOverride is not null && (string.IsNullOrWhiteSpace(destination) ||
            !Path.IsPathFullyQualified(destination) || !Directory.Exists(Path.GetDirectoryName(destination))))
            throw new InvalidOperationException("Choose an absolute output filename in an existing folder.");
        if (string.IsNullOrWhiteSpace(destination) ||
            !Directory.Exists(Path.GetDirectoryName(destination)))
            return draft;

        // A scanned folder can contain several titles. Preserve the actual
        // selected native title index instead of inventing a title number.
        draft.NativePresetJson = GetEditorPresetJson();
        draft.UsePresetAudio = UsePresetAudioToggle.IsChecked == true;
        draft.UsePresetSubtitles = UsePresetSubtitlesToggle.IsChecked == true;
        draft.UsePresetFilters = UsePresetFiltersToggle.IsChecked == true;
        draft.ForeignAudioSearch = !draft.UsePresetSubtitles && ForeignAudioSearchToggle.IsChecked == true;
        draft.ForeignAudioForced = ForeignAudioForcedToggle.IsChecked == true;
        draft.ForeignAudioBurn = draft.ForeignAudioSearch && ForeignAudioBurnToggle.IsChecked == true;
        draft.SubtitleImports = draft.UsePresetSubtitles ? new() : ReadImportedSubtitles();
        BuildImportedSubtitles(draft.SubtitleImports, CurrentMux);
        draft.Subtitles = draft.UsePresetSubtitles ? new() : ReadSubtitleChoices();
        if (draft.Subtitles.Count(track => track.Burn) + draft.SubtitleImports.Count(track => track.Burn) + (draft.ForeignAudioBurn ? 1 : 0) > 1)
            throw new InvalidOperationException("Select at most one subtitle track for burn-in.");
        BuildSubtitleTracks(draft.Subtitles, title, CurrentMux);
        draft.AudioTracks = draft.UsePresetAudio ? new() : ReadAudioChoices();
        draft.FilterOverrides = draft.UsePresetFilters ? new() : ReadFilterOverrides();
        // Validate selected native presets before persisting a configured entry.
        foreach (var filter in BuildFilterOverrides(draft.FilterOverrides))
            filter.Settings?.Dispose();
        draft.Geometry = ReadGeometryOverride(title);
        draft.ChapterMarkers = ChapterMarkersToggle.IsChecked == true;
        draft.ChapterNames = chapterNameEditors.Select(editor => editor.Text.Trim()).ToList();
        draft.MetadataOverrides = new Dictionary<string, string>();
        foreach (var field in new[] {
            ("Name", MetadataTitleBox.Text), ("Artist", MetadataArtistBox.Text),
            ("Album", MetadataAlbumBox.Text), ("Comment", MetadataCommentBox.Text) })
            if (!string.IsNullOrWhiteSpace(field.Item2))
                draft.MetadataOverrides[field.Item1] = field.Item2.Trim();
        draft.Selection = ReadSourceSelection(title);
        draft.TitleIndex = title.Index;
        draft.DisplayName += $" · Title {title.Index}";
        draft.PresetKey = settings.PresetKey;
        draft.PresetName = settings.PresetName;
        draft.Container = settings.Container;
        draft.VideoEncoder = settings.VideoEncoder;
        draft.VideoRateControl = settings.RateControl;
        draft.VideoQuality = settings.Quality;
        draft.VideoBitrateKbps = settings.BitrateKbps;
        draft.VideoFramerate = settings.Framerate;
        draft.VideoEncoderSpeed = settings.EncoderSpeed;
        draft.DestinationPath = destination;
        return draft;
    }

    private void StageQueue_Click(object sender, RoutedEventArgs e)
    {
        if (encodeRunning || preparingEncodeQueue) return;
        int added = 0, updated = 0, configured = 0;
        bool singleSource = selectedSourcePaths.Count == 1;

        foreach (string path in selectedSourcePaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            QueueDraft draft;
            try { draft = CreateDraft(path, singleSource); }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                QueueStatusText.Text = error.Message;
                return;
            }
            if (draft.HasVideoConfiguration) configured++;

            // A source can have several scanned titles; keep distinct title
            // configurations, but upgrade an older source-only draft in place.
            int existing = -1;
            for (int i = 0; i < draftQueue.Count; i++)
            {
                QueueDraft previous = draftQueue[i];
                if (!string.Equals(previous.SourcePath, path, StringComparison.OrdinalIgnoreCase))
                    continue;
                if ((previous.State == "Waiting" && previous.TitleIndex == draft.TitleIndex &&
                    string.Equals(previous.DestinationPath, draft.DestinationPath, StringComparison.OrdinalIgnoreCase)) ||
                    (previous.TitleIndex is null && draft.TitleIndex.HasValue))
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
            {
                draft.Id = draftQueue[existing].Id;
                draftQueue[existing] = draft;
                updated++;
            }
            else
            {
                draftQueue.Add(draft);
                added++;
            }
        }

        QueueStatusText.Text = added + updated > 0
            ? $"Added {added} job(s), updated {updated}; {configured} ready."
            : "No accessible sources to stage.";
        if (added + updated > 0 && configured == 0)
            QueueStatusText.Text += " Open a source in Workspace, scan it and choose compatible output settings.";
        SaveDraftQueue();
        UpdateQueueUi();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[1];
    }

    private void DraftQueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateQueueUi();

    private void RemoveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (encodeRunning || preparingEncodeQueue) return;
        if (DraftQueueList.SelectedItem is not QueueDraft draft) return;
        draftQueue.Remove(draft);
        QueueStatusText.Text = "Job removed.";
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private async void ClearDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (encodeRunning || preparingEncodeQueue || draftQueue.Count == 0) return;
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "Clear the queue?",
            Content = "This removes the WinUI draft queue only. No source files will be deleted.",
            PrimaryButtonText = "Clear queue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (encodeRunning || preparingEncodeQueue) return;
        draftQueue.Clear();
        QueueStatusText.Text = "Queue cleared.";
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void UpdateQueueUi()
    {
        int waiting = draftQueue.Count(job => job.IsPending);
        int completed = draftQueue.Count(job => job.State == "Completed");
        int failed = draftQueue.Count(job => job.State is "Failed" or "Interrupted");
        int setup = draftQueue.Count(job => !job.HasVideoConfiguration);
        QueueCountText.Text = $"{draftQueue.Count} job(s) · {waiting} waiting · {completed} completed · {failed} need attention" +
            (setup > 0 ? $" · {setup} need setup" : "") +
            (encodeRunning ? encodePaused ? " · paused" : " · running" : "");
        if (DraftQueueList.SelectedItem is null && draftQueue.Count > 0) DraftQueueList.SelectedIndex = 0;
        UpdateEncodeControls();
        RefreshQueueDetails();
    }

    private void SaveDraftQueue()
    {
        try
        {
            string folder = Path.GetDirectoryName(DraftQueueFile)!;
            Directory.CreateDirectory(folder);
            string temp = DraftQueueFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(draftQueue.ToArray(),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, DraftQueueFile, overwrite: true);
            QueueStorageWarning.IsOpen = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            QueueStatusText.Text = "Unable to save queue: " + ex.Message;
            QueueStorageWarning.Message = QueueStatusText.Text;
            QueueStorageWarning.IsOpen = true;
        }
    }
}

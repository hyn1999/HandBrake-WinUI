using System;
using System.Collections.Generic;
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
public sealed class QueueDraft
{
    public List<SubtitleChoice>? Subtitles { get; set; }
    public List<AudioTrackChoice>? AudioTracks { get; set; }
    public List<FilterOverride>? FilterOverrides { get; set; }
    public GeometryOverride? Geometry { get; set; }
    public bool? ChapterMarkers { get; set; }
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
          $" · {Container} · ready to encode"
        : "Source-only draft · scan a title and select a native preset to configure";

    [JsonIgnore]
    public string DestinationSummary => string.IsNullOrWhiteSpace(DestinationPath)
        ? "No destination configured"
        : "Suggested destination: " + DestinationPath;
}

public sealed partial class MainWindow
{
    private readonly ObservableCollection<QueueDraft> draftQueue = new();

    private static string DraftQueueFile => Path.Combine(
        Path.GetDirectoryName(AppPreferences.StorageFile)!, "queue-drafts.json");

    private void InitializeDraftQueue()
    {
        DraftQueueList.ItemsSource = draftQueue;
        try
        {
            if (File.Exists(DraftQueueFile))
            {
                var saved = JsonSerializer.Deserialize<QueueDraft[]>(File.ReadAllText(DraftQueueFile));
                if (saved is not null)
                {
                    foreach (var item in saved.Where(x => !string.IsNullOrWhiteSpace(x.SourcePath)))
                        draftQueue.Add(item);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            QueueStatusText.Text = "Unable to load saved queue drafts: " + ex.Message;
        }
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
        if (string.IsNullOrWhiteSpace(destination) ||
            !Directory.Exists(Path.GetDirectoryName(destination)))
            return draft;

        // A scanned folder can contain several titles. Preserve the actual
        // selected native title index instead of inventing a title number.
        draft.Subtitles = ReadSubtitleChoices();
        BuildSubtitleTracks(draft.Subtitles, title, activePreset?.FileFormat ?? "");
        draft.AudioTracks = ReadAudioChoices();
        draft.FilterOverrides = ReadFilterOverrides();
        // Validate selected native presets before persisting a configured entry.
        foreach (var filter in BuildFilterOverrides(draft.FilterOverrides))
            filter.Settings?.Dispose();
        draft.Geometry = ReadGeometryOverride(title);
        draft.ChapterMarkers = ChapterMarkersToggle.IsChecked == true;
        draft.MetadataOverrides = new Dictionary<string, string>();
        foreach (var field in new[] {
            ("Name", MetadataTitleBox.Text), ("Artist", MetadataArtistBox.Text),
            ("Album", MetadataAlbumBox.Text), ("Comment", MetadataCommentBox.Text) })
            if (!string.IsNullOrWhiteSpace(field.Item2))
                draft.MetadataOverrides[field.Item1] = field.Item2.Trim();
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
        int added = 0, updated = 0, configured = 0;
        bool singleSource = selectedSourcePaths.Count == 1;

        foreach (string path in selectedSourcePaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            QueueDraft draft;
            try { draft = CreateDraft(path, singleSource); }
            catch (InvalidOperationException error)
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
                if (previous.TitleIndex == draft.TitleIndex ||
                    (previous.TitleIndex is null && draft.TitleIndex.HasValue))
                {
                    existing = i;
                    break;
                }
            }

            if (existing >= 0)
            {
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
            ? $"Staged {added} new, updated {updated} source(s); {configured} with native preset/video settings. No encoding has started."
            : "No accessible sources to stage.";
        if (added + updated > 0 && configured == 0)
            QueueStatusText.Text += " To configure a draft, scan one source, select a title, and choose a compatible native preset/encoder.";
        SaveDraftQueue();
        UpdateQueueUi();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[1];
    }

    private void DraftQueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateQueueUi();

    private void RemoveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (DraftQueueList.SelectedItem is not QueueDraft draft) return;
        draftQueue.Remove(draft);
        QueueStatusText.Text = "Draft removed.";
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private async void ClearDrafts_Click(object sender, RoutedEventArgs e)
    {
        if (draftQueue.Count == 0) return;
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "Clear staged sources?",
            Content = "This removes the WinUI draft queue only. No source files will be deleted.",
            PrimaryButtonText = "Clear drafts",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        draftQueue.Clear();
        QueueStatusText.Text = "Draft queue cleared.";
        SaveDraftQueue();
        UpdateQueueUi();
    }

    private void UpdateQueueUi()
    {
        int configured = draftQueue.Count(x => x.HasVideoConfiguration);
        QueueCountText.Text = draftQueue.Count == 0
            ? "No staged sources"
            : $"{draftQueue.Count} draft(s) · {configured} configured · none running";
        RemoveDraftButton.IsEnabled = DraftQueueList.SelectedItem is QueueDraft;
        ClearDraftsButton.IsEnabled = draftQueue.Count > 0;
        UpdateEncodeControls();
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            QueueStatusText.Text = "Unable to save queue drafts: " + ex.Message;
        }
    }
}


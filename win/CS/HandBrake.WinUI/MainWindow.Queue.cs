using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed class QueueDraft
{
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}

/// <summary>
/// A local source staging list only. Encoding jobs are not constructed here;
/// the real queue will be connected to HandBrake's job and preset models.
/// </summary>
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
                    {
                        draftQueue.Add(item);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            QueueStatusText.Text = "Unable to load saved queue drafts: " + ex.Message;
        }
        UpdateQueueUi();
    }

    private void StageQueue_Click(object sender, RoutedEventArgs e)
    {
        int added = 0;
        foreach (string path in selectedSourcePaths)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;
            if (draftQueue.Any(item => string.Equals(item.SourcePath, path,
                                                       StringComparison.OrdinalIgnoreCase)))
                continue;

            draftQueue.Add(new QueueDraft
            {
                SourcePath = path,
                DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path))
            });
            added++;
        }

        QueueStatusText.Text = added > 0
            ? $"Staged {added} source(s). Encoding is not available yet."
            : "These sources are already staged, or no longer exist.";
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
        QueueCountText.Text = draftQueue.Count == 0
            ? "No staged sources"
            : $"{draftQueue.Count} staged source(s) · not yet encoding";
        RemoveDraftButton.IsEnabled = DraftQueueList.SelectedItem is QueueDraft;
        ClearDraftsButton.IsEnabled = draftQueue.Count > 0;
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

using System;
using System.Collections.Generic;
using System.Linq;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed record AudioTrackChoice(int TrackNumber, string Name);

public sealed partial class MainWindow
{
    private sealed record AudioEditor(int TrackNumber, CheckBox Selected, TextBox Name);
    private readonly List<AudioEditor> audioEditors = new();

    private void DisplayAudioEditor(SourceTitle title)
    {
        audioEditors.Clear();
        AudioTrackList.Children.Clear();
        foreach (var track in title.AudioList ?? new List<SourceAudioTrack>())
        {
            var selected = new CheckBox
            {
                IsChecked = true,
                Content = $"{track.TrackNumber}. {track.Language ?? "Unknown language"} · {track.CodecName ?? track.Description ?? "Unknown codec"} · {track.ChannelCount} channels"
            };
            var name = new TextBox { Header = "Output track name", Text = track.Name ?? string.Empty };
            selected.Checked += (_, _) => name.IsEnabled = true;
            selected.Unchecked += (_, _) => name.IsEnabled = false;
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(selected);
            row.Children.Add(name);
            AudioTrackList.Children.Add(new Border
            {
                Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
                Child = row
            });
            audioEditors.Add(new AudioEditor(track.TrackNumber, selected, name));
        }
        if (audioEditors.Count == 0) AddDetailLine(AudioTrackList, "No audio tracks detected. Output will have no audio.");
    }

    private List<AudioTrackChoice> ReadAudioChoices()
        => audioEditors.Where(editor => editor.Selected.IsChecked == true)
            .Select(editor => new AudioTrackChoice(editor.TrackNumber, editor.Name.Text.Trim())).ToList();

    private static List<SourceAudioTrack> SelectAudioTracks(QueueDraft draft, SourceTitle title)
    {
        var tracks = title.AudioList ?? new List<SourceAudioTrack>();
        // Null is the legacy schema; an explicit empty list means intentionally silent output.
        if (draft.AudioTracks is null) return tracks;
        var selected = new List<SourceAudioTrack>();
        foreach (var choice in draft.AudioTracks)
        {
            var track = tracks.FirstOrDefault(track => track.TrackNumber == choice.TrackNumber);
            if (track is null || selected.Any(previous => previous.TrackNumber == choice.TrackNumber))
                throw new InvalidOperationException("A selected audio track is missing or duplicated. Re-scan and stage this job again.");
            selected.Add(track);
        }
        return selected;
    }
}

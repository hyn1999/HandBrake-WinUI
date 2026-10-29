using System;
using System.Collections.Generic;
using System.Linq;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.Interfaces.Model.Encoders;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed record AudioTrackChoice(int TrackNumber, string Name)
{
    public string? Encoder { get; init; }
    public int? BitrateKbps { get; init; }
}

public sealed partial class MainWindow
{
    private sealed record AudioEditor(int TrackNumber, CheckBox Selected, TextBox Name, ComboBox Encoder, NumberBox Bitrate);
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
            var encoder = new ComboBox { Header = "Output encoder", HorizontalAlignment = HorizontalAlignment.Stretch };
            encoder.Items.Add(new ComboBoxItem { Content = "Use selected preset audio rule" });
            foreach (var codec in HandBrakeEncoderHelpers.AudioEncoders.Where(codec => !codec.IsPassthru && !codec.IsLosslessEncoder && codec.ShortName != "none"))
                encoder.Items.Add(new ComboBoxItem { Content = codec.DisplayName, Tag = codec.ShortName });
            encoder.SelectedIndex = 0;
            var bitrate = new NumberBox { Header = "Bitrate (kbps)", Minimum = 6, Maximum = 1536, Value = 160, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            encoder.SelectionChanged += (_, _) => bitrate.IsEnabled = selected.IsChecked == true && encoder.SelectedIndex > 0;
            selected.Checked += (_, _) => { encoder.IsEnabled = true; bitrate.IsEnabled = encoder.SelectedIndex > 0; };
            selected.Unchecked += (_, _) => { encoder.IsEnabled = false; bitrate.IsEnabled = false; };
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(selected);
            row.Children.Add(name);
            row.Children.Add(encoder);
            row.Children.Add(bitrate);
            AudioTrackList.Children.Add(new Border
            {
                Padding = new Thickness(16), CornerRadius = new CornerRadius(8),
                Child = row
            });
            audioEditors.Add(new AudioEditor(track.TrackNumber, selected, name, encoder, bitrate));
        }
        if (audioEditors.Count == 0) AddDetailLine(AudioTrackList, "No audio tracks detected. Output will have no audio.");
    }

    private List<AudioTrackChoice> ReadAudioChoices()
    {
        var choices = new List<AudioTrackChoice>();
        foreach (var editor in audioEditors.Where(editor => editor.Selected.IsChecked == true))
        {
            string? encoder = (editor.Encoder.SelectedItem as ComboBoxItem)?.Tag as string;
            double bitrate = editor.Bitrate.Value;
            if (encoder is not null && (!double.IsFinite(bitrate) || bitrate != Math.Truncate(bitrate) || bitrate < 6 || bitrate > 1536))
                throw new InvalidOperationException("Audio bitrate must be a whole number from 6 to 1536 kbps.");
            choices.Add(new AudioTrackChoice(editor.TrackNumber, editor.Name.Text.Trim())
            {
                Encoder = encoder, BitrateKbps = encoder is null ? null : (int)bitrate
            });
        }
        return choices;
    }

    private static bool SupportsAudioContainer(HBAudioEncoder encoder, string container)
        => container switch { "MP4" => encoder.SupportsMP4, "MKV" => encoder.SupportsMkv, "WebM" => encoder.SupportsWebM, _ => false };

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

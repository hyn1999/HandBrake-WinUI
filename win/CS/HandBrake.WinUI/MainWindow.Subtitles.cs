using System;
using System.Collections.Generic;
using System.Linq;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OutputSubtitle = HandBrake.Interop.Interop.Json.Encode.SubtitleTrack;

namespace HandBrake.WinUI;

public sealed record SubtitleChoice(int TrackNumber, bool Default, bool Forced, int Offset, string Name);

public sealed partial class MainWindow
{
    private sealed record SubtitleEditor(int TrackNumber, CheckBox Selected, CheckBox Default,
        CheckBox Forced, NumberBox Offset, TextBox Name);
    private readonly List<SubtitleEditor> subtitleEditors = new();

    private void DisplaySubtitleEditor(SourceTitle title)
    {
        subtitleEditors.Clear();
        SubtitleTrackList.Children.Clear();
        foreach (var track in title.SubtitleList ?? new List<SourceSubtitleTrack>())
        {
            var selected = new CheckBox { Content = $"{track.TrackNumber}. {track.Language ?? "Unknown language"} · {track.SourceName ?? track.Format}" };
            var defaultTrack = new CheckBox { Content = "Default", IsEnabled = false };
            var forced = new CheckBox { Content = "Forced only", IsEnabled = false };
            bool canForce = HandBrakeSubtitleHelpers.CheckCanForceSubtitle(track.Source) != 0;
            var offset = new NumberBox { Header = "Offset (milliseconds)", Value = 0, Minimum = -3600000, Maximum = 3600000, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var name = new TextBox { Header = "Output track name", Text = track.Name ?? string.Empty, IsEnabled = false };
            selected.Checked += (_, _) => { defaultTrack.IsEnabled = true; forced.IsEnabled = canForce; offset.IsEnabled = true; name.IsEnabled = true; };
            selected.Unchecked += (_, _) => { defaultTrack.IsEnabled = forced.IsEnabled = offset.IsEnabled = name.IsEnabled = false; };
            defaultTrack.Checked += (_, _) =>
            {
                foreach (var other in subtitleEditors.Where(other => other.Default != defaultTrack)) other.Default.IsChecked = false;
            };
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(selected); row.Children.Add(defaultTrack); row.Children.Add(forced);
            row.Children.Add(offset); row.Children.Add(name);
            SubtitleTrackList.Children.Add(new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), Child = row });
            subtitleEditors.Add(new SubtitleEditor(track.TrackNumber, selected, defaultTrack, forced, offset, name));
        }
        if (subtitleEditors.Count == 0) AddDetailLine(SubtitleTrackList, "No internal subtitle tracks detected.");
    }

    private List<SubtitleChoice> ReadSubtitleChoices()
    {
        var choices = new List<SubtitleChoice>();
        foreach (var editor in subtitleEditors.Where(editor => editor.Selected.IsChecked == true))
        {
            double offset = editor.Offset.Value;
            if (!double.IsFinite(offset) || offset != Math.Truncate(offset) || Math.Abs(offset) > 3600000)
                throw new InvalidOperationException("Subtitle offset must be a whole number within one hour in milliseconds.");
            choices.Add(new SubtitleChoice(editor.TrackNumber, editor.Default.IsChecked == true,
                editor.Forced.IsChecked == true, (int)offset, editor.Name.Text.Trim()));
        }
        return choices;
    }

    private static List<OutputSubtitle> BuildSubtitleTracks(IEnumerable<SubtitleChoice>? choices, SourceTitle title, string mux)
    {
        var selected = choices?.ToList() ?? new List<SubtitleChoice>();
        if (selected.Count(choice => choice.Default) > 1 || selected.Select(choice => choice.TrackNumber).Distinct().Count() != selected.Count)
            throw new InvalidOperationException("Subtitle selection has duplicate tracks or multiple defaults. Re-stage this job.");
        var result = new List<OutputSubtitle>();
        var container = HandBrakeEncoderHelpers.GetContainer(mux);
        foreach (var choice in selected)
        {
            var track = title.SubtitleList?.FirstOrDefault(track => track.TrackNumber == choice.TrackNumber);
            if (track is null || container is null || HBFunctions.hb_subtitle_can_pass(track.Source, container.Id) == 0)
                throw new InvalidOperationException("A selected subtitle is missing or cannot be stored in this container. Choose another container or remove the track; burn-in is not implemented yet.");
            if (choice.Forced && HandBrakeSubtitleHelpers.CheckCanForceSubtitle(track.Source) == 0)
                throw new InvalidOperationException("Forced-only filtering is unsupported for this subtitle source.");
            if (Math.Abs((long)choice.Offset) > 3600000)
                throw new InvalidOperationException("Saved subtitle offset is outside the supported range.");
            result.Add(new OutputSubtitle { Track = track.TrackNumber - 1, Default = choice.Default,
                Forced = choice.Forced, Burn = false, Offset = choice.Offset, Name = choice.Name });
        }
        return result;
    }
}

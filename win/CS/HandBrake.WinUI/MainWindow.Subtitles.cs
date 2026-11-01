using System;
using System.Collections.Generic;
using System.Linq;
using HandBrake.Interop.Interop;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OutputSubtitle = HandBrake.Interop.Interop.Json.Encode.SubtitleTrack;

namespace HandBrake.WinUI;

public sealed record SubtitleChoice(int TrackNumber, bool Default, bool Forced, int Offset, string Name)
{
    public bool Burn { get; init; }
}

public sealed partial class MainWindow
{
    private sealed record SubtitleEditor(int TrackNumber, CheckBox Selected, CheckBox Default,
        CheckBox Forced, CheckBox Burn, NumberBox Offset, TextBox Name);
    private readonly List<SubtitleEditor> subtitleEditors = new();

    private void DisplaySubtitleEditor(SourceTitle title)
    {
        subtitleEditors.Clear();
        SubtitleTrackList.Children.Clear();
        foreach (var track in title.SubtitleList ?? new List<SourceSubtitleTrack>())
        {
            var selected = new CheckBox { Content = new TextBlock { Text = $"{track.TrackNumber}. {track.Language ?? "und"} · {track.SourceName ?? track.Format}", TextWrapping = TextWrapping.Wrap } };
            var defaultTrack = new CheckBox { Content = WpfText.Label("SubtitlesView_Default"), IsEnabled = false };
            var forced = new CheckBox { Content = WpfText.Label("SubtitlesView_ForcedOnly"), IsEnabled = false };
            var burn = new CheckBox { Content = WpfText.Label("SubtitlesView_BurnIn"), IsEnabled = false };
            bool canBurn = HandBrakeSubtitleHelpers.CheckCanBurnSubtitle(track.Source) != 0;
            bool canForce = HandBrakeSubtitleHelpers.CheckCanForceSubtitle(track.Source) != 0;
            var offset = new NumberBox { Value = 0, Minimum = -3600000, Maximum = 3600000, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var name = new TextBox { Text = track.Name ?? string.Empty, IsEnabled = false };
            selected.Checked += (_, _) => { defaultTrack.IsEnabled = burn.IsChecked != true; burn.IsEnabled = canBurn; forced.IsEnabled = canForce; offset.IsEnabled = true; name.IsEnabled = true; };
            selected.Unchecked += (_, _) => { defaultTrack.IsEnabled = forced.IsEnabled = burn.IsEnabled = offset.IsEnabled = name.IsEnabled = false; };
            defaultTrack.Checked += (_, _) =>
            {
                SelectSubtitleDefaultTarget(defaultTrack);
            };
            burn.Checked += (_, _) => { SelectSubtitleBurnTarget(burn); defaultTrack.IsChecked = false; defaultTrack.IsEnabled = false; };
            burn.Unchecked += (_, _) => defaultTrack.IsEnabled = selected.IsChecked == true;
            ToolTipService.SetToolTip(burn, CompactUi.Help(WpfText.Tooltip("SubtitlesView_BurnIn")));
            var row = new StackPanel { Spacing = CompactUi.RowGap };
            row.Children.Add(selected);
            row.Children.Add(CompactUi.AlignedFormRow(
                new CompactFormField("", forced, FieldWidthRole.ShortChoice),
                new CompactFormField("", burn, FieldWidthRole.ShortChoice),
                new CompactFormField("", defaultTrack, FieldWidthRole.ShortChoice)));
            row.Children.Add(CompactUi.AlignedFormRow(
                new CompactFormField("Offset (ms)", offset, FieldWidthRole.Number),
                new CompactFormField(WpfText.Label("AudioView_TrackName"), name, FieldWidthRole.NamedChoice)));
            SubtitleTrackList.Children.Add(new Border { Style = CompactUi.Resource<Style>("HBCompactTrackRowStyle"), Child = row });
            foreach (var toggle in new[] { selected, forced, burn, defaultTrack })
            {
                toggle.Checked += (_, _) => RefreshTrackPresentation();
                toggle.Unchecked += (_, _) => RefreshTrackPresentation();
            }
            offset.ValueChanged += (_, _) => RefreshTrackPresentation();
            name.TextChanged += (_, _) => RefreshTrackPresentation();
            subtitleEditors.Add(new SubtitleEditor(track.TrackNumber, selected, defaultTrack, forced, burn, offset, name));
        }
        if (subtitleEditors.Count == 0) AddDetailLine(SubtitleTrackList, WpfText.Label("SummaryView_NoSubtitleTracks"));
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
                editor.Forced.IsChecked == true, (int)offset, editor.Name.Text.Trim()) { Burn = editor.Burn.IsChecked == true });
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
            if (track is null || container is null || (!choice.Burn && HandBrakeSubtitleHelpers.CheckCanPassSubtitle(track.Source, container.Id) == 0))
                throw new InvalidOperationException("A selected subtitle is missing or cannot be stored in this container. Choose another container or a supported burn-in track.");
            if (choice.Burn && (choice.Default || HandBrakeSubtitleHelpers.CheckCanBurnSubtitle(track.Source) == 0))
                throw new InvalidOperationException("This subtitle cannot be burned or marked as default while burning.");
            if (choice.Forced && HandBrakeSubtitleHelpers.CheckCanForceSubtitle(track.Source) == 0)
                throw new InvalidOperationException("Forced-only filtering is unsupported for this subtitle source.");
            if (Math.Abs((long)choice.Offset) > 3600000)
                throw new InvalidOperationException("Saved subtitle offset is outside the supported range.");
            result.Add(new OutputSubtitle { ID = track.TrackNumber, Track = track.TrackNumber - 1, Default = choice.Default,
                Forced = choice.Forced, Burn = choice.Burn, Offset = choice.Offset, Name = choice.Name });
        }
        return result;
    }
}


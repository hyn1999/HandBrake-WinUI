using System;
using System.Linq;
using HandBrake.Interop.Interop.Json.Scan;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed record EncodeSelection(int Angle, string Type, double Start, double End);

public sealed partial class MainWindow
{
    private readonly ComboBox anglePicker = new() { Header = "Angle" };
    private readonly ComboBox sourceRangePicker = new() { Header = "Range" };
    private readonly NumberBox rangeStartBox = new() { Header = "Start", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly NumberBox rangeEndBox = new() { Header = "End", SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly TextBlock rangeDurationText = new() { Text = "—", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private bool updatingSourceSelection;
    private Grid? rangeBoundsPanel;

    private void InitializeSourceRangeEditor()
    {
        updatingSourceSelection = true;
        PopulateChoices(sourceRangePicker, new[] { ("Full title", "all"), ("Chapters", "chapter"), ("Seconds", "time"), ("Frames", "frame") }, "all");
        rangeBoundsPanel = new Grid { ColumnSpacing = 12 };
        rangeBoundsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rangeBoundsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rangeStartBox.HorizontalAlignment = rangeEndBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        rangeStartBox.MaxWidth = rangeEndBox.MaxWidth = double.PositiveInfinity;
        Grid.SetColumn(rangeEndBox, 1);
        rangeBoundsPanel.Children.Add(rangeStartBox); rangeBoundsPanel.Children.Add(rangeEndBox);
        rangeBoundsPanel.Visibility = Visibility.Collapsed;
        InitializeSourceHeaderHelp();
        sourceRangePicker.SelectionChanged += (_, _) =>
        {
            if (!updatingSourceSelection && SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle title })
                ResetRangeBounds(title);
        };
        anglePicker.SelectionChanged += (_, _) => { if (!updatingSourceSelection) ScheduleResolvedSummary(); };
        rangeStartBox.ValueChanged += (_, _) => UpdateRangeDuration();
        rangeEndBox.ValueChanged += (_, _) => UpdateRangeDuration();
        SourceRangeGate.IsEnabled = false;
        updatingSourceSelection = false;
    }

    private static double SourceSeconds(SourceTitle title)
        => title.Duration?.Ticks > 0 ? title.Duration.Ticks / 90000d :
            (title.Duration?.Hours ?? 0) * 3600d + (title.Duration?.Minutes ?? 0) * 60d + (title.Duration?.Seconds ?? 0);

    private void LoadSourceRange(SourceTitle title)
    {
        updatingSourceSelection = true;
        try
        {
            PopulateChoices(anglePicker, Enumerable.Range(1, Math.Max(1, title.AngleCount)).Select(angle => (angle.ToString(), angle.ToString())), "1");
            anglePicker.IsEnabled = title.AngleCount > 1;
            sourceRangePicker.SelectedIndex = 0;
            ((ComboBoxItem)sourceRangePicker.Items[1]).IsEnabled = title.ChapterList?.Count > 0;
            SourceRangeGate.IsEnabled = true;
            SourceRangePanel.Visibility = Visibility.Visible;
        }
        finally { updatingSourceSelection = false; }
        ResetRangeBounds(title);
    }

    private void ResetRangeBounds(SourceTitle title)
    {
        updatingSourceSelection = true;
        try
        {
            string type = ChoiceValue(sourceRangePicker);
            bool full = type == "all", time = type == "time";
            if (rangeBoundsPanel is not null) rangeBoundsPanel.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            rangeStartBox.Header = time ? "Start (seconds)" : "Start";
            rangeEndBox.Header = time ? "End (seconds)" : "End";
            rangeStartBox.Minimum = rangeEndBox.Minimum = time ? 0 : 1;
            rangeStartBox.Maximum = rangeEndBox.Maximum = time ? Math.Max(1, SourceSeconds(title)) : int.MaxValue;
            rangeStartBox.SmallChange = rangeEndBox.SmallChange = time ? 0.1 : 1;
            rangeStartBox.Value = time ? 0 : 1;
            double fps = title.FrameRate is { Den: > 0 } rate ? (double)rate.Num / rate.Den : 0;
            rangeEndBox.Value = type == "chapter" ? Math.Max(1, title.ChapterList?.Count ?? 0)
                : time ? SourceSeconds(title) : Math.Max(1, Math.Floor(SourceSeconds(title) * fps));
        }
        finally { updatingSourceSelection = false; }
        LayoutSourceHeader();
        UpdateRangeDuration();
    }

    private EncodeSelection ReadSourceSelection(SourceTitle title)
    {
        if (!int.TryParse(ChoiceValue(anglePicker), out int angle)) angle = 1;
        string type = ChoiceValue(sourceRangePicker);
        var selection = new EncodeSelection(angle, type, type == "all" ? 0 : rangeStartBox.Value, type == "all" ? 0 : rangeEndBox.Value);
        ValidateSelection(selection, title);
        return selection;
    }

    private static void ValidateSelection(EncodeSelection value, SourceTitle title)
    {
        if (value.Angle < 1 || value.Angle > Math.Max(1, title.AngleCount))
            throw new InvalidOperationException("The selected angle is unavailable. Re-scan and stage this title again.");
        if (value.Type == "all") return;
        if (!double.IsFinite(value.Start) || !double.IsFinite(value.End))
            throw new InvalidOperationException("Enter valid start and end values.");
        if (value.Type == "time")
        {
            if (value.Start < 0 || value.End <= value.Start || value.End > SourceSeconds(title))
                throw new InvalidOperationException("The end time must be after the start and within the source duration.");
        }
        else if (value.Type is "chapter" or "frame")
        {
            double maximum = value.Type == "chapter" ? title.ChapterList?.Count ?? 0 : int.MaxValue;
            if (value.Start != Math.Truncate(value.Start) || value.End != Math.Truncate(value.End) ||
                value.Start < 1 || value.End < value.Start || value.End > maximum)
                throw new InvalidOperationException("Use a valid inclusive range of whole chapter or frame numbers.");
        }
        else throw new InvalidOperationException("The saved range type is unsupported.");
    }

    private void UpdateRangeDuration()
    {
        if (updatingSourceSelection || SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title }) return;
        ScheduleResolvedSummary();
        try
        {
            var selection = ReadSourceSelection(title);
            double seconds = SourceSeconds(title);
            string prefix = "";
            if (selection.Type == "time") seconds = selection.End - selection.Start;
            if (selection.Type == "chapter")
                seconds = title.ChapterList.Skip((int)selection.Start - 1).Take((int)(selection.End - selection.Start + 1))
                    .Sum(chapter => chapter.Duration?.Ticks / 90000d ?? 0);
            if (selection.Type == "frame")
            {
                double fps = title.FrameRate is { Den: > 0 } rate ? (double)rate.Num / rate.Den : 0;
                if (fps <= 0) { rangeDurationText.Text = "Frame duration is unavailable."; return; }
                seconds = (selection.End - selection.Start + 1) / fps;
                prefix = "≈ ";
            }
            rangeDurationText.Text = prefix + TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
        }
        catch (InvalidOperationException error) { rangeDurationText.Text = error.Message; }
    }
}

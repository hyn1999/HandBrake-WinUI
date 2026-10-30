using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    // Compact layout is scoped to the editor. It retains native templates and focus states.
    private static Grid CompactRow(params FrameworkElement[] fields)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8, Margin = new Thickness(0, 4, 0, 8) };
        foreach (var field in fields)
        {
            field.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(field);
        }
        int previousColumns = -1;
        grid.SizeChanged += (_, args) =>
        {
            int columns = args.NewSize.Width >= 900 ? fields.Length : args.NewSize.Width >= 560 ? Math.Min(2, fields.Length) : 1;
            if (columns == previousColumns) return;
            previousColumns = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int row = 0; row < (fields.Length + columns - 1) / columns; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < fields.Length; i++) { Grid.SetColumn(fields[i], i % columns); Grid.SetRow(fields[i], i / columns); }
        };
        return grid;
    }

    private async void BrowseDestination_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            string filename = System.IO.Path.GetFileName(GetSuggestedOutputPath() ?? GetAutomaticOutputPath() ?? "output.mp4");
            DestinationText.Text = System.IO.Path.Combine(folder.Path, filename);
        }
        catch (Exception error) { SourceStatusText.Text = "Unable to choose output folder: " + error.Message; }
    }

    private void PresetRules_Toggled(object sender, RoutedEventArgs args)
    {
        if (AudioTrackList is not null && UsePresetAudioToggle is not null)
            AudioTrackList.IsEnabled = UsePresetAudioToggle.IsChecked != true;
        if (SubtitleManualPanel is not null && UsePresetSubtitlesToggle is not null)
            SubtitleManualPanel.IsEnabled = UsePresetSubtitlesToggle.IsChecked != true;
        if (FilterEditorPanel is not null && UsePresetFiltersToggle is not null)
            FilterEditorPanel.IsEnabled = UsePresetFiltersToggle.IsChecked != true;
        RefreshOutputSummary();
    }

    private void RefreshOutputSummary()
    {
        UpdatePresetPresentation();
        if (ConfiguredOutputText is null || VideoRateModePicker is null || !TryGetVideoDraftSettings(out var settings)) return;
        string rate = settings.RateControl == "ConstantQuality" ? $"Quality {settings.Quality:0.##}" : $"{settings.BitrateKbps} kbps";
        int audio = audioEditors.Count(editor => editor.Selected.IsChecked == true);
        string geometry = CustomGeometryToggle.IsChecked == true
            ? $"{OutputWidthBox.Value:0} × {OutputHeightBox.Value:0} (manual dimensions)" : "Dimensions from preset";
        string audioSummary = UsePresetAudioToggle?.IsChecked == true ? "Audio: native preset rules" : $"{audio} selected audio track(s)";
        ConfiguredOutputText.Text = $"{settings.Container} · {settings.VideoEncoder} · {rate}\n" +
            $"{audioSummary} · {geometry}\n" +
            $"Chapter markers: {(ChapterMarkersToggle.IsChecked == true ? "On" : "Off")}";
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs args) => UpdatePreviewFrameBounds();

    private void UpdatePreviewFrameBounds()
    {
        if (PreviewFrame?.Source is not Microsoft.UI.Xaml.Media.Imaging.BitmapSource bitmap ||
            bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 || PreviewViewport is null) return;
        double scale = Math.Min(PreviewViewport.ActualWidth / bitmap.PixelWidth,
                                PreviewViewport.ActualHeight / bitmap.PixelHeight);
        if (!double.IsFinite(scale) || scale < 0) return;
        // Fit the complete decoded display frame in both dimensions.
        // A zero-height viewport produces a zero-sized image, never overflow.
        PreviewFrame.Width = bitmap.PixelWidth * scale;
        PreviewFrame.Height = bitmap.PixelHeight * scale;
    }

    private bool previewHidden;
    private readonly List<TextBox> chapterNameEditors = new();
    private void GeometryOverride_Toggled(object sender, RoutedEventArgs args)
    {
        foreach (var box in new[] { OutputWidthBox, OutputHeightBox, CropTopBox, CropBottomBox, CropLeftBox, CropRightBox })
            if (box is not null) box.IsEnabled = CustomGeometryToggle.IsChecked == true;
    }
    private void ForeignSearch_Toggled(object sender, RoutedEventArgs args)
    {
        if (ForeignAudioForcedToggle is not null) ForeignAudioForcedToggle.IsEnabled = ForeignAudioSearchToggle.IsChecked == true;
        if (ForeignAudioBurnToggle is not null) ForeignAudioBurnToggle.IsEnabled = ForeignAudioSearchToggle.IsChecked == true;
    }
    private void DisplayChapterEditor(HandBrake.Interop.Interop.Json.Scan.SourceTitle title)
    {
        ChapterEditorPanel.Children.Clear();
        chapterNameEditors.Clear();
        foreach (var chapter in title.ChapterList ?? new())
        {
            var name = new TextBox { Text = chapter.Name ?? string.Empty, Header = $"Chapter {chapterNameEditors.Count + 1}" };
            name.TextChanged += (_, _) => UpdatePresetPresentation();
            chapterNameEditors.Add(name);
            ChapterEditorPanel.Children.Add(name);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HandBrake.App.Core.Utilities;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private static string ChapterSourceName(SourceChapter chapter, int index)
        => string.IsNullOrEmpty(chapter.Name) ? string.Format(CultureInfo.CurrentCulture, WpfText.Label("ChapterViewModel_Chapter"), index + 1) : chapter.Name;

    private void DisplayChapterEditor(SourceTitle title)
    {
        ChapterEditorPanel.Children.Clear();
        ChapterEditorPanel.RowDefinitions.Clear();
        chapterNameEditors.Clear();
        AddChapterCell(new TextBlock { Text = WpfText.Label("ChaptersView_ChapterNumber") }, 0, 0);
        AddChapterCell(new TextBlock { Text = WpfText.Label("ChaptersView_Duration") }, 0, 1);
        AddChapterCell(new TextBlock { Text = WpfText.Label("ChaptersView_ChapterName") }, 0, 2);
        foreach (var chapter in title.ChapterList ?? new())
        {
            int index = chapterNameEditors.Count;
            var name = new TextBox { Text = ChapterSourceName(chapter, index) };
            AutomationProperties.SetName(name, string.Format(CultureInfo.CurrentCulture, WpfText.Label("ChapterViewModel_Chapter"), index + 1));
            name.TextChanged += (_, _) => RefreshTrackPresentation();
            AddChapterCell(new TextBlock { Text = (index + 1).ToString(CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center }, index + 1, 0);
            AddChapterCell(new TextBlock { Text = FormatDuration(chapter.Duration), VerticalAlignment = VerticalAlignment.Center }, index + 1, 1);
            AddChapterCell(name, index + 1, 2);
            chapterNameEditors.Add(name);
        }
        bool hasChapters = chapterNameEditors.Count > 0;
        ImportChaptersButton.IsEnabled = ExportChaptersButton.IsEnabled = ResetChaptersButton.IsEnabled = hasChapters;
        ChapterListText.Text = hasChapters ? "" : WpfText.Label("SummaryView_NoChapters");
    }

    private void AddChapterCell(FrameworkElement cell, int row, int column)
    {
        while (ChapterEditorPanel.RowDefinitions.Count <= row) ChapterEditorPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(cell, row); Grid.SetColumn(cell, column);
        ChapterEditorPanel.Children.Add(cell);
    }

    private void ResetChapters_Click(object sender, RoutedEventArgs args)
    {
        if (SourceTitlePicker.SelectedItem is ComboBoxItem { Tag: SourceTitle title }) DisplayChapterEditor(title);
        RefreshTrackPresentation();
    }

    private async void ImportChapters_Click(object sender, RoutedEventArgs args)
    {
        if (chapterNameEditors.Count == 0) return;
        try
        {
            var title = (SourceTitlePicker.SelectedItem as ComboBoxItem)?.Tag;
            var picker = new FileOpenPicker(AppWindow.Id) { FileTypeFilter = { ".csv", ".tsv" } };
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var names = new Dictionary<int, string>();
            // Use the same RFC 4180 CSV/TSV parser as the WPF importer.
            foreach (var row in CsvHelper.ParseFile(file.Path))
            {
                if (row.Length < 2 || !int.TryParse(row[0], out int number) || number < 1 || number > chapterNameEditors.Count || !names.TryAdd(number, row[1].Trim()))
                    throw new InvalidDataException("Chapter numbers must be unique and match the scanned source.");
            }
            if (names.Count != chapterNameEditors.Count) throw new InvalidDataException("The imported chapter count does not match the scanned source.");
            if (!ReferenceEquals(title, (SourceTitlePicker.SelectedItem as ComboBoxItem)?.Tag)) throw new InvalidOperationException("The source changed during chapter import.");
            // Validate completely before changing any name.
            for (int index = 0; index < chapterNameEditors.Count; index++) chapterNameEditors[index].Text = names[index + 1];
            ChapterListText.Text = "";
            RefreshTrackPresentation();
        }
        catch (Exception error) { ChapterListText.Text = WpfText.Label("ChaptersViewModel_UnableToImportChaptersWarning") + " " + error.Message; }
    }

    private async void ExportChapters_Click(object sender, RoutedEventArgs args)
    {
        if (chapterNameEditors.Count == 0) return;
        try
        {
            var names = chapterNameEditors.Select(editor => editor.Text).ToArray();
            var picker = new FileSavePicker(AppWindow.Id) { SuggestedFileName = "chapters.csv", ShowOverwritePrompt = true };
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            string csv = string.Join(Environment.NewLine, names.Select((name, index) => $"{index + 1},{CsvHelper.Escape(name)}")) + Environment.NewLine;
            await File.WriteAllTextAsync(file.Path, csv, new UTF8Encoding(false));
            ChapterListText.Text = "";
        }
        catch (Exception error) { ChapterListText.Text = WpfText.Label("ChaptersViewModel_UnableToExportChaptersWarning") + " " + error.Message; }
    }
}

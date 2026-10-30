using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.Json.Encode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace HandBrake.WinUI;

public sealed record ImportedSubtitle(string Path, string Language, string Name, int Offset)
{
    public bool Burn { get; init; }
}

public sealed partial class MainWindow
{
    private sealed record ImportEditor(string Path, TextBox Language, TextBox Name, NumberBox Offset, CheckBox Burn, Border Card);
    private readonly List<ImportEditor> importedSubtitleEditors = new();

    private async void ImportSubtitle_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { FileTypeFilter = { ".srt" } };
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                ValidateSubtitleFile(file.Path);
                if (importedSubtitleEditors.Any(editor => string.Equals(editor.Path, file.Path, StringComparison.OrdinalIgnoreCase))) continue;
                AddImportedSubtitleEditor(new ImportedSubtitle(file.Path, "und", Path.GetFileNameWithoutExtension(file.Path), 0));
            }
            SubtitleImportStatus.Text = "Imported SRT tracks will be captured when you stage the selected title. Files remain at their original paths.";
        }
        catch (Exception error) { SubtitleImportStatus.Text = "Unable to import subtitles: " + error.Message; }
    }

    private void AddImportedSubtitleEditor(ImportedSubtitle import)
    {
        var language = new TextBox { Header = "Language (3-letter ISO code)", Text = import.Language, MaxLength = 3 };
        var name = new TextBox { Header = "Output track name", Text = import.Name };
        var offset = new NumberBox { Header = "Offset (milliseconds)", Value = import.Offset, Minimum = -3600000, Maximum = 3600000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var burn = new CheckBox { Content = "Burn into video", IsChecked = import.Burn };
        var remove = new Button { Content = "Remove" };
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = import.Path, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(CompactRow(language, name, offset, burn, remove));
        var card = new Border { Padding = new Thickness(4), Child = row };
        var editor = new ImportEditor(import.Path, language, name, offset, burn, card);
        remove.Click += (_, _) => { importedSubtitleEditors.Remove(editor); ImportedSubtitleList.Children.Remove(card); };
        importedSubtitleEditors.Add(editor);
        ImportedSubtitleList.Children.Add(card);
    }

    private static void ValidateSubtitleFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".srt", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidOperationException("An imported SRT file is missing. Locate it and stage the job again.");
        // Reject encodings that would be misinterpreted as UTF-8 by the native import.
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), false);
        var buffer = new char[4096];
        while (reader.Read(buffer, 0, buffer.Length) > 0) { }
    }

    private List<ImportedSubtitle> ReadImportedSubtitles()
    {
        var result = new List<ImportedSubtitle>();
        foreach (var editor in importedSubtitleEditors)
        {
            string language = editor.Language.Text.Trim().ToLowerInvariant();
            double offset = editor.Offset.Value;
            if (language.Length != 3 || language.Any(character => character < 'a' || character > 'z') ||
                !double.IsFinite(offset) || offset != Math.Truncate(offset) || Math.Abs(offset) > 3600000)
                throw new InvalidOperationException("Use a 3-letter subtitle language code and a whole offset within one hour.");
            ValidateSubtitleFile(editor.Path);
            result.Add(new ImportedSubtitle(editor.Path, language, editor.Name.Text.Trim(), (int)offset) { Burn = editor.Burn.IsChecked == true });
        }
        return result;
    }

    private static List<SubtitleTrack> BuildImportedSubtitles(IEnumerable<ImportedSubtitle>? imports, string mux)
    {
        var result = new List<SubtitleTrack>();
        foreach (var import in imports ?? Enumerable.Empty<ImportedSubtitle>())
        {
            string container = FormatPresetContainer(mux);
            if (!import.Burn && container is not ("MP4" or "MKV"))
                throw new InvalidOperationException("Imported SRT subtitles currently require MP4 or MKV output.");
            ValidateSubtitleFile(import.Path);
            if (import.Language.Length != 3 || import.Language.Any(character => character < 'a' || character > 'z') || Math.Abs((long)import.Offset) > 3600000)
                throw new InvalidOperationException("Saved subtitle import settings are invalid. Re-stage this job.");
            result.Add(new SubtitleTrack { Track = -1, Burn = import.Burn, Default = false, Forced = false,
                Offset = import.Offset, Name = import.Name,
                Import = new SubImport { Filename = import.Path, Format = "SRT", Codeset = "UTF-8", Language = import.Language } });
        }
        return result;
    }
}

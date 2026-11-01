using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HandBrake.Interop.Interop;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using HandBrake.Interop.Interop.Json.Encode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace HandBrake.WinUI;

public sealed record ImportedSubtitle(string Path, string Language, string Name, int Offset)
{
    public bool Burn { get; init; }
    public bool Default { get; init; }
}

public sealed partial class MainWindow
{
    private sealed record ImportEditor(string Path, TextBox Language, TextBox Name, NumberBox Offset, CheckBox Burn, CheckBox Default, Border Card);
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
            SubtitleImportStatus.Visibility = Visibility.Collapsed;
            RefreshTrackPresentation();
        }
        catch (Exception error) { SubtitleImportStatus.Text = "Unable to import subtitles: " + error.Message; SubtitleImportStatus.Visibility = Visibility.Visible; }
    }

    private void AddImportedSubtitleEditor(ImportedSubtitle import)
    {
        var language = new TextBox { Text = import.Language, MaxLength = 3 };
        var name = new TextBox { Text = import.Name };
        var offset = new NumberBox { Value = import.Offset, Minimum = -3600000, Maximum = 3600000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var burn = new CheckBox { Content = WpfText.Label("SubtitlesView_BurnIn"), IsChecked = import.Burn };
        var defaultTrack = new CheckBox { Content = WpfText.Label("SubtitlesView_Default"), IsChecked = import.Default, IsEnabled = !import.Burn };
        var remove = CompactUi.IconCommand(Symbol.Delete, WpfText.Label("QueueView_Delete"));
        var row = new StackPanel { Spacing = CompactUi.RowGap };
        var source = new TextBlock { Text = import.Path, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(source, CompactUi.Help(import.Path));
        row.Children.Add(source);
        row.Children.Add(CompactUi.AlignedFormRow(
            new CompactFormField(WpfText.Label("OptionsView_Language"), language, FieldWidthRole.ShortNumber),
            new CompactFormField(WpfText.Label("AudioView_TrackName"), name, FieldWidthRole.NamedChoice),
            new CompactFormField("Offset (ms)", offset, FieldWidthRole.Number)));
        row.Children.Add(CompactUi.AlignedFormRow(
            new CompactFormField("", burn, FieldWidthRole.ShortChoice),
            new CompactFormField("", defaultTrack, FieldWidthRole.ShortChoice),
            new CompactFormField("", remove, FieldWidthRole.ShortNumber)));
        ToolTipService.SetToolTip(burn, CompactUi.Help(WpfText.Tooltip("SubtitlesView_BurnIn")));
        burn.Checked += (_, _) => { SelectSubtitleBurnTarget(burn); defaultTrack.IsChecked = false; defaultTrack.IsEnabled = false; RefreshTrackPresentation(); };
        burn.Unchecked += (_, _) => { defaultTrack.IsEnabled = true; RefreshTrackPresentation(); };
        defaultTrack.Checked += (_, _) => { SelectSubtitleDefaultTarget(defaultTrack); RefreshTrackPresentation(); };
        defaultTrack.Unchecked += (_, _) => RefreshTrackPresentation();
        language.TextChanged += (_, _) => RefreshTrackPresentation();
        name.TextChanged += (_, _) => RefreshTrackPresentation();
        offset.ValueChanged += (_, _) => RefreshTrackPresentation();
        var card = new Border { Style = CompactUi.Resource<Style>("HBCompactTrackRowStyle"), Child = row };
        var editor = new ImportEditor(import.Path, language, name, offset, burn, defaultTrack, card);
        remove.Click += (_, _) => { importedSubtitleEditors.Remove(editor); ImportedSubtitleList.Children.Remove(card); RefreshTrackPresentation(); };
        importedSubtitleEditors.Add(editor);
        ImportedSubtitleList.Children.Add(card);
        if (burn.IsChecked == true) SelectSubtitleBurnTarget(burn);
        if (defaultTrack.IsChecked == true) SelectSubtitleDefaultTarget(defaultTrack);
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
            result.Add(new ImportedSubtitle(editor.Path, language, editor.Name.Text.Trim(), (int)offset) { Burn = editor.Burn.IsChecked == true, Default = editor.Default.IsChecked == true });
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
            if (import.Burn && import.Default) throw new InvalidOperationException("A burned subtitle cannot also be the default track.");
            result.Add(new SubtitleTrack { Track = -1, Burn = import.Burn, Default = import.Default, Forced = false,
                Offset = import.Offset, Name = import.Name,
                Import = new SubImport { Filename = import.Path, Format = "SRT", Codeset = "UTF-8", Language = import.Language } });
        }
        return result;
    }
}

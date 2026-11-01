using System;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private bool settingsPresentationReady;
    private readonly TextBox MetadataTitleBox = new();
    private readonly TextBox MetadataArtistBox = new();
    private readonly TextBox MetadataAlbumBox = new();
    private readonly TextBox MetadataCommentBox = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox ForeignAudioSearchToggle = new();
    private readonly CheckBox ForeignAudioForcedToggle = new() { IsChecked = true, IsEnabled = false };
    private readonly CheckBox ForeignAudioBurnToggle = new() { IsEnabled = false };

    private void InitializeRemainingSettingsPresentation()
    {
        ForeignAudioSearchToggle.Content = WpfText.Label("Subtitle_ForeignAudioScan");
        ForeignAudioForcedToggle.Content = WpfText.Label("SubtitlesView_ForcedOnly");
        ForeignAudioBurnToggle.Content = WpfText.Label("SubtitlesView_BurnIn");
        ToolTipService.SetToolTip(ForeignAudioBurnToggle, CompactUi.Help(WpfText.Tooltip("SubtitlesView_BurnIn")));
        ForeignSearchFields.Content = CompactUi.AlignedFormRow(
            new CompactFormField("", ForeignAudioSearchToggle, FieldWidthRole.NamedChoice),
            new CompactFormField("", ForeignAudioForcedToggle, FieldWidthRole.ShortChoice),
            new CompactFormField("", ForeignAudioBurnToggle, FieldWidthRole.ShortChoice));
        ForeignAudioSearchToggle.Checked += ForeignSearch_Toggled;
        ForeignAudioSearchToggle.Unchecked += ForeignSearch_Toggled;
        ForeignAudioSearchToggle.Checked += (_, _) => { if (ForeignAudioBurnToggle.IsChecked == true) SelectSubtitleBurnTarget(ForeignAudioBurnToggle); };
        ForeignAudioBurnToggle.Checked += (_, _) => { if (ForeignAudioSearchToggle.IsChecked == true) SelectSubtitleBurnTarget(ForeignAudioBurnToggle); };
        foreach (var toggle in new[] { ForeignAudioSearchToggle, ForeignAudioForcedToggle, ForeignAudioBurnToggle })
        {
            toggle.Checked += (_, _) => RefreshTrackPresentation();
            toggle.Unchecked += (_, _) => RefreshTrackPresentation();
        }
        AddMetadataField(WpfText.Label("MetadataView_TitleTag"), MetadataTitleBox);
        // Artist/Album are native annotation keys in the generic WPF metadata grid.
        AddMetadataField("Artist", MetadataArtistBox);
        AddMetadataField("Album", MetadataAlbumBox);
        MetadataCommentBox.Style = CompactUi.Resource<Style>("HBCompactMultilineTextBoxStyle");
        AddMetadataField(WpfText.Label("MetadataView_Comments"), MetadataCommentBox, multiline: true);
        settingsPresentationReady = true;
        EditorTabs.SelectedItem = SummaryTab;
        ShowSettingsPage();
    }

    private void AddMetadataField(string caption, TextBox box, bool multiline = false)
    {
        int row = MetadataFields.RowDefinitions.Count;
        var label = new TextBlock { Text = caption, Style = CompactUi.Resource<Style>("HBCompactFormSideLabelStyle") };
        FieldLayout.SetRole(box, multiline ? FieldWidthRole.Path : FieldWidthRole.Name);
        AutomationProperties.SetLabeledBy(box, label);
        MetadataFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
        MetadataFields.Children.Add(label); MetadataFields.Children.Add(box);
        box.TextChanged += (_, _) => RefreshTrackPresentation();
    }

    private void RefreshMetadataPlaceholders()
    {
        var title = (SourceTitlePicker.SelectedItem as ComboBoxItem)?.Tag as SourceTitle;
        var metadata = metadataPassthru.IsChecked == true ? title?.MetaData : null;
        MetadataTitleBox.PlaceholderText = metadata?.GetValueOrDefault("Name") ?? "";
        MetadataArtistBox.PlaceholderText = metadata?.GetValueOrDefault("Artist") ?? "";
        MetadataAlbumBox.PlaceholderText = metadata?.GetValueOrDefault("Album") ?? "";
        MetadataCommentBox.PlaceholderText = metadata?.GetValueOrDefault("Comment") ?? "";
    }

    private void ShowSettingsPage()
    {
        if (EditorPages is null || EditorTabs.SelectedItem is null) return;
        int selected = EditorTabs.Items.IndexOf(EditorTabs.SelectedItem);
        for (int index = 0; index < EditorPages.Children.Count; index++)
        {
            var page = EditorPages.Children[index];
            bool show = index == selected;
            bool wasVisible = page.Visibility == Visibility.Visible;
            page.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show && !wasVisible) AnimateOpacity(page, 0, 1, 140);
        }
    }

    private void RefreshTrackPresentation()
    {
        if (settingsPresentationReady && !updatingVideoEditor) RefreshOutputSummary();
    }

    private void ReloadAudio_Click(object sender, RoutedEventArgs args)
    {
        if (SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title }) return;
        DisplayAudioEditor(title);
        UsePresetAudioToggle.IsChecked = true;
        RefreshTrackPresentation();
    }

    private void ClearAudio_Click(object sender, RoutedEventArgs args)
    {
        UsePresetAudioToggle.IsChecked = false;
        foreach (var editor in audioEditors) editor.Selected.IsChecked = false;
        RefreshTrackPresentation();
    }

    private void ReloadSubtitles_Click(object sender, RoutedEventArgs args)
    {
        if (SourceTitlePicker.SelectedItem is not ComboBoxItem { Tag: SourceTitle title }) return;
        DisplaySubtitleEditor(title);
        importedSubtitleEditors.Clear(); ImportedSubtitleList.Children.Clear();
        ForeignAudioSearchToggle.IsChecked = false;
        UsePresetSubtitlesToggle.IsChecked = true;
        SubtitleImportStatus.Visibility = Visibility.Collapsed;
        RefreshTrackPresentation();
    }

    private void ClearSubtitles_Click(object sender, RoutedEventArgs args)
    {
        UsePresetSubtitlesToggle.IsChecked = false;
        foreach (var editor in subtitleEditors) editor.Selected.IsChecked = false;
        ForeignAudioSearchToggle.IsChecked = false;
        importedSubtitleEditors.Clear(); ImportedSubtitleList.Children.Clear();
        SubtitleImportStatus.Visibility = Visibility.Collapsed;
        RefreshTrackPresentation();
    }

    private void SelectSubtitleDefaultTarget(CheckBox target)
    {
        foreach (var editor in subtitleEditors)
            if (editor.Default != target) editor.Default.IsChecked = false;
        foreach (var editor in importedSubtitleEditors)
            if (editor.Default != target) editor.Default.IsChecked = false;
    }

    private static void ValidateSubtitleDefaults(System.Collections.Generic.IEnumerable<SubtitleChoice>? subtitles,
        System.Collections.Generic.IEnumerable<ImportedSubtitle>? imports)
    {
        if ((subtitles?.Count(track => track.Default) ?? 0) + (imports?.Count(track => track.Default) ?? 0) > 1)
            throw new InvalidOperationException("Select at most one default subtitle track.");
    }

    private void SelectSubtitleBurnTarget(CheckBox target)
    {
        foreach (var editor in subtitleEditors)
            if (editor.Burn != target) editor.Burn.IsChecked = false;
        foreach (var editor in importedSubtitleEditors)
            if (editor.Burn != target) editor.Burn.IsChecked = false;
        if (ForeignAudioBurnToggle != target) ForeignAudioBurnToggle.IsChecked = false;
    }
}

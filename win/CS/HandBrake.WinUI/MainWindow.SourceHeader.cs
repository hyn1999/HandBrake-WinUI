using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private StackPanel? sourceDurationPanel;
    private string? sourceFieldLayout;
    private bool sourceRangeHasBeenShown;
    private string sourceLocationDetails = "";

    private void InitializeSourceHeaderHelp()
    {
        Design.FieldLayout.SetRole(SourceTitlePicker, Design.FieldWidthRole.NamedChoice);
        ToolTipService.SetToolTip(SourceTitlePicker, Design.CompactUi.Help(WpfText.Tooltip("MainView_Title")));
        ToolTipService.SetToolTip(anglePicker, Design.CompactUi.Help(WpfText.Tooltip("MainView_Angle")));
        ToolTipService.SetToolTip(sourceRangePicker, Design.CompactUi.Help(WpfText.Tooltip("MainView_Range")));
        ToolTipService.SetToolTip(rangeStartBox, Design.CompactUi.Help(WpfText.Tooltip("MainView_StartPoint")));
        ToolTipService.SetToolTip(rangeEndBox, Design.CompactUi.Help(WpfText.Tooltip("MainView_EndPoint")));
        sourceDurationPanel = new StackPanel { Spacing = Design.CompactUi.LabelGap };
        sourceDurationPanel.Children.Add(new TextBlock { Text = "Duration" });
        rangeDurationText.MinHeight = Design.CompactUi.ControlHeight;
        sourceDurationPanel.Children.Add(rangeDurationText);
        ToolTipService.SetToolTip(sourceDurationPanel, Design.CompactUi.Help(WpfText.Tooltip("MainView_Duration")));
    }

    private void InitializeSourcePresentation()
    {
        SourceStatusText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => UpdateSourceActivity());
        SourceScanProgress.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateSourceActivity());
        CancelScanButton.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateSourceActivity());
        SetSourceRangeVisible(false);
        UpdateSourceActivity();
        UpdateEncodeControls();
    }

    private void UpdateSourceActivity()
    {
        if (SourceActivityPanel is null) return;
        string status = SourceStatusText.Text ?? "";
        bool routineSuccess = status.StartsWith("Scan complete", StringComparison.Ordinal) || status == "Edit canceled.";
        SourceStatusText.Visibility = status.Length > 0 && !routineSuccess ? Visibility.Visible : Visibility.Collapsed;
        SourceActivityPanel.Visibility = SourceStatusText.Visibility == Visibility.Visible || SourceScanProgress.Visibility == Visibility.Visible || CancelScanButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        if (!scanInProgress && SourceTitlePicker.Items.Count == 0 &&
            (status == "Source scan cancelled." || status.Contains("no usable video titles", StringComparison.Ordinal) ||
             status.StartsWith("HandBrake native scan could not start", StringComparison.Ordinal) ||
             status.StartsWith("Source selected. Native HandBrake scanner unavailable", StringComparison.Ordinal)))
            SetSourceRangeVisible(false);
    }

    private void SetSourceRangeVisible(bool visible)
    {
        if (SourceRangeGate is null) return;
        bool reveal = visible && SourceRangeGate.Visibility != Visibility.Visible;
        sourceRangeHasBeenShown = visible;
        SourceRangeGate.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            SourceRangePanel.Visibility = Visibility.Visible;
            if (reveal) AnimateOpacity(SourceRangeGate, 0, 1, 140);
        }
    }

    private void SetSourceLocationDetails(string details)
    {
        sourceLocationDetails = details;
        ToolTipService.SetToolTip(SelectedFileText, Design.CompactUi.Help(SelectedFileText.Text + Environment.NewLine + details));
        CopySourcePathItem.IsEnabled = OpenSourceFolderItem.IsEnabled = selectedSourcePaths.Count > 0;
    }

    private void UpdateSourceFacts(SourceTitle title)
    {
        var facts = new List<string>();
        if (title.Geometry is { } geometry) facts.Add($"{geometry.Width} × {geometry.Height}");
        if (title.FrameRate is { Den: > 0 } rate) facts.Add($"{(double)rate.Num / rate.Den:0.###} fps");
        if (!string.IsNullOrWhiteSpace(title.VideoCodec)) facts.Add(title.VideoCodec switch { "h264" => "H.264", "hevc" => "HEVC", "av1" => "AV1", _ => title.VideoCodec });
        facts.Add(FormatDuration(title.Duration));
        facts.Add($"{title.AudioList?.Count ?? 0} audio");
        facts.Add($"{title.SubtitleList?.Count ?? 0} subtitles");
        SourceFactsText.Text = string.Join(" · ", facts);
        ToolTipService.SetToolTip(SourceFactsText, Design.CompactUi.Help(SourceFactsText.Text));
        AutomationProperties.SetName(SourceFactsText, SourceFactsText.Text);
        SetSourceLocationDetails(sourceLocationDetails);
    }

    private void CopySourcePath_Click(object sender, RoutedEventArgs args)
    {
        if (selectedSourcePaths.Count == 0) return;
        try
        {
            var content = new DataPackage(); content.SetText(string.Join(Environment.NewLine, selectedSourcePaths));
            Clipboard.SetContent(content);
        }
        catch (Exception error) { SourceStatusText.Text = "Unable to copy source path: " + error.Message; }
    }

    private async void OpenSourceFolder_Click(object sender, RoutedEventArgs args)
    {
        if (selectedSourcePaths.Count == 0) return;
        try
        {
            string path = selectedSourcePaths[0];
            string? folderPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) throw new IOException("The source folder is unavailable.");
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folderPath);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception error) { SourceStatusText.Text = "Unable to open source folder: " + error.Message; }
    }

    private void SourceHeader_SizeChanged(object sender, SizeChangedEventArgs args) => LayoutSourceHeader();

    private void LayoutSourceHeader()
    {
        if (SourceHeader is null || SourceHeader.ActualWidth <= 0 || sourceDurationPanel is null || rangeBoundsPanel is null) return;
        SelectedFileText.MaxWidth = Math.Min(Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.Name), SourceHeader.ActualWidth * 0.4);
        // Toolbar sizing/overflow belongs to the native CommandBar. Only the
        // settings fields use our bounded wrapping recipe.
        Design.CompactUi.WrapFields(SourceRangePanel, new List<(FrameworkElement, double)>
        {
            (SourceTitlePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.NamedChoice)),
            (anglePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortNumber)),
            (sourceRangePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortChoice)),
            (rangeBoundsPanel, 2 * Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortNumber) + Design.CompactUi.ControlGap),
            (sourceDurationPanel, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortNumber))
        }, SourceHeader.ActualWidth, ref sourceFieldLayout);
    }
}

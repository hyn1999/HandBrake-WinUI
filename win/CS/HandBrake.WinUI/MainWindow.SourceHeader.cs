using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private List<FrameworkElement>? sourceCommandButtons;
    private StackPanel? sourceDurationPanel;
    private string? sourceFieldLayout;
    private string? sourceCommandLayout;

    private void InitializeSourceHeaderHelp()
    {
        // Verbatim values from WPF Properties/ResourcesTooltips.resx at
        // 112b214569449daf810c521bc6a1a415f4cfe09b (MainView_* keys).
        ToolTipService.SetToolTip(SourceTitlePicker, "Title, or video clip, to encode.\nBlu-ray and DVD sources often have multiple titles, the longest of which is typically the main feature");
        ToolTipService.SetToolTip(anglePicker, "Video angle to encode. Only applicable to multi-angle DVD and Blu-ray.");
        ToolTipService.SetToolTip(sourceRangePicker, "Source range selection. By default, all chapters are selected and the entire source is encoded.");
        ToolTipService.SetToolTip(rangeStartBox, "The start point for this range.");
        ToolTipService.SetToolTip(rangeEndBox, "The end point for this range.");
        sourceDurationPanel = new StackPanel { Spacing = Design.CompactUi.RowGap };
        sourceDurationPanel.Children.Add(new TextBlock { Text = "Duration" });
        rangeDurationText.MinHeight = Design.CompactUi.ControlHeight;
        rangeDurationText.VerticalAlignment = VerticalAlignment.Center;
        sourceDurationPanel.Children.Add(rangeDurationText);
        ToolTipService.SetToolTip(sourceDurationPanel, "Duration of the selected source range in Hours:Minutes:Seconds");
    }

    private void SourceHeader_SizeChanged(object sender, SizeChangedEventArgs args) => LayoutSourceHeader();

    private void LayoutSourceHeader()
    {
        if (SourceHeader is null || SourceHeader.ActualWidth <= 0 || sourceDurationPanel is null || rangeBoundsPanel is null) return;
        sourceCommandButtons ??= WorkspaceCommands.Children.OfType<FrameworkElement>().ToList();
        var commands = sourceCommandButtons.Select(button => (button, button == StageQueueButton ? 148d : 136d)).ToList();
        Design.CompactUi.WrapFields(WorkspaceCommands, commands, SourceHeader.ActualWidth, ref sourceCommandLayout);
        Design.CompactUi.WrapFields(SourceRangePanel, new List<(FrameworkElement, double)>
        {
            (SourceTitlePicker, 320), (anglePicker, 80), (sourceRangePicker, 140),
            (rangeBoundsPanel, 300), (sourceDurationPanel, 180)
        }, SourceHeader.ActualWidth, ref sourceFieldLayout);
    }

}

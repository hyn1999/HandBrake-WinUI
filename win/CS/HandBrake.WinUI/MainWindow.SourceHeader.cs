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
        Design.FieldLayout.SetRole(SourceTitlePicker, Design.FieldWidthRole.Name);
        // Verbatim values from WPF Properties/ResourcesTooltips.resx at
        // 112b214569449daf810c521bc6a1a415f4cfe09b (MainView_* keys).
        ToolTipService.SetToolTip(SourceTitlePicker, Design.CompactUi.Help("Title, or video clip, to encode.\nBlu-ray and DVD sources often have multiple titles, the longest of which is typically the main feature"));
        ToolTipService.SetToolTip(anglePicker, Design.CompactUi.Help("Video angle to encode. Only applicable to multi-angle DVD and Blu-ray."));
        ToolTipService.SetToolTip(sourceRangePicker, Design.CompactUi.Help("Source range selection. By default, all chapters are selected and the entire source is encoded."));
        ToolTipService.SetToolTip(rangeStartBox, Design.CompactUi.Help("The start point for this range."));
        ToolTipService.SetToolTip(rangeEndBox, Design.CompactUi.Help("The end point for this range."));
        sourceDurationPanel = new StackPanel { Spacing = Design.CompactUi.LabelGap };
        sourceDurationPanel.Children.Add(new TextBlock { Text = "Duration" });
        rangeDurationText.MinHeight = Design.CompactUi.ControlHeight;
        rangeDurationText.VerticalAlignment = VerticalAlignment.Center;
        sourceDurationPanel.Children.Add(rangeDurationText);
        ToolTipService.SetToolTip(sourceDurationPanel, Design.CompactUi.Help("Duration of the selected source range in Hours:Minutes:Seconds"));
    }

    private void SourceHeader_SizeChanged(object sender, SizeChangedEventArgs args) => LayoutSourceHeader();

    private void LayoutSourceHeader()
    {
        if (SourceHeader is null || SourceHeader.ActualWidth <= 0 || sourceDurationPanel is null || rangeBoundsPanel is null) return;
        sourceCommandButtons ??= WorkspaceCommands.Children.OfType<FrameworkElement>().ToList();
        var commands = sourceCommandButtons.Select(button => (button, button == StageQueueButton ? 148d : 136d)).ToList();
        Design.CompactUi.WrapFields(WorkspaceCommands, commands, SourceHeader.ActualWidth, ref sourceCommandLayout,
            Design.CompactUi.Resource<double>("HBMainCommandGap"));
        Design.CompactUi.WrapFields(SourceRangePanel, new List<(FrameworkElement, double)>
        {
            (SourceTitlePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.Name)),
            (anglePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortNumber)),
            (sourceRangePicker, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortChoice)),
            (rangeBoundsPanel, 2 * Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.Number) + Design.CompactUi.ControlGap),
            (sourceDurationPanel, Design.FieldLayout.PreferredWidth(Design.FieldWidthRole.ShortChoice))
        }, SourceHeader.ActualWidth, ref sourceFieldLayout);
    }

}

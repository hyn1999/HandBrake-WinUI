using System;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private readonly ComboBox VideoEncoderPicker = new() { IsEnabled = false };
    private readonly NumberBox VideoBitrateBox = new() { Minimum = 100, Maximum = 200000, Value = 2500, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly RadioButton averageBitrateChoice = new() { GroupName = "VideoRateControl", IsEnabled = false };
    private readonly RadioButton constantFramerateChoice = new() { GroupName = "VideoFramerate" };
    private readonly RadioButton variableFramerateChoice = new() { GroupName = "VideoFramerate" };
    private readonly RadioButton peakFramerateChoice = new() { GroupName = "VideoFramerate" };
    private bool qualityAdjustmentAvailable;
    private int videoRateMode = -1; // 0 = native constant quality; 1 = native average bitrate.
    private string frameMode = "vfr";
    private bool updatingVideoPresentation;

    private void InitializeVideoPresentation()
    {
        AddVideoFormRow(VideoBasicsPanel, "VideoView_Codec", VideoEncoderPicker, FieldWidthRole.NamedChoice);
        var framerate = new StackPanel { Spacing = CompactUi.LabelGap };
        FieldLayout.SetRole(frameRatePicker, FieldWidthRole.ShortChoice);
        framerate.Children.Add(frameRatePicker);
        constantFramerateChoice.Content = WpfText.Label("VideoView_ConstantFramerate");
        variableFramerateChoice.Content = WpfText.Label("VideoView_VariableFramerate");
        peakFramerateChoice.Content = WpfText.Label("VideoView_PeakFramerate");
        foreach (var choice in new[] { constantFramerateChoice, variableFramerateChoice, peakFramerateChoice })
        {
            framerate.Children.Add(choice);
            choice.Checked += FramerateMode_Checked;
        }
        AddVideoFormRow(VideoBasicsPanel, "VideoView_Framerate", framerate);
        AddVideoFormRow(VideoBasicsPanel, "VideoView_ColourRange", colorRangePicker, FieldWidthRole.ShortChoice);

        averageBitrateChoice.Content = WpfText.Label("VideoView_AverageBitrate");
        averageBitrateChoice.Checked += VideoRateControl_Checked;
        VideoBitrateRow.Content = CompactUi.AlignedFormRow(
            new CompactFormField("", averageBitrateChoice, FieldWidthRole.ShortChoice),
            new CompactFormField("", VideoBitrateBox, FieldWidthRole.Number));
        AutomationProperties.SetName(VideoBitrateBox, WpfText.Label("VideoView_AverageBitrate"));
        multiPass.Content = WpfText.Label("VideoView_MultiPass");
        turboPass.Content = WpfText.Label("VideoView_TurboAnalysisPass");
        fastDecode.Content = WpfText.Label("VideoView_FastDecode");
        VideoPassOptions.Children.Add(CompactUi.AlignedFormRow(
            new CompactFormField("", multiPass, FieldWidthRole.ShortChoice),
            new CompactFormField("", turboPass, FieldWidthRole.ShortChoice)));

        AddVideoFormRow(AdvancedVideoPanel, "VideoView_EncoderPreset", speedPicker, FieldWidthRole.ShortChoice);
        AddVideoFormRow(AdvancedVideoPanel, "VideoView_EncodeTune", CompactUi.AlignedFormRow(
            new CompactFormField("", tunePicker, FieldWidthRole.ShortChoice),
            new CompactFormField("", fastDecode, FieldWidthRole.ShortChoice)));
        var levelRow = new Grid { ColumnSpacing = CompactUi.ControlGap };
        levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        levelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddVideoFormRow(levelRow, "VideoView_EncoderLevel", levelPicker, FieldWidthRole.ShortChoice);
        AddVideoFormRow(AdvancedVideoPanel, "VideoView_EncoderProfile", CompactUi.AlignedFormRow(
            new CompactFormField("", profilePicker, FieldWidthRole.ShortChoice),
            new CompactFormField("", levelRow, FieldWidthRole.NamedChoice)));
        encoderArguments.Style = CompactUi.Resource<Style>("HBCompactMultilineTextBoxStyle");
        AddVideoFormRow(AdvancedVideoPanel, "VideoView_ExtraOptions", encoderArguments, FieldWidthRole.Path);
        VideoEncoderPicker.SelectionChanged += VideoEncoderPicker_SelectionChanged;
        VideoBitrateBox.ValueChanged += VideoBitrateBox_ValueChanged;
        InitializeVideoHelp();
        RefreshFrameModeControls();
    }

    private static void AddVideoFormRow(Grid host, string labelKey, FrameworkElement body, FieldWidthRole role = FieldWidthRole.None)
    {
        int row = host.RowDefinitions.Count;
        var label = new TextBlock { Text = WpfText.Label(labelKey), Style = CompactUi.Resource<Style>("HBCompactFormSideLabelStyle") };
        if (role != FieldWidthRole.None) FieldLayout.SetRole(body, role);
        AutomationProperties.SetLabeledBy(body, label);
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, row); Grid.SetRow(body, row); Grid.SetColumn(body, 1);
        host.Children.Add(label); host.Children.Add(body);
    }

    private void VideoMainGroups_SizeChanged(object sender, SizeChangedEventArgs args)
        => CompactUi.ReflowSettingsGroups(VideoMainGroups);

    private void SetVideoRateMode(int mode)
    {
        videoRateMode = mode is 0 or 1 ? mode : -1;
        updatingVideoPresentation = true;
        try
        {
            ConstantQualityChoice.IsChecked = videoRateMode == 0;
            averageBitrateChoice.IsChecked = videoRateMode == 1;
        }
        finally { updatingVideoPresentation = false; }
    }

    private void VideoRateControl_Checked(object sender, RoutedEventArgs args)
    {
        if (updatingVideoPresentation || updatingVideoEditor) return;
        SetVideoRateMode(sender == ConstantQualityChoice ? 0 : 1);
        UpdateVideoModeControls();
        RefreshOutputSummary();
    }

    private void SetFrameMode(string? mode)
    {
        frameMode = mode is null or "" ? "vfr" : mode;
        if (frameMode is not ("cfr" or "vfr" or "pfr"))
            throw new InvalidOperationException("The saved frame-rate mode is unavailable.");
        RefreshFrameModeControls();
    }

    private void RefreshFrameModeControls()
    {
        bool sourceRate = ChoiceValue(frameRatePicker) is "" or "auto" or "0";
        if (sourceRate && frameMode == "pfr") frameMode = "vfr";
        updatingVideoPresentation = true;
        try
        {
            constantFramerateChoice.IsChecked = frameMode == "cfr";
            variableFramerateChoice.IsChecked = frameMode == "vfr";
            peakFramerateChoice.IsChecked = frameMode == "pfr";
            // A saved explicit-rate VFR job retains its meaning. Normal user
            // rate changes follow WPF: explicit rate -> PFR, source rate -> VFR.
            variableFramerateChoice.Visibility = sourceRate || frameMode == "vfr" ? Visibility.Visible : Visibility.Collapsed;
            peakFramerateChoice.Visibility = sourceRate ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { updatingVideoPresentation = false; }
    }

    private void FramerateMode_Checked(object sender, RoutedEventArgs args)
    {
        if (updatingVideoPresentation || updatingAdvancedEditor || updatingVideoEditor) return;
        SetFrameMode(sender == constantFramerateChoice ? "cfr" : sender == peakFramerateChoice ? "pfr" : "vfr");
        AdvancedOption_Changed(sender, args);
    }

    private void InitializeVideoHelp()
    {
        foreach (var (control, key) in new (DependencyObject, string)[]
        {
            (VideoEncoderPicker, "Video_Encoders"), (frameRatePicker, "Video_Framerate"),
            (colorRangePicker, "Video_ColourRange"), (constantFramerateChoice, "Video_ConstantFramerate"),
            (variableFramerateChoice, "Video_VariableFramerate"), (peakFramerateChoice, "Video_PeakFramerate"),
            (ConstantQualityChoice, "Video_Quality"), (VideoQualitySlider, "Video_Quality"),
            (averageBitrateChoice, "Video_AvgBitrate"), (VideoBitrateBox, "Video_AvgBitrate"),
            (multiPass, "Video_MultiPass"), (turboPass, "Video_TurboAnalysisPass"),
            (speedPicker, "Video_Presets"), (tunePicker, "Video_EncoderTune"), (fastDecode, "Video_FastDecode"),
            (profilePicker, "Video_Profile"), (levelPicker, "Video_Level"), (encoderArguments, "Video_ExtraOpts")
        }) ToolTipService.SetToolTip(control, CompactUi.Help(control == speedPicker ? WpfText.Tooltip(key, paragraphs: 2) : WpfText.Tooltip(key)));
        AutomationProperties.SetName(VideoQualitySlider, WpfText.Label("VideoView_ConstantQuality"));
        AutomationProperties.SetName(frameRatePicker, WpfText.Label("VideoView_Framerate"));
        AutomationProperties.SetName(tunePicker, WpfText.Label("VideoView_EncodeTune"));
        AutomationProperties.SetName(profilePicker, WpfText.Label("VideoView_EncoderProfile"));
        AutomationProperties.SetName(speedPicker, WpfText.Label("VideoView_EncoderPreset"));
    }
}

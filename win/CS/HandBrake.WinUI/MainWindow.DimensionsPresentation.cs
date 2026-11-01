using System;
using System.Globalization;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.WinUI.Design;
using HandBrake.WinUI.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private readonly NumberBox OutputWidthBox = new() { Minimum = 2, Maximum = 16384, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly NumberBox OutputHeightBox = new() { Minimum = 2, Maximum = 16384, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };

    private void InitializeDimensionsPresentation()
    {
        DimensionsSizeFields.Content = CompactUi.AlignedFormRow(
            new CompactFormField(WpfText.Label("PictureSettingsView_Width"), OutputWidthBox, FieldWidthRole.Number),
            new CompactFormField(WpfText.Label("PictureSettingsView_Height"), OutputHeightBox, FieldWidthRole.Number));
        foreach (var box in new[] { OutputWidthBox, OutputHeightBox, CropTopBox, CropBottomBox, CropLeftBox, CropRightBox })
            box.ValueChanged += (_, _) => { if (!updatingVideoEditor) RefreshOutputSummary(); };
        // Shared keys resolve directly against original WPF resources at runtime.
        ToolTipService.SetToolTip(OutputWidthBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_Width")));
        ToolTipService.SetToolTip(OutputHeightBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_Height")));
        ToolTipService.SetToolTip(CropTopBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_CropTop")));
        ToolTipService.SetToolTip(CropBottomBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_CropBottom")));
        ToolTipService.SetToolTip(CropLeftBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_CropLeft")));
        ToolTipService.SetToolTip(CropRightBox, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_CropRight")));
        ToolTipService.SetToolTip(CustomGeometryToggle, CompactUi.Help(WpfText.Tooltip("PictureSettingsView_ManualCrop")));
    }

    private void DimensionsGroups_SizeChanged(object sender, SizeChangedEventArgs args)
        => CompactUi.ReflowSettingsGroups(DimensionsGroups);

    private void ClearDimensionsPresentation()
    {
        if (FinalDimensionsText is null) return;
        SourceDimensionsText.Text = FinalDimensionsText.Text = OrientationSummaryText.Text = ScalingSummaryText.Text = BordersSummaryText.Text = "—";
        CustomGeometryToggle.IsEnabled = false;
        foreach (var box in new[] { OutputWidthBox, OutputHeightBox, CropTopBox, CropBottomBox, CropLeftBox, CropRightBox }) box.IsEnabled = false;
        ClearDimensionsResult();
    }

    private void ClearDimensionsResult()
    {
        FinalDimensionsText.Text = PixelAspectSummaryText.Text = "—";
        DimensionsStatusText.Visibility = Visibility.Collapsed;
        ToolTipService.SetToolTip(DimensionsStatusText, null);
    }

    private void RefreshDimensionsPresentation()
    {
        if (DimensionsTab is null) return;
        bool manual = CustomGeometryToggle.IsChecked == true;
        CroppingModeText.Text = WpfText.Label("PictureSettingsView_Cropping");
        var title = (SourceTitlePicker.SelectedItem as ComboBoxItem)?.Tag as SourceTitle;
        CustomGeometryToggle.IsEnabled = title is not null && !scanInProgress && ActivePresetChoice is not null;
        if (title?.Geometry is { } geometry)
        {
            long numerator = Math.Max(1, geometry.PAR?.Num ?? 1), denominator = Math.Max(1, geometry.PAR?.Den ?? 1);
            SourceDimensionsText.Text = string.Format(CultureInfo.CurrentCulture, WpfText.Label("PictureSettingsViewModel_StorageDisplayLabel"), geometry.Width, geometry.Height) +
                $" · {WpfText.Label("PictureSettingsView_DisplayWidth")} {(double)geometry.Width * numerator / denominator:0} × {geometry.Height} · " +
                string.Format(CultureInfo.CurrentCulture, WpfText.Label("PictureSettingsViewModel_AspectRatioLabel"), DimensionAspect(geometry.Width * numerator, geometry.Height * denominator));
        }
        else SourceDimensionsText.Text = "—";
        OrientationSummaryText.Text = ScalingSummaryText.Text = BordersSummaryText.Text = "—";
        if (ActivePresetChoice is null) return;
        try
        {
            var preset = JsonNode.Parse(GetEditorPresetJson() ?? "{}")!.AsObject();
            var transform = GeometryTransform(preset);
            string cropKey = manual ? "CropMode_Custom" : preset["PictureCropMode"]?.GetValue<int>() switch
            {
                1 => "CropMode_Loose", 2 => "CropMode_None", 3 => "CropMode_Custom", _ => "CropMode_Auto"
            };
            CroppingModeText.Text += " " + WpfText.Label(cropKey);
            OrientationSummaryText.Text = $"{WpfText.Label("FiltersView_Rotate")} {transform.Angle}° · {WpfText.Label("FiltersView_Flip")} {WpfText.Label(transform.Flip == 1 ? "FiltersView_FlipVideo" : "SubtitleBehaviourModes_None")}";
            string pad = preset["PicturePadMode"]?.ToString() ?? "none";
            string padKey = pad switch { "custom" => "PaddingMode_Custom", "fill" => "PaddingMode_Fill", "letterbox" => "PaddingMode_Height", "pillarbox" => "PaddingMode_Width", _ => "PaddingMode_None" };
            BordersSummaryText.Text = WpfText.Label("PictureSettingsView_Padding") + " " + WpfText.Label(padKey);
            if (pad != "none")
            {
                BordersSummaryText.Text += $"\n{WpfText.Label("PictureSettingsView_PaddingColour")} {preset["PicturePadColor"]}";
                if (pad == "custom") BordersSummaryText.Text += $"\nTop: {preset["PicturePadTop"]} · Bottom: {preset["PicturePadBottom"]}\nLeft: {preset["PicturePadLeft"]} · Right: {preset["PicturePadRight"]}";
            }
            string anamorphic = preset["PicturePAR"]?.ToString() ?? "auto";
            string anamorphicKey = anamorphic switch { "none" => "AnamorphicMode_None", "custom" => "AnamorphicMode_Custom", _ => "AnamorphicMode_Automatic" };
            ScalingSummaryText.Text = manual ? WpfText.Label("PictureSettingsView_Custom") : $"{WpfText.Label("PictureSettingsView_ResLimit")} {DimensionLimit(preset)}\n{WpfText.Label("PictureSettingsView_Anamorphic")} {WpfText.Label(anamorphicKey)}";
            if (!manual && preset["PictureUseMaximumSize"]?.GetValue<bool>() == true)
                ScalingSummaryText.Text += "\n" + WpfText.Label("PictureSettingsView_OptimalSize");
            if (!manual && preset["PictureAllowUpscaling"]?.GetValue<bool>() == true)
                ScalingSummaryText.Text += "\n" + WpfText.Label("PictureSettingsView_AllowUpscaling");
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            DimensionsStatusText.Text = "Dimensions unavailable. Review picture settings.";
            DimensionsStatusText.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DimensionsStatusText, CompactUi.Help(error.Message));
        }
    }

    private static string DimensionLimit(JsonObject preset)
    {
        string width = preset["PictureWidth"]?.ToString() ?? "0", height = preset["PictureHeight"]?.ToString() ?? "0";
        return width == "0" && height == "0" ? WpfText.Label("PaddingMode_None") : $"{(width == "0" ? WpfText.Label("PictureSettingsView_Automatic") : width)} × {(height == "0" ? WpfText.Label("PictureSettingsView_Automatic") : height)}";
    }

    private static string DimensionAspect(long width, long height)
    {
        if (width <= 0 || height <= 0) return "—";
        long a = width, b = height;
        while (b != 0) { long next = a % b; a = b; b = next; }
        return $"{width / a}:{height / a}";
    }
}

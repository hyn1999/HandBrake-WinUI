using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Interfaces.Model.Picture;
using HandBrake.Interop.Interop.Json.Scan;

namespace HandBrake.WinUI;

public sealed record GeometryOverride(int Width, int Height, int Top, int Bottom, int Left, int Right);

public sealed partial class MainWindow
{
    private GeometryOverride? ReadGeometryOverride(SourceTitle title)
    {
        if (CustomGeometryToggle.IsChecked != true) return null;
        static int Pixels(double value)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value < 0 || value > 16384)
                throw new InvalidOperationException("Dimensions and crop values must be whole pixels between 0 and 16384.");
            return (int)value;
        }
        var geometry = new GeometryOverride(Pixels(OutputWidthBox.Value), Pixels(OutputHeightBox.Value),
            Pixels(CropTopBox.Value), Pixels(CropBottomBox.Value), Pixels(CropLeftBox.Value), Pixels(CropRightBox.Value));
        ValidateGeometry(geometry, title, JsonNode.Parse(GetEditorPresetJson() ?? "{}")!.AsObject());
        return geometry;
    }

    private static (int Angle, int Flip) GeometryTransform(JsonObject? preset)
    {
        string? rotate = preset?["PictureRotate"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(rotate)) return (0, 0);
        string json = HandBrakeFilterHelpers.GenerateFilterSettingJson((int)hb_filter_ids.HB_FILTER_ROTATE, null, null, rotate);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
            throw new InvalidOperationException("The preset rotation is invalid.");
        using var settings = JsonDocument.Parse(json);
        var root = settings.RootElement;
        if (root.TryGetProperty("disable", out var disabled) &&
            ((disabled.ValueKind == JsonValueKind.True) || (disabled.ValueKind == JsonValueKind.Number && disabled.GetInt32() != 0))) return (0, 0);
        int angle = root.TryGetProperty("angle", out var value) ? value.GetInt32() : 0;
        if (angle is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException("The preset rotation is unsupported.");
        bool flip = root.TryGetProperty("hflip", out var flipValue) &&
            (flipValue.ValueKind == JsonValueKind.True || (flipValue.ValueKind == JsonValueKind.Number && flipValue.GetInt32() != 0));
        return (angle, flip ? 1 : 0);
    }

    private static void ValidateGeometry(GeometryOverride geometry, SourceTitle title, JsonObject? preset = null)
    {
        bool quarterTurn = GeometryTransform(preset).Angle is 90 or 270;
        int sourceWidth = (quarterTurn ? title.Geometry?.Height : title.Geometry?.Width) ?? 0;
        int sourceHeight = (quarterTurn ? title.Geometry?.Width : title.Geometry?.Height) ?? 0;
        if (geometry.Width < 2 || geometry.Height < 2 || geometry.Width > 16384 || geometry.Height > 16384 ||
            geometry.Width % 2 != 0 || geometry.Height % 2 != 0 ||
            geometry.Top < 0 || geometry.Bottom < 0 || geometry.Left < 0 || geometry.Right < 0 ||
            title.Geometry is null ||
            (long)geometry.Top + geometry.Bottom >= sourceHeight ||
            (long)geometry.Left + geometry.Right >= sourceWidth)
            throw new InvalidOperationException("Use even output dimensions from 2 to 16384 and crop edges that leave a nonempty image after preset rotation.");
    }

    private static void ApplyManualGeometry(JsonObject preset, GeometryOverride geometry, SourceTitle title)
    {
        ValidateGeometry(geometry, title, preset);
        bool quarterTurn = GeometryTransform(preset).Angle is 90 or 270;
        int parNum = Math.Max(1, title.Geometry?.PAR?.Num ?? 1);
        int parDen = Math.Max(1, title.Geometry?.PAR?.Den ?? 1);
        string padMode = preset["PicturePadMode"]?.GetValue<string>() ?? "none";
        bool fillWidth = padMode is "fill" or "pillarbox";
        bool fillHeight = padMode is "fill" or "letterbox";
        int canvasWidth = preset["PictureWidth"]?.GetValue<int>() ?? 0;
        int canvasHeight = preset["PictureHeight"]?.GetValue<int>() ?? 0;
        if ((fillWidth && canvasWidth > 0 && geometry.Width > canvasWidth) ||
            (fillHeight && canvasHeight > 0 && geometry.Height > canvasHeight))
            throw new InvalidOperationException("The requested image is larger than the preset's padding canvas. Choose smaller dimensions or another preset.");

        preset["PictureCropMode"] = 3; // Native custom crop, in rotated coordinates.
        preset["PictureTopCrop"] = geometry.Top; preset["PictureBottomCrop"] = geometry.Bottom;
        preset["PictureLeftCrop"] = geometry.Left; preset["PictureRightCrop"] = geometry.Right;
        preset["PictureForceWidth"] = geometry.Width; preset["PictureForceHeight"] = geometry.Height;
        // Manual pixel dimensions replace resolution limits, except limits that
        // define a fill/letterbox/pillarbox canvas. Preserve all padding fields.
        preset["PictureWidth"] = fillWidth ? canvasWidth : 0;
        preset["PictureHeight"] = fillHeight ? canvasHeight : 0;
        preset["PicturePAR"] = "custom";
        preset["PicturePARWidth"] = quarterTurn ? parDen : parNum;
        preset["PicturePARHeight"] = quarterTurn ? parNum : parDen;
        preset["PictureKeepRatio"] = false;
        preset["PictureItuPAR"] = false;
        preset["PictureAllowUpscaling"] = true;
        preset["PictureUseMaximumSize"] = false;
        preset["PictureModulus"] = 2;
        preset["PictureDARWidth"] = 0;
        // PictureRotate and PicturePad* deliberately remain native preset input.
    }

    private void ResetManualGeometryEditor(SourceTitle title)
    {
        try
        {
            var preset = JsonNode.Parse(GetEditorPresetJson() ?? "{}")!.AsObject();
            var transform = GeometryTransform(preset);
            var rotated = HandBrakePictureHelpers.RotateGeometry(new PictureSettingsJob
            {
                Width = title.Geometry?.Width ?? 0, Height = title.Geometry?.Height ?? 0,
                ParW = Math.Max(1, title.Geometry?.PAR?.Num ?? 1), ParH = Math.Max(1, title.Geometry?.PAR?.Den ?? 1),
                RotateAngle = transform.Angle, Hflip = transform.Flip,
                Crop = new()
                {
                    Top = title.Crop?.ElementAtOrDefault(0) ?? 0, Bottom = title.Crop?.ElementAtOrDefault(1) ?? 0,
                    Left = title.Crop?.ElementAtOrDefault(2) ?? 0, Right = title.Crop?.ElementAtOrDefault(3) ?? 0
                },
                Pad = new()
            });
            OutputWidthBox.Value = rotated.Width; OutputHeightBox.Value = rotated.Height;
            CropTopBox.Value = rotated.CropTop; CropBottomBox.Value = rotated.CropBottom;
            CropLeftBox.Value = rotated.CropLeft; CropRightBox.Value = rotated.CropRight;
        }
        catch (Exception error)
        {
            // Invalidate stale values rather than leaving another title's crop
            // in an apparently usable manual editor.
            OutputWidthBox.Value = OutputHeightBox.Value = double.NaN;
            SourceDimensionsText.Text = "Unable to initialize rotated dimensions: " + error.Message;
        }
    }

    private static void VerifyResolvedGeometry(JsonObject job, GeometryOverride requested)
    {
        var scale = job["Filters"]?["FilterList"]?.AsArray().FirstOrDefault(filter =>
            filter?["ID"]?.GetValue<int>() == (int)hb_filter_ids.HB_FILTER_CROP_SCALE)?["Settings"];
        if (scale is null || scale["width"]?.GetValue<int>() != requested.Width ||
            scale["height"]?.GetValue<int>() != requested.Height ||
            scale["crop-top"]?.GetValue<int>() != requested.Top ||
            scale["crop-bottom"]?.GetValue<int>() != requested.Bottom ||
            scale["crop-left"]?.GetValue<int>() != requested.Left ||
            scale["crop-right"]?.GetValue<int>() != requested.Right)
            throw new InvalidOperationException("The native engine could not resolve the requested dimensions and crop exactly. Choose dimensions supported by the source and preset padding canvas.");
    }
}


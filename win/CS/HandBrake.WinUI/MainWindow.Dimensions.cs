using System;
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
        ValidateGeometry(geometry, title);
        return geometry;
    }

    private static void ValidateGeometry(GeometryOverride geometry, SourceTitle title)
    {
        if (geometry.Width < 2 || geometry.Height < 2 || geometry.Width > 16384 || geometry.Height > 16384 ||
            geometry.Width % 2 != 0 || geometry.Height % 2 != 0 ||
            geometry.Top < 0 || geometry.Bottom < 0 || geometry.Left < 0 || geometry.Right < 0 ||
            title.Geometry is null ||
            (long)geometry.Top + geometry.Bottom >= title.Geometry.Height ||
            (long)geometry.Left + geometry.Right >= title.Geometry.Width)
            throw new InvalidOperationException("Use even output dimensions from 2 to 16384 and crop edges that leave a nonempty source image.");
    }
}

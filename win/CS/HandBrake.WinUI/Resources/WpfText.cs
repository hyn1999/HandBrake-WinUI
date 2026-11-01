using System;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace HandBrake.WinUI.Resources;

/// <summary>Read shared product copy from the original WPF string catalogs.
/// These are linked string resources, not a dependency on the WPF UI assembly.</summary>
public static class WpfText
{
    private static readonly ResourceManager Labels = new("HandBrake.WinUI.Resources.WpfLabels", typeof(WpfText).Assembly);
    private static readonly ResourceManager Tooltips = new("HandBrake.WinUI.Resources.WpfTooltips", typeof(WpfText).Assembly);
    public static string Label(string key) => Get(Labels, key);
    public static string Tooltip(string key) => Get(Tooltips, key);
    // Some WinUI controls have a different interaction from their WPF peer.
    // Retain the original descriptive paragraphs, omitting peer-specific UI directions.
    public static string Tooltip(string key, int paragraphs)
        => string.Join("\n\n", Tooltip(key).Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Take(paragraphs));
    private static string Get(ResourceManager resources, string key)
        => resources.GetString(key, CultureInfo.CurrentUICulture)
           ?? throw new InvalidOperationException($"Missing original HandBrake text: {key}");

    public static string Video => Label("VideoView_Video");
    public static string VideoQuality => Label("VideoView_Quality");
    public static string ConstantQuality => Label("VideoView_ConstantQuality");
    public static string LowerQuality => Label("Video_LowQuality");
    public static string VideoEncoderOptions => Label("VideoView_OptimiseVideo");
    public static string Format => Label("MainView_Format");
    public static string Tracks => Label("MainView_Tracks");
    public static string Filters => Label("MainView_Filters");
    public static string Size => Label("MainView_Size");
    public static string SourceDimensions => Label("PictureSettingsView_SourceDimensions");
    public static string FinalDimensions => Label("PictureSettingsView_Output");
    public static string OrientationAndCropping => Label("PictureSettingsView_RotateAndCrop");
    public static string ResolutionAndScaling => Label("PictureSettingsView_ResAndScaling");
    public static string Borders => Label("PictureSettingsView_Borders");
    public static string Top => Label("PictureSettingsView_Top");
    public static string Bottom => Label("PictureSettingsView_Bottom");
    public static string Left => Label("PictureSettingsView_Left");
    public static string Right => Label("PictureSettingsView_Right");
}

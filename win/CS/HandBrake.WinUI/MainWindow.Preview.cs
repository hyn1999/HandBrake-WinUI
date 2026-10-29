using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HandBrake.Interop.Interop;
using HandBrake.Interop.Interop.HbLib;
using HandBrake.Interop.Interop.Interfaces.Model.Preview;
using HandBrake.Interop.Interop.Json.Encode;
using HandBrake.Interop.Interop.Json.Presets;
using HandBrake.Interop.Interop.Json.Scan;
using HandBrake.Interop.Interop.Json.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using EncodeRange = HandBrake.Interop.Interop.Json.Encode.Range;
using EncodeSource = HandBrake.Interop.Interop.Json.Encode.Source;
using EncodeAudioTrack = HandBrake.Interop.Interop.Json.Encode.AudioTrack;
using EncodeSubtitleTrack = HandBrake.Interop.Interop.Json.Encode.SubtitleTrack;

namespace HandBrake.WinUI;

/// <summary>
/// Native decoded-frame preview. Preview generation runs away from the UI thread,
/// and request IDs prevent a slow frame from replacing a newer title selection.
/// </summary>
public sealed partial class MainWindow
{
    private SourceTitle? previewTitle;
    private int previewIndex;
    private int previewRequestId;

    private async Task LoadPreviewAsync(SourceTitle title)
    {
        previewTitle = title;
        previewIndex = 0;
        await RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        int requestId = ++previewRequestId;
        SourceTitle? title = previewTitle;
        HandBrakeInstance? instance = scanInstance;
        int previewCount = Math.Max(1, instance?.PreviewCount ?? SettingsPanel.Preferences.ScanPreviewCount);
        previewIndex = Math.Clamp(previewIndex, 0, previewCount - 1);

        PreviewFrame.Source = null;
        PreviewFrame.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewPlaceholderText.Text = "Decoding a frame with HandBrake…";
        PreviewFrameCounterText.Text = "Loading preview…";
        PreviousPreviewButton.IsEnabled = false;
        NextPreviewButton.IsEnabled = false;

        if (title is null || instance is null || scanInProgress)
        {
            PreviewPlaceholderText.Text = "Scan a source to decode a real frame.";
            PreviewFrameCounterText.Text = "No frame";
            return;
        }

        try
        {
            JsonEncodeObject job = CreatePreviewJob(title);
            int requestedIndex = previewIndex;

            // libhb decoding is synchronous and can take noticeable time. Keep
            // the WinUI dispatcher responsive while the native call runs.
            RawPreviewData raw = await Task.Run(() => instance.GetPreview(job, requestedIndex));
            if (requestId != previewRequestId) return;

            byte[] bitmapBytes = BuildBitmapFile(raw);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bitmapBytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (requestId != previewRequestId) return;

            PreviewFrame.Source = bitmap;
            PreviewFrame.Opacity = 0;
            PreviewFrame.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            AnimateOpacity(PreviewFrame, 0, 1, 180);
            PreviewFrameCounterText.Text = $"Frame {previewIndex + 1} of {previewCount}";
            PreviousPreviewButton.IsEnabled = previewIndex > 0;
            NextPreviewButton.IsEnabled = previewIndex < previewCount - 1;
        }
        catch (Exception ex)
        {
            if (requestId != previewRequestId) return;
            PreviewFrame.Source = null;
            PreviewFrame.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholderText.Text = "HandBrake could not decode this preview frame.";
            PreviewFrameCounterText.Text = "Preview unavailable";
            PreviewMetadataText.Text += Environment.NewLine + ex.Message;
        }
    }

    private JsonEncodeObject CreatePreviewJob(SourceTitle title)
    {
        HBPreset? preset = activePreset;
        string encoderName = ActiveVideoEncoder?.ShortName ?? preset?.VideoEncoder ?? "x264";
        bool useQuality = VideoRateModePicker.SelectedIndex == 0;
        bool useBitrate = VideoRateModePicker.SelectedIndex == 1;

        int cropTop = title.Crop?.ElementAtOrDefault(0) ?? 0;
        int cropBottom = title.Crop?.ElementAtOrDefault(1) ?? 0;
        int cropLeft = title.Crop?.ElementAtOrDefault(2) ?? 0;
        int cropRight = title.Crop?.ElementAtOrDefault(3) ?? 0;
        int sourceWidth = title.Geometry?.Width ?? 0;
        int sourceHeight = title.Geometry?.Height ?? 0;
        int outputWidth = preset?.PictureWidth is > 0
            ? preset.PictureWidth.Value
            : Math.Max(2, sourceWidth - cropLeft - cropRight);
        int outputHeight = preset?.PictureHeight is > 0
            ? preset.PictureHeight.Value
            : Math.Max(2, sourceHeight - cropTop - cropBottom);

        var filters = new List<Filter>();
        if (sourceWidth > 0 && sourceHeight > 0)
        {
            string cropSettings =
                $"width={outputWidth}:height={outputHeight}:crop-top={cropTop}:" +
                $"crop-bottom={cropBottom}:crop-left={cropLeft}:crop-right={cropRight}";
            string json = HandBrakeFilterHelpers.GenerateFilterSettingJson(
                (int)hb_filter_ids.HB_FILTER_CROP_SCALE, null, null, cropSettings);
            if (!string.IsNullOrWhiteSpace(json))
            {
                filters.Add(new Filter
                {
                    ID = (int)hb_filter_ids.HB_FILTER_CROP_SCALE,
                    Settings = JsonDocument.Parse(json)
                });
            }
        }

        int parNum = preset is { PicturePARWidth: > 0 } ? preset.PicturePARWidth : title.Geometry?.PAR?.Num ?? 1;
        int parDen = preset is { PicturePARHeight: > 0 } ? preset.PicturePARHeight : title.Geometry?.PAR?.Den ?? 1;

        return new JsonEncodeObject
        {
            SequenceID = 0,
            Audio = new Audio
            {
                AudioList = new List<EncodeAudioTrack>(),
                CopyMask = preset?.AudioCopyMask?.ToArray() ?? Array.Empty<string>(),
                FallbackEncoder = preset?.AudioEncoderFallback
            },
            Destination = new Destination
            {
                File = string.Empty,
                Mux = string.IsNullOrWhiteSpace(preset?.FileFormat) ? "av_mp4" : preset.FileFormat,
                Options = new Options { IpodAtom = false, Optimize = false },
                ChapterMarkers = false,
                AlignAVStart = false,
                ChapterList = new List<Chapter>()
            },
            Filters = new Filters { FilterList = filters },
            PAR = new PAR { Num = Math.Max(1, parNum), Den = Math.Max(1, parDen) },
            Metadata = title.MetaData ?? new Dictionary<string, string>(),
            CoverArts = title.CoverArts ?? new List<CoverArt>(),
            Source = new EncodeSource
            {
                Path = title.Path,
                Title = title.Index,
                Angle = 0,
                HWDecode = 0,
                KeepDuplicateTitles = title.KeepDuplicateTitles,
                Range = new EncodeRange
                {
                    Type = "preview",
                    Start = 0,
                    End = 30 * 90000L,
                    SeekPoints = Math.Max(1, SettingsPanel.Preferences.ScanPreviewCount)
                }
            },
            Subtitle = new Subtitles
            {
                Search = new SubtitleSearch { Enable = false, Default = false, Burn = false, Forced = false },
                SubtitleList = new List<EncodeSubtitleTrack>()
            },
            Video = new Video
            {
                Encoder = encoderName,
                Level = preset?.VideoLevel,
                Preset = preset?.VideoPreset,
                Profile = preset?.VideoProfile,
                Tune = preset?.VideoTune,
                Options = preset?.VideoOptionExtra,
                Quality = useQuality ? (decimal?)VideoQualitySlider.Value : null,
                Bitrate = useBitrate && !double.IsNaN(VideoBitrateBox.Value)
                    ? (int?)Math.Clamp((int)Math.Round(VideoBitrateBox.Value), 100, 200000)
                    : null,
                MultiPass = false,
                Turbo = false,
                ColorMatrixCode = preset?.VideoColorMatrixCode ?? 0,
                HardwareDecode = 0
            }
        };
    }

    private static byte[] BuildBitmapFile(RawPreviewData preview)
    {
        if (preview.Width <= 0 || preview.Height <= 0)
            throw new InvalidDataException("The native preview returned an empty image.");
        int rowBytes = checked(preview.Width * 4);
        if (preview.StrideWidth < rowBytes ||
            preview.RawBitmapData.Length < checked(preview.StrideWidth * preview.Height))
            throw new InvalidDataException("The native preview buffer has an invalid stride.");

        int imageBytes = checked(rowBytes * preview.Height);
        byte[] bmp = new byte[checked(54 + imageBytes)];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, bmp.Length);
        WriteInt32(bmp, 10, 54);
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, preview.Width);
        WriteInt32(bmp, 22, -preview.Height); // Top-down DIB avoids flipping the frame.
        WriteInt16(bmp, 26, 1);
        WriteInt16(bmp, 28, 32);
        WriteInt32(bmp, 30, 0);
        WriteInt32(bmp, 34, imageBytes);

        for (int row = 0; row < preview.Height; row++)
            Buffer.BlockCopy(preview.RawBitmapData, row * preview.StrideWidth,
                bmp, 54 + row * rowBytes, rowBytes);

        return bmp;
    }

    private static void WriteInt32(byte[] target, int offset, int value)
        => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, target, offset, sizeof(int));

    private static void WriteInt16(byte[] target, int offset, short value)
        => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, target, offset, sizeof(short));

    private async void PreviousPreview_Click(object sender, RoutedEventArgs e)
    {
        if (previewTitle is null || previewIndex <= 0) return;
        previewIndex--;
        await RenderPreviewAsync();
    }

    private async void NextPreview_Click(object sender, RoutedEventArgs e)
    {
        int count = Math.Max(1, scanInstance?.PreviewCount ?? SettingsPanel.Preferences.ScanPreviewCount);
        if (previewTitle is null || previewIndex >= count - 1) return;
        previewIndex++;
        await RenderPreviewAsync();
    }

    private void ResetPreview()
    {
        previewRequestId++;
        previewTitle = null;
        previewIndex = 0;
        PreviewFrame.Source = null;
        PreviewFrame.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewPlaceholderText.Text = "Choose a source to decode a real frame.";
        PreviewFrameCounterText.Text = "No frame";
        PreviousPreviewButton.IsEnabled = false;
        NextPreviewButton.IsEnabled = false;
    }
}

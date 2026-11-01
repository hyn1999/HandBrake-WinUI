using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HandBrake.WinUI;

public sealed partial class PreviewWindow : Window
{
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;

    public PreviewWindow(ElementTheme theme)
    {
        InitializeComponent();
        PreviewRoot.RequestedTheme = theme;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 700));
    }

    public void SetFrame(ImageSource? source, string status, bool previous, bool next)
    {
        FrameImage.Source = source;
        FramePlaceholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        FrameStatus.Text = status;
        PreviousButton.IsEnabled = previous;
        NextButton.IsEnabled = next;
        FitFrame();
    }

    private void FrameViewport_SizeChanged(object sender, SizeChangedEventArgs args) => FitFrame();

    private void FitFrame()
    {
        if (FrameImage.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return;
        double scale = Math.Min(FrameViewport.ActualWidth / bitmap.PixelWidth, FrameViewport.ActualHeight / bitmap.PixelHeight);
        if (!double.IsFinite(scale) || scale < 0) return;
        FrameImage.Width = bitmap.PixelWidth * scale;
        FrameImage.Height = bitmap.PixelHeight * scale;
    }

    private void Previous_Click(object sender, RoutedEventArgs args) => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs args) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs args) => Close();
}

using Microsoft.UI.Xaml;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private PreviewWindow? detachedPreview;

    private void OpenPreviewWindow_Click(object sender, RoutedEventArgs args)
    {
        if (PreviewFrame.Source is null) return;
        if (detachedPreview is null)
        {
            var window = new PreviewWindow(WindowRoot.ActualTheme);
            window.PreviousRequested += (_, _) => PreviousPreview_Click(PreviousPreviewButton, new RoutedEventArgs());
            window.NextRequested += (_, _) => NextPreview_Click(NextPreviewButton, new RoutedEventArgs());
            window.Closed += (_, _) =>
            {
                if (detachedPreview == window) detachedPreview = null;
            };
            detachedPreview = window;
        }
        SyncDetachedPreview();
        detachedPreview.Activate();
    }

    private void SyncDetachedPreview()
    {
        OpenPreviewWindowButton.IsEnabled = PreviewFrame.Source is not null;
        detachedPreview?.SetFrame(PreviewFrame.Source, PreviewFrameCounterText.Text,
            PreviousPreviewButton.IsEnabled, NextPreviewButton.IsEnabled);
    }
}

using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

namespace HandBrake.WinUI;

public sealed partial class MainWindow : Window
{
    private bool isNarrow;
    private bool showPreviewOnNarrow;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
    }

    private void AppNavigation_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? page = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        HomePanel.Visibility = page == "workspace" ? Visibility.Visible : Visibility.Collapsed;
        QueuePanel.Visibility = page == "queue" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrowNow = e.NewSize.Width < 760;
        if (narrowNow != isNarrow)
        {
            isNarrow = narrowNow;
            showPreviewOnNarrow = false;
        }

        UpdatePreviewLayout();
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!isNarrow) return;
        showPreviewOnNarrow = !showPreviewOnNarrow;
        UpdatePreviewLayout();
    }

    private void UpdatePreviewLayout()
    {
        PreviewToggleButton.Visibility = isNarrow ? Visibility.Visible : Visibility.Collapsed;
        if (isNarrow)
        {
            EditorColumn.Width = showPreviewOnNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = showPreviewOnNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            EditorPanel.Visibility = showPreviewOnNarrow ? Visibility.Collapsed : Visibility.Visible;
            PreviewPanel.Visibility = showPreviewOnNarrow ? Visibility.Visible : Visibility.Collapsed;
            PreviewToggleButton.Content = showPreviewOnNarrow ? "Editor" : "Preview";
        }
        else
        {
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(350);
            EditorPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Visible;
        }
    }

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                FileTypeFilter = { ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".ts", ".mts" },
            };

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null) return;

            var info = new FileInfo(selectedFile.Path);
            SelectedFileText.Text = info.Name;
            SelectedPathText.Text = $"{info.Length / 1024d / 1024d:N1} MiB · {info.FullName}";
            PreviewSourceText.Text = info.Name;
            DestinationText.Text = Path.GetFileNameWithoutExtension(info.Name) + ".mp4 (suggested only)";
            SourceInfoBar.Title = "Source selected";
            SourceInfoBar.Message = "The file is selected. Actual source scanning and video preview are not connected yet.";
            SourceInfoBar.Severity = InfoBarSeverity.Informational;
        }
        catch (Exception error)
        {
            SourceInfoBar.Title = "Unable to open video";
            SourceInfoBar.Message = error.Message;
            SourceInfoBar.Severity = InfoBarSeverity.Error;
        }
    }
}
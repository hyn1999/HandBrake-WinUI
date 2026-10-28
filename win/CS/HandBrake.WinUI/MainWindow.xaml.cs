using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

namespace HandBrake.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
    }

    private void AppNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        string? page = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        HomePanel.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        QueuePanel.Visibility = page == "queue" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                FileTypeFilter =
                {
                    ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".ts", ".mts"
                },
            };

            var selectedFile = await picker.PickSingleFileAsync();
            if (selectedFile is null)
            {
                return;
            }

            var info = new FileInfo(selectedFile.Path);
            SelectedFileText.Text = info.Name;
            SelectedPathText.Text = $"{info.Length / 1024d / 1024d:N1} MiB · {info.FullName}";
            SourceInfoBar.Title = "Source selected";
            SourceInfoBar.Message = "The video was selected successfully. Media scanning and encoding will be connected in the next development stage.";
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

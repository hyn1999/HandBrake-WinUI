using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace HandBrake.WinUI;

public sealed partial class MainWindow : Window
{
    private bool isNarrow;
    private bool showPreviewOnNarrow;
    private readonly List<string> selectedSourcePaths = new();
    private Storyboard? sourcePickerAnimation;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        SystemBackdrop = new MicaBackdrop();
        SettingsPanel.HostWindow = this;
        SettingsPanel.PreferencesChanged += (_, _) => ApplyPreferences();
        ApplyPreferences();
        InitializeDraftQueue();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        InitializeSourceScanner();
        InitializeEncodeRunner();
    }

    private void AppNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        if (AppNavigation.XamlRoot is not null)
        {
            AppNavigation.XamlRoot.Changed += (_, _) => UpdateDpiStatus();
            UpdateDpiStatus();
            InitializePresetCatalog();
        }
    }

    private void UpdateDpiStatus()
    {
        if (AppNavigation.XamlRoot is not null)
        {
            double scale = AppNavigation.XamlRoot.RasterizationScale * 100;
            SettingsPanel.SetDpiScale(scale);
        }
    }

    private void AppNavigation_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? page = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        HomePanel.Visibility = page == "workspace" ? Visibility.Visible : Visibility.Collapsed;
        QueuePanel.Visibility = page == "queue" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        HideSourcePicker();
        UIElement? activePanel = page switch
        {
            "workspace" => HomePanel,
            "queue" => QueuePanel,
            "settings" => SettingsPanel,
            _ => null
        };
        if (activePanel is not null)
        {
            activePanel.Opacity = 0;
            AnimateOpacity(activePanel, 0, 1, 160);
        }
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
        if (!SettingsPanel.Preferences.ShowPreviewPanel)
        {
            PreviewToggleButton.Visibility = Visibility.Collapsed;
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(0);
            EditorPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

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

    private void ShowSourcePicker_Click(object sender, RoutedEventArgs e) => ShowSourcePicker();

    private void ShowSourcePicker()
    {
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        sourcePickerAnimation?.Stop();
        SourcePickerOverlay.Visibility = Visibility.Visible;
        SourcePickerOverlay.Opacity = 0;
        AnimateOpacity(SourcePickerOverlay, 0, 1, 180, storyboard => sourcePickerAnimation = storyboard);
    }

    private void CloseSourcePicker_Click(object sender, RoutedEventArgs e) => HideSourcePicker();

    private void HideSourcePicker()
    {
        if (SourcePickerOverlay.Visibility != Visibility.Visible) return;
        sourcePickerAnimation?.Stop();
        AnimateOpacity(SourcePickerOverlay, SourcePickerOverlay.Opacity, 0, 140, storyboard =>
        {
            sourcePickerAnimation = storyboard;
            storyboard.Completed += (_, _) =>
            {
                if (SourcePickerOverlay.Opacity <= 0.01)
                    SourcePickerOverlay.Visibility = Visibility.Collapsed;
            };
        });
    }

    private static void AnimateOpacity(UIElement target, double from, double to, int durationMs,
        Action<Storyboard>? configure = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        configure?.Invoke(storyboard);
        storyboard.Begin();
    }

    private async void OpenFiles_Click(object sender, RoutedEventArgs e) => await PickFilesAsync();

    private async void OpenFolder_Click(object sender, RoutedEventArgs e) => await PickFolderAsync();

    private async void OpenFiles_AcceleratorInvoked(KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await PickFilesAsync();
    }

    private async void OpenFolder_AcceleratorInvoked(KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await PickFolderAsync();
    }

    private async System.Threading.Tasks.Task PickFilesAsync()
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                FileTypeFilter =
                {
                    ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv",
                    ".ts", ".mts", ".m2ts", ".mpg", ".mpeg", ".vob", ".flv",
                    ".ogv", ".3gp", ".mxf", ".iso"
                }
            };

            var files = await picker.PickMultipleFilesAsync();
            if (files.Count > 0)
            {
                AcceptSourcePaths(files.Select(file => file.Path));
            }
        }
        catch (Exception error)
        {
            ShowSourceError(error);
        }
    }

    private async System.Threading.Tasks.Task PickFolderAsync()
    {
        try
        {
            var picker = new FolderPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                AcceptSourcePaths(new[] { folder.Path });
            }
        }
        catch (Exception error)
        {
            ShowSourceError(error);
        }
    }

    private void WindowRoot_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DropHighlight.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void WindowRoot_DragLeave(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
    }

    private async void WindowRoot_Drop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.Handled = true;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Where(item => item is StorageFile || item is StorageFolder)
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path));
            AcceptSourcePaths(paths);
        }
        catch (Exception error)
        {
            ShowSourceError(error);
        }
    }

    private void AcceptSourcePaths(IEnumerable<string> sourcePaths)
    {
        if (scanInProgress || encodeRunning)
        {
            SourceStatusText.Text = "A native scan or encode is already running. Stop it before opening a new source.";
            SourcePickerOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var paths = sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();

        if (paths.Count == 0)
        {
            SourceStatusText.Text = "No accessible files or folders were selected.";
            SourcePickerOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        selectedSourcePaths.Clear();
        selectedSourcePaths.AddRange(paths);
        selectedScannedTitleName = null;
        StageQueueButton.IsEnabled = true;

        string first = paths[0];
        bool isFolder = Directory.Exists(first);
        string firstName = Path.GetFileName(Path.TrimEndingDirectorySeparator(first));
        SelectedFileText.Text = paths.Count == 1
            ? firstName
            : $"{firstName} + {paths.Count - 1} more source(s)";
        SelectedPathText.Text = paths.Count == 1
            ? (isFolder ? first : $"{new FileInfo(first).Length / 1024d / 1024d:N1} MiB · {first}")
            : $"{paths.Count} selected files/folders · {first}";
        PreviewSourceText.Text = SelectedFileText.Text;
        UpdateSuggestedDestination();

        SourceStatusText.Text = paths.Count == 1
            ? "Source selected. Preparing HandBrake source scan..."
            : $"{paths.Count} sources selected. Queue and encoding are not connected yet.";

        SourcePickerOverlay.Visibility = Visibility.Collapsed;
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        StartSourceScan(paths);
    }

    private void ShowSourceError(Exception error)
    {
        SourceStatusText.Text = $"Unable to open source: {error.Message}";
        SourcePickerOverlay.Visibility = Visibility.Collapsed;
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
    }
}

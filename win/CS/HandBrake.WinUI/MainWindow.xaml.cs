using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;
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
        InitializePresetTracking();
        InitializeNativeDiagnostics();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        WindowRoot.ActualThemeChanged += (_, _) => ApplyTitleBarTheme();
        SystemBackdrop = new MicaBackdrop();
        ConfigureInitialWindowBounds();
        SettingsPanel.HostWindow = this;
        SettingsPanel.PreferencesChanged += (_, _) => ApplyPreferences();
        ApplyPreferences();
        ApplyTitleBarTheme();
        InitializeDraftQueue();
        AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
        InitializeSourceScanner();
        InitializeEncodeRunner();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;

    private void ConfigureInitialWindowBounds()
    {
        // Start with a balanced 16:10 workspace rather than a panoramic window.
        // Respect the available work area on smaller displays and high-DPI setups.
        try
        {
            DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            RectInt32 work = area.WorkArea;
            int width = Math.Min(1280, (int)(work.Width * 0.90));
            int height = Math.Min(800, (int)(work.Height * 0.90));
            if (width <= 0 || height <= 0) return;

            if (width / (double)height > 1.6)
                width = (int)(height * 1.6);
            else
                height = (int)(width / 1.6);

            AppWindow.MoveAndResize(new RectInt32(
                work.X + (work.Width - width) / 2,
                work.Y + (work.Height - height) / 2,
                width, height));
        }
        catch
        {
            AppWindow.Resize(new SizeInt32(1280, 800));
        }
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            bool dark = WindowRoot.ActualTheme == ElementTheme.Dark;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverBackgroundColor = dark
                ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            titleBar.ButtonPressedBackgroundColor = dark
                ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

            string iconPath = Path.Combine(AppContext.BaseDirectory, "HandBrake-WinUI.ico");
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Optional title-bar customization was not available: " + ex.Message);
        }
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
        if (isNarrow) showPreviewOnNarrow = !showPreviewOnNarrow;
        else previewHidden = !previewHidden;
        UpdatePreviewLayout();
    }

    private void UpdatePreviewLayout()
    {
        if ((!isNarrow && previewHidden) || !SettingsPanel.Preferences.ShowPreviewPanel)
        {
            PreviewToggleButton.Visibility = SettingsPanel.Preferences.ShowPreviewPanel ? Visibility.Visible : Visibility.Collapsed;
            PreviewToggleButton.Label = "Show preview";
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = new GridLength(0);
            EditorPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewToggleButton.Visibility = Visibility.Visible;
        if (isNarrow)
        {
            EditorColumn.Width = showPreviewOnNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            PreviewColumn.Width = showPreviewOnNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            EditorPanel.Visibility = showPreviewOnNarrow ? Visibility.Collapsed : Visibility.Visible;
            PreviewPanel.Visibility = showPreviewOnNarrow ? Visibility.Visible : Visibility.Collapsed;
            PreviewToggleButton.Label = showPreviewOnNarrow ? "Editor" : "Preview";
        }
        else
        {
            EditorColumn.Width = new GridLength(1, GridUnitType.Star);
            PreviewToggleButton.Label = "Hide preview";
            PreviewColumn.Width = new GridLength(0.8, GridUnitType.Star);
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
            Duration = new Duration(TimeSpan.FromMilliseconds(
                new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? durationMs : 0)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        configure?.Invoke(storyboard);
        storyboard.Begin();
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        RefreshOutputSummary();
        if (sender is TabView tabs && tabs.SelectedItem is TabViewItem item && item.Content is UIElement content)
            AnimateOpacity(content, 0, 1, 140);
    }

    private void OpenSource_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowSourcePicker();
    }

    private void StartEncode_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (StartEncodeButton.IsEnabled) StartEncode_Click(StartEncodeButton, new RoutedEventArgs());
    }

    private void StopEncode_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (CancelEncodeButton.IsEnabled) CancelEncode_Click(CancelEncodeButton, new RoutedEventArgs());
    }

    private void Queue_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AppNavigation.SelectedItem = AppNavigation.MenuItems[1];
    }

    private void StageQueue_AcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (StageQueueButton.IsEnabled) StageQueue_Click(StageQueueButton, new RoutedEventArgs());
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

        MetadataTitleBox.Text = MetadataArtistBox.Text = MetadataAlbumBox.Text = MetadataCommentBox.Text = string.Empty;
        destinationOverride = null;
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
            : $"{paths.Count} sources selected. Open and scan one source at a time to configure each encode job.";

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

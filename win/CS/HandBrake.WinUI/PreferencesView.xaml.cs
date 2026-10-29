using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;

namespace HandBrake.WinUI;

/// <summary>
/// Fluent, in-app preferences surface. Only supported settings are interactive.
/// Native encoder, queue completion and auto-update actions remain disabled.
/// </summary>
public sealed partial class PreferencesView : UserControl
{
    private bool ready;
    public Window? HostWindow { get; set; }
    public AppPreferences Preferences { get; private set; } = AppPreferences.Load();
    public event EventHandler? PreferencesChanged;

    public PreferencesView()
    {
        InitializeComponent();
        LoadControls();
        CategoryList.SelectedIndex = 0;
        ready = true;
    }

    public void SetDpiScale(double scale)
    {
        string label = $"WinUI rendering scale: {scale:0}% (Windows display scaling)";
        DpiStatusText.Text = label;
        AboutDpiText.Text = label;
    }

    private void LoadControls()
    {
        ready = false;
        ThemePicker.SelectedIndex = Preferences.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        ShowPreviewToggle.IsOn = Preferences.ShowPreviewPanel;
        OutputFolderText.Text = string.IsNullOrWhiteSpace(Preferences.DefaultOutputFolder)
            ? "Use the source folder (no default folder selected)"
            : Preferences.DefaultOutputFolder;
        ReplaceUnderscoresToggle.IsOn = Preferences.ReplaceUnderscoresInFileNames;
        ExtensionPicker.SelectedIndex = Preferences.Mp4Extension == ".m4v" ? 1 : 0;
        ScanPreviewCountBox.Value = Preferences.ScanPreviewCount;
        MinimumDurationBox.Value = Preferences.MinTitleDurationSeconds;
        KeepDuplicateTitlesToggle.IsOn = Preferences.KeepDuplicateTitles;
        ScanVerbosityPicker.SelectedIndex = Preferences.ScanVerbosity;
        ready = true;
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedIndex >= 0)
            ShowCategory(CategoryList.SelectedIndex);
    }

    private void CompactCategoryPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompactCategoryPicker.SelectedIndex >= 0)
            ShowCategory(CompactCategoryPicker.SelectedIndex);
    }

    private void ShowCategory(int index)
    {
        UIElement[] pages =
        {
            GeneralPage, OutputPage, WhenDonePage, VideoPage,
            AdvancedPage, UpdatesPage, ResetPage, AboutPage
        };

        if (index < 0 || index >= pages.Length) return;
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        if (CategoryList.SelectedIndex != index)
            CategoryList.SelectedIndex = index;
        if (CompactCategoryPicker.SelectedIndex != index)
            CompactCategoryPicker.SelectedIndex = index;
    }

    private void PreferencesView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 670;
        CategoryColumn.Width = new GridLength(compact ? 0 : 184);
        CategorySidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactCategoryPicker.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnyToggle_Toggled(object sender, RoutedEventArgs e) => SaveFromControls();
    private void AnyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => SaveFromControls();
    private void ScanNumber_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => SaveFromControls();

    private static int ReadNumber(NumberBox box, int fallback, int min, int max)
    {
        double value = box.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;
        return Math.Clamp((int)value, min, max);
    }

    private void SaveFromControls()
    {
        if (!ready) return;

        Preferences.Theme = ThemePicker.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
        Preferences.ShowPreviewPanel = ShowPreviewToggle.IsOn;
        Preferences.ReplaceUnderscoresInFileNames = ReplaceUnderscoresToggle.IsOn;
        Preferences.Mp4Extension = ExtensionPicker.SelectedIndex == 1 ? ".m4v" : ".mp4";
        Preferences.ScanPreviewCount = ReadNumber(ScanPreviewCountBox, 10, 1, 60);
        Preferences.MinTitleDurationSeconds = ReadNumber(MinimumDurationBox, 0, 0, 3600);
        Preferences.KeepDuplicateTitles = KeepDuplicateTitlesToggle.IsOn;
        Preferences.ScanVerbosity = Math.Clamp(ScanVerbosityPicker.SelectedIndex, 0, 2);
        PersistAndNotify();
    }

    private void PersistAndNotify()
    {
        try
        {
            Preferences.Save();
            PreferencesStatusText.Text = "Preferences saved automatically.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PreferencesStatusText.Text = "Unable to save preferences: " + ex.Message;
        }

        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        if (HostWindow is null)
        {
            PreferencesStatusText.Text = "Cannot open the folder picker before the window is ready.";
            return;
        }

        try
        {
            var picker = new FolderPicker(HostWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            Preferences.DefaultOutputFolder = folder.Path;
            OutputFolderText.Text = folder.Path;
            PersistAndNotify();
        }
        catch (Exception ex)
        {
            PreferencesStatusText.Text = "Unable to choose output folder: " + ex.Message;
        }
    }

    private void ClearOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        Preferences.DefaultOutputFolder = string.Empty;
        OutputFolderText.Text = "Use the source folder (no default folder selected)";
        PersistAndNotify();
    }

    private async void ResetPreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore WinUI preferences?",
            Content = "This resets only the new WinUI frontend preferences. Original HandBrake WPF settings and source files are not affected.",
            PrimaryButtonText = "Restore defaults",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Preferences = new AppPreferences();
        LoadControls();
        PersistAndNotify();
        PreferencesStatusText.Text = "WinUI preferences restored to defaults.";
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI;

public sealed partial class MainWindow
{
    private void SourceBrowseActions_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool stacked = args.NewSize.Width < 520;
        SourceBrowseSecondColumn.Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SourceBrowseSecondRow.Height = stacked ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(SourceBrowseFolderButton, stacked ? 0 : 1);
        Grid.SetRow(SourceBrowseFolderButton, stacked ? 1 : 0);
        SourceBrowseActions.ColumnSpacing = stacked ? 0 : 12;
        SourceBrowseActions.RowSpacing = stacked ? 12 : 0;
    }

    private void ClearSourceDropFeedback()
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        SourceDropFeedback.Visibility = Visibility.Collapsed;
    }
}

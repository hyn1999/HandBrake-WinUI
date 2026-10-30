using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI.Design;

/// <summary>
/// Shared UI recipes for programmatically created controls. Size values live
/// only in CompactMetrics.xaml; styles and native templates live in XAML.
/// </summary>
public static partial class CompactUi
{
    public static T Resource<T>(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is T typed) return typed;
        throw new InvalidOperationException($"Missing or invalid UI resource: {key}");
    }

    public static double ControlHeight => Resource<double>("HBControlMinHeight");
    public static double RowGap => Resource<double>("HBSpaceRow");
    public static double GroupGap => Resource<double>("HBSpaceGroup");
    public static double ControlGap => Resource<double>("HBControlGap");
    public static double LabelGap => Resource<double>("HBLabelInputGap");

    public static Viewbox Icon(Symbol symbol) => new()
    {
        Width = Resource<double>("HBIconSize"), Height = Resource<double>("HBIconSize"),
        Child = new SymbolIcon(symbol), VerticalAlignment = VerticalAlignment.Center
    };

    public static Button Command(string label, Symbol symbol, bool primary = false)
        => CreateCommand(label, symbol, primary, main: false);

    public static Button MainCommand(string label, Symbol symbol, bool primary = false)
        => CreateCommand(label, symbol, primary, main: true);

    private static Button CreateCommand(string label, Symbol symbol, bool primary, bool main)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Resource<double>(main ? "HBMainCommandIconTextGap" : "HBIconTextGap")
        };
        content.Children.Add(Icon(symbol));
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        if (main) text.Style = Resource<Style>("HBMainCommandTextStyle");
        content.Children.Add(text);
        var button = new Button
        {
            Content = content,
            Style = Resource<Style>(main
                ? primary ? "HBMainCommandAccentButtonStyle" : "HBMainCommandButtonStyle"
                : primary ? "HBCompactAccentButtonStyle" : "HBCompactButtonStyle")
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    public static Button IconCommand(Symbol symbol, string accessibleName)
    {
        if (string.IsNullOrWhiteSpace(accessibleName)) throw new ArgumentException("An icon command needs an accessible name.", nameof(accessibleName));
        var button = new Button { Content = Icon(symbol), Style = Resource<Style>("HBCompactIconButtonStyle") };
        AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, accessibleName);
        return button;
    }

    public static Border Card(UIElement content) => new()
    {
        Child = content, Style = Resource<Style>("HBCompactCardStyle")
    };

    public static T MenuItem<T>(T item) where T : MenuFlyoutItemBase
    {
        item.MinHeight = ControlHeight;
        item.Padding = Resource<Thickness>("HBMenuItemPadding");
        item.FontSize = Resource<double>("HBBodyFontSize");
        return item;
    }

    public static ToolTip Help(string text) => new()
    {
        Content = new TextBlock
        {
            Text = text, FontSize = Resource<double>("HBTooltipFontSize"),
            MaxWidth = Resource<double>("HBTooltipMaxWidth"), TextWrapping = TextWrapping.Wrap
        }
    };

    public static ComboBoxItem PresetItem(string caption, object value) => new()
    {
        Content = caption, Tag = value, Style = Resource<Style>("HBCompactPresetItemStyle")
    };

    public static ComboBoxItem PresetCategory(string caption) => new()
    {
        Content = caption, IsEnabled = false, IsTabStop = false,
        Style = Resource<Style>("HBCompactPresetCategoryStyle")
    };

    public static void SizeComboBoxPopup(ComboBox picker, IEnumerable<ComboBoxItem> items)
    {
        if (picker.XamlRoot is not { } root) return;
        picker.MaxDropDownHeight = Math.Max(0, root.Size.Height * Resource<double>("HBPopupHeightFraction"));
        // Bound item measure without replacing/accessing private popup parts.
        // Leave room for native popup padding and its vertical scrollbar.
        double popupWidth = Math.Min(Resource<double>("HBPopupPreferredWidth"), Resource<double>("HBPopupMaxWidth"));
        double bodyWidth = Math.Max(0, Math.Min(popupWidth,
            Math.Max(0, root.Size.Width - 2 * GroupGap)) - 2 * GroupGap);
        foreach (var item in items)
        {
            item.MinWidth = 0;
            item.Width = bodyWidth;
            item.MaxWidth = bodyWidth;
        }
    }

    /// <summary>Size a future picker presenter; native popup placement/scrolling stays native.</summary>
    public static void SizePickerPopup(FrameworkElement anchor, FrameworkElement presenter)
    {
        if (anchor.XamlRoot is not { } root) return;
        double width = Math.Max(0, root.Size.Width - 2 * GroupGap);
        presenter.MaxWidth = Math.Min(Resource<double>("HBPopupMaxWidth"), width);
        presenter.Width = Math.Min(Resource<double>("HBPopupPreferredWidth"), presenter.MaxWidth);
        presenter.MaxHeight = Math.Max(0, root.Size.Height * Resource<double>("HBPopupHeightFraction"));
    }

    /// <summary>Responsive settings rows. Field widths/alignment remain layout concerns.</summary>
    public static Grid FormRow(params FrameworkElement[] fields)
    {
        var grid = new Grid
        {
            ColumnSpacing = ControlGap, RowSpacing = RowGap,
            Margin = Resource<Thickness>("HBFormRowMargin")
        };
        foreach (var field in fields)
        {
            field.VerticalAlignment = VerticalAlignment.Bottom;
            grid.Children.Add(field);
        }
        int previousColumns = -1;
        grid.SizeChanged += (_, args) =>
        {
            if (fields.Length == 0) return;
            int columns = args.NewSize.Width >= Resource<double>("HBFormWideBreakpoint") ? fields.Length :
                args.NewSize.Width >= Resource<double>("HBFormTwoColumnBreakpoint") ? Math.Min(2, fields.Length) : 1;
            if (columns == previousColumns) return;
            previousColumns = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int row = 0; row < (fields.Length + columns - 1) / columns; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < fields.Length; i++) { Grid.SetColumn(fields[i], i % columns); Grid.SetRow(fields[i], i / columns); }
        };
        return grid;
    }

    /// <summary>Bounded-width groups for headers and command strips, without star-column gaps.</summary>
    public static void WrapFields(StackPanel host, List<(FrameworkElement Element, double Width)> fields,
        double availableWidth, ref string? previousLayout, double? gap = null)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return;
        double spacing = gap ?? ControlGap;
        var rows = new List<List<FrameworkElement>>();
        var row = new List<FrameworkElement>();
        double used = 0;
        var signature = new List<string>();
        for (int index = 0; index < fields.Count; index++)
        {
            var (element, desiredWidth) = fields[index];
            if (element.Visibility == Visibility.Collapsed) continue;
            double width = Math.Min(desiredWidth, availableWidth);
            element.Width = width;
            element.HorizontalAlignment = HorizontalAlignment.Left;
            element.VerticalAlignment = VerticalAlignment.Top;
            if (row.Count > 0 && used + spacing + width > availableWidth)
            {
                rows.Add(row); row = new(); used = 0; signature.Add("/");
            }
            if (row.Count > 0) used += spacing;
            row.Add(element); used += width; signature.Add(index.ToString());
        }
        if (row.Count > 0) rows.Add(row);
        string layout = string.Join(",", signature);
        if (layout == previousLayout) return;
        previousLayout = layout;
        foreach (var oldRow in host.Children.OfType<StackPanel>().ToList()) oldRow.Children.Clear();
        host.Children.Clear();
        foreach (var elements in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
            foreach (var element in elements) line.Children.Add(element);
            host.Children.Add(line);
        }
    }
}

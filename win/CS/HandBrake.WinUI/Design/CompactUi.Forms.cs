using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HandBrake.WinUI.Design;

public sealed record CompactFormField(string Label, FrameworkElement Input, FieldWidthRole Role);

public static partial class CompactUi
{
    /// <summary>
    /// New forms use independent shared label/body rows. This avoids centering
    /// a headerless checkbox against the entire header+input of a ComboBox.
    /// Inputs must have no native Header; this method supplies accessible labels.
    /// </summary>
    public static Grid AlignedFormRow(params CompactFormField[] fields)
    {
        var host = new Grid { RowSpacing = RowGap, Margin = Resource<Thickness>("HBFormRowMargin") };
        if (fields.Length == 0) return host;
        var labels = new List<TextBlock>();
        foreach (var field in fields)
        {
            object? header = field.Input switch
            {
                ComboBox combo => combo.Header, TextBox text => text.Header,
                NumberBox number => number.Header, ToggleSwitch toggle => toggle.Header,
                _ => null
            };
            if (header is not null && header is not "")
                throw new ArgumentException("Use the field label instead of a native Header in AlignedFormRow.", nameof(fields));
            FieldLayout.SetRole(field.Input, field.Role);
            field.Input.VerticalAlignment = VerticalAlignment.Center;
            var label = new TextBlock
            {
                Text = field.Label, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, LabelGap),
                Visibility = string.IsNullOrWhiteSpace(field.Label) ? Visibility.Collapsed : Visibility.Visible
            };
            labels.Add(label);
            if (label.Visibility == Visibility.Visible) AutomationProperties.SetLabeledBy(field.Input, label);
        }

        string? previous = null;
        var columns = new List<(ColumnDefinition Column, int Index)>();
        void Relayout()
        {
            double available = host.ActualWidth;
            if (!double.IsFinite(available) || available <= 0) return;
            var widths = new double[fields.Length];
            var groups = new List<List<int>>();
            var group = new List<int>();
            double used = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Input.Visibility == Visibility.Collapsed) continue;
                double preferred = FieldLayout.PreferredWidth(fields[i].Role);
                double width = double.IsFinite(preferred) ? Math.Min(preferred, available) : available;
                if (fields[i].Role == FieldWidthRole.Path && group.Count > 0)
                {
                    double remaining = available - used - ControlGap;
                    if (remaining >= FieldLayout.PreferredWidth(FieldWidthRole.Number)) width = remaining;
                }
                if (group.Count > 0 && used + ControlGap + width > available)
                { groups.Add(group); group = new(); used = 0; }
                if (group.Count > 0) used += ControlGap;
                widths[i] = width; used += width; group.Add(i);
            }
            if (group.Count > 0) groups.Add(group);
            string signature = string.Join("/", groups.Select(indices => string.Join(",", indices)));
            if (signature == previous)
            {
                foreach (var item in columns) item.Column.Width = new GridLength(widths[item.Index]);
                return;
            }
            previous = signature;
            foreach (var old in host.Children.OfType<Grid>()) old.Children.Clear();
            host.Children.Clear(); host.RowDefinitions.Clear(); columns.Clear();
            for (int rowIndex = 0; rowIndex < groups.Count; rowIndex++)
            {
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var row = new Grid { ColumnSpacing = ControlGap, HorizontalAlignment = HorizontalAlignment.Left };
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int col = 0; col < groups[rowIndex].Count; col++)
                {
                    int i = groups[rowIndex][col];
                    var definition = new ColumnDefinition { Width = new GridLength(widths[i]) };
                    row.ColumnDefinitions.Add(definition); columns.Add((definition, i));
                    Grid.SetColumn(labels[i], col); Grid.SetRow(labels[i], 0);
                    Grid.SetColumn(fields[i].Input, col); Grid.SetRow(fields[i].Input, 1);
                    row.Children.Add(labels[i]); row.Children.Add(fields[i].Input);
                }
                Grid.SetRow(row, rowIndex); host.Children.Add(row);
            }
        }
        host.SizeChanged += (_, _) => Relayout();
        var visibilityTokens = new List<(FrameworkElement Input, long Token)>();
        host.Loaded += (_, _) =>
        {
            foreach (var field in fields)
                visibilityTokens.Add((field.Input, field.Input.RegisterPropertyChangedCallback(
                    UIElement.VisibilityProperty, (_, _) => Relayout())));
            Relayout();
        };
        host.Unloaded += (_, _) =>
        {
            foreach (var entry in visibilityTokens)
                entry.Input.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, entry.Token);
            visibilityTokens.Clear();
        };
        return host;
    }
}

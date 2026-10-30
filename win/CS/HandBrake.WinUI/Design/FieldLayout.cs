using System;
using Microsoft.UI.Xaml;

namespace HandBrake.WinUI.Design;

public enum FieldWidthRole { None, ShortNumber, Number, ShortChoice, NamedChoice, Name, Preset, Path }

/// <summary>Content roles own width policy; density styles never set a global field width.</summary>
public sealed class FieldLayout : DependencyObject
{
    private FieldLayout() { }
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(FieldWidthRole), typeof(FieldLayout),
        new PropertyMetadata(FieldWidthRole.None, RoleChanged));

    public static FieldWidthRole GetRole(DependencyObject element) => (FieldWidthRole)element.GetValue(RoleProperty);
    public static void SetRole(DependencyObject element, FieldWidthRole role) => element.SetValue(RoleProperty, role);

    public static double PreferredWidth(FieldWidthRole role) => role switch
    {
        FieldWidthRole.ShortNumber => CompactUi.Resource<double>("HBShortNumberWidth"),
        FieldWidthRole.Number => CompactUi.Resource<double>("HBNumberWidth"),
        FieldWidthRole.ShortChoice => CompactUi.Resource<double>("HBShortChoiceWidth"),
        FieldWidthRole.NamedChoice => CompactUi.Resource<double>("HBNamedChoiceWidth"),
        FieldWidthRole.Name => CompactUi.Resource<double>("HBNameWidth"),
        FieldWidthRole.Preset => CompactUi.Resource<double>("HBPresetWidth"),
        FieldWidthRole.None or FieldWidthRole.Path => double.PositiveInfinity,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private static void RoleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement field) return;
        var role = (FieldWidthRole)args.NewValue;
        if (role == FieldWidthRole.None)
        {
            field.ClearValue(FrameworkElement.MaxWidthProperty);
            field.ClearValue(FrameworkElement.MinWidthProperty);
            field.ClearValue(FrameworkElement.WidthProperty);
            field.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
            return;
        }
        field.Width = double.NaN;
        field.MinWidth = 0; // Recommendations must not prevent narrow-window shrinking.
        field.MaxWidth = role == FieldWidthRole.Preset
            ? CompactUi.Resource<double>("HBPresetMaxWidth") : PreferredWidth(role);
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}

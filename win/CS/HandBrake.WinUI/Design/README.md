# Shared Compact UI

This is the app's internal presentation library. It adds no NuGet dependency or separate assembly. Do not put encoding rules or task state here.

## Decision

Use Microsoft's native Compact dictionary as the baseline, then application tokens and derived styles. App.xaml merges XamlControlsResources, native DensityStyles/Compact.xaml, then CompactStyles.xaml (which imports CompactMetrics.xaml). Never replace native control templates to make the UI smaller.

The goal is coherent desktop density, not scaling the whole application or forcing every container to the same height. Native header labels, multiline inputs, switches and interaction targets need their own space. No fixed-height text inputs or reduced system font scaling are introduced.

| Role | Policy |
| --- | --- |
| Button, accent command, DropDownButton | 28 effective-pixel minimum, 14 text, shared padding; native corners and states |
| ComboBox, TextBox | 28 minimum input body, 14 text; headers add their native height |
| NumberBox | Native template retained; shared TextControlThemeMinHeight/Padding resources size its inner TextBox |
| CheckBox / RadioButton | 32 minimum row; preserve native indicator and RadioButton's internal 32-pixel layout |
| Icon command | 28 square with 16 icon; accessible name required |
| List items / navigation | Native Compact baseline (32 where provided) |
| Slider, ToggleSwitch, AppBarButton / CommandBar | Keep native geometry/states; Compact font/resources where consumed; do not squeeze native interaction parts |
| Settings card | Shared padding, radius and theme brushes |
| Add media browse action | Dedicated 68 minimum, 20 icon; deliberately larger two-line action |
| Drag/drop area and title/caption buttons | Keep their functional/platform dimensions; not small-input controls |

28/32 are initial design values awaiting Windows review, not a claim that every runtime control measures exactly that height. A minimum can grow for content and accessibility. Touch-oriented or unusually dense views should be reviewed separately before any template change.

## Authoring XAML

Common Button/ComboBox/TextBox/CheckBox/RadioButton/DropDownButton styles apply implicitly throughout Workspace, Queue, Preferences and Preview, including controls created in C#. Do not repeat MinHeight, Padding, FontSize or CornerRadius on each new control.

```xml
<Button Content="Reload" Click="Reload_Click" />
<Button Content="Start Encode" Style="{StaticResource HBCompactAccentButtonStyle}" />
<ComboBox Header="Encoder" />
<NumberBox Header="Bitrate" />
<TextBox Header="Track name" />
<TextBlock Text="Audio" Style="{StaticResource HBCompactSectionTitleStyle}" />
<Border Style="{StaticResource HBCompactCardStyle}">
    <StackPanel Spacing="{StaticResource HBSpaceRow}">
        <!-- controls -->
    </StackPanel>
</Border>
```

Icon-only buttons use HBCompactIconButtonStyle and must provide AutomationProperties.Name and an appropriate tooltip. Use HBMediaSourceButtonStyle only for the larger source-selection action, not ordinary commands.

Use HBSpaceTight / HBSpaceRow / HBSpaceGroup for spacing. Edit CompactMetrics.xaml to tune density; edit CompactStyles.xaml for reusable semantic roles. Do not override framework colours or radii with fixed colours. Styles inherit native defaults, including AccentButtonStyle, so Light/Dark/HighContrast and focus/pressed/disabled states remain the framework's responsibility.

## Authoring C#

```csharp
using HandBrake.WinUI.Design;

var start = CompactUi.Command("Start Encode", Symbol.Play, primary: true);
var expand = CompactUi.IconCommand(Symbol.FullScreen, "Open preview");
var row = CompactUi.FormRow(encoderPicker, bitrateBox, trackNameBox);
var card = CompactUi.Card(row);
var reload = CompactUi.MenuItem(new MenuFlyoutItem { Text = "Reload" });
```

Wire events and provide localized/product-approved labels at the call site. Helpers centralize appearance and composition, not behavior or tooltip copy. Set values such as minimum/maximum bitrate at the feature boundary. Widths, column count, wrapping and visibility remain layout concerns, not density setters.

FormRow preserves the existing responsive equal-column editor recipe. Use WrapFields for bounded-width groups such as the source header; do not stretch small controls across an entire window. MainWindow.CompactRow is now only a compatibility forwarding method. Source-header wrapping now also delegates to this library.

## Migration boundary

Removed the conflicting HomePanel/source-header implicit density styles. MainWindow spacing/text/icon roles, preview icon buttons, preset-menu metrics and Preferences card styling now use shared resources. Existing per-feature layout sizes/card paddings not migrated here remain candidates for their visual review; this is not a claim that the complete application layout has been redesigned.

The grouped single-popup preset picker and inline Default/Modified labels are recorded in doc/winui-preset-picker-plan.md. Its old menu structure is intentionally pending while this foundation is reviewed.

## Sources and constraints

- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing
- https://learn.microsoft.com/windows/apps/design/style/xaml-styles
- Microsoft UI XAML source inspected at 6583084e128aca2ac5aed38bd5521029e803ad66: controls/dev/dll/DensityStyles/Compact.xaml; CommonStyles Button/TextBox/CheckBox/RadioButton resources; ComboBox resources; DropDownButton.xaml; NumberBox.xaml.

The inspected NumberBox source supplies an implicit native style, not a named DefaultNumberBoxStyle. Do not invent that resource key: the numeric-input density is applied through the native TextBox sizing resources instead. Microsoft main is implementation evidence, not a substitute for validating the app's pinned Windows App SDK 2.5.1 package.

## Verification status

Performed: XAML XML/resource-reference checks, helper call-site review, native style-key/source inspection and checks that no replacement ControlTemplate or unsupported SymbolIcon.FontSize was added. No build, Windows runtime/UI inspection or full accessibility verification was performed.

Next aggregate Windows build: compare input bodies including NumberBox inline spin buttons; inspect Light/Dark/HighContrast, 100/150/200% display scale and enlarged text, keyboard focus/tab order, hover/pressed/disabled states, open popups, narrow header wrapping, Queue edit controls, Preferences and detached Preview. Inspect rather than assume uniformity: native templates may impose additional content requirements.

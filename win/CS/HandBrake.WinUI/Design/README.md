# Shared Compact UI — agreed profile

This internal presentation library adds no package or separate assembly. Keep engine/task logic outside Design. The values below implement the user's size, spacing and width review; colour, font family/weight, corners, focus and interaction states remain native WinUI.

## Resource order and scope

App.xaml loads XamlControlsResources, Microsoft's DensityStyles/Compact.xaml, then CompactStyles.xaml. The latter imports CompactMetrics.xaml. Common controls inherit application Compact styles, including controls created in C#. Native text/number input resources size NumberBox's inner TextBox; no fictional DefaultNumberBoxStyle or replacement templates are used.

StandardCommands.xaml is an explicit exception: merge it ONLY inside a main command strip or CommandBar. It restores native body typography and default button styles within that scope. Never merge it at application scope. HBMainCommandButtonStyle / HBMainCommandAccentButtonStyle inherit native defaults, have no Compact MinHeight/Padding setters, and use the native BodyTextBlockFontSize resource. Their text style inherits native BodyTextBlockStyle. Main commands therefore remain naturally larger than compact settings without hard-coded enlarged fonts, icons or weights.

## Agreed size and spacing tokens

| Role | Profile |
| --- | --- |
| Settings buttons, fields, labels, menu/list text, source/job names | 13 base text |
| Secondary text, status and tooltips | 12 |
| Section headings | 15; inherit native SubtitleTextBlockStyle weight/family |
| Page headings | 20; inherit native TitleTextBlockStyle weight/family |
| Main command strip | Native typography, padding and geometry; exempt from Compact |
| Compact input/button body | 28 minimum, not a fixed text-control height |
| CheckBox / RadioButton | 32 minimum row; retain native indicator geometry |
| Icon button / icon | 28 / 16; accessible name required |
| Larger Add media action / icon | 68 minimum / 20 |
| Icon-to-text gap in compact compound buttons | 6 |
| Label-to-input gap | 4 |
| Related controls / rows | 8 |
| Independent groups / cards | 12 |
| Card padding | 12 |
| Main command icon-to-text / between-command gap | Separate 8 / 12 tokens |

Minimums grow with content and Windows text scaling. Native sliders, switches, CommandBars and their interaction parts retain their native geometry. Drag/drop space and system caption buttons are not compact form inputs. Card radius aliases the native OverlayCornerRadius; other corners use native ControlCornerRadius. Do not introduce density-specific FontWeight/FontFamily setters or fixed colours.

## Authoring XAML

Add xmlns:design="using:HandBrake.WinUI.Design" for width roles.

```xml
<Button Content="Reload" />
<Button Content="Apply" Style="{StaticResource HBCompactAccentButtonStyle}" />
<ComboBox Header="Encoder" design:FieldLayout.Role="NamedChoice" />
<NumberBox Header="Bitrate" design:FieldLayout.Role="Number" />
<TextBox Header="Track name" design:FieldLayout.Role="Name" />
<TextBlock Text="Audio" Style="{StaticResource HBCompactSectionTitleStyle}" />
<Border Style="{StaticResource HBCompactCardStyle}">
    <StackPanel Spacing="{StaticResource HBSpaceRow}">
        <!-- content -->
    </StackPanel>
</Border>
```

Width roles cap fields without forcing minimum widths that break narrow layouts. Their parent still owns alignment and space allocation: use bounded columns or AlignedFormRow rather than spreading small fields across star columns. Role changes own Width/MinWidth/MaxWidth/HorizontalAlignment; do not combine them with conflicting local width setters. Path fills available space; Preset has a 360 preference and 400 cap. None clears the role's sizing back to defaults.

| FieldWidthRole | Preferred width (effective pixels) |
| --- | ---: |
| ShortNumber | 96 |
| Number | 144 |
| ShortChoice | 180 |
| NamedChoice | 280 |
| Name | 360 |
| Preset | 360 (maximum 400) |
| Path | Remaining space |

These are initial preferences within the agreed ranges, not minimums or a guarantee that every field is always that width. Values remain unchanged when names/paths are visually truncated; expose full values through approved tooltips where needed.

For the main Workspace command strip, use StandardCommands.xaml in the strip's resources and HBMainCommandAccentButtonStyle for its primary action. This also prevents the application's implicit compact TextBlock style leaking into command labels. Existing Queue CommandBars use the same scoped dictionary and retain native overflow behavior.

## Authoring C#

```csharp
using HandBrake.WinUI.Design;

var reload = CompactUi.Command("Reload", Symbol.Refresh); // Compact card action
var start = CompactUi.MainCommand("Start Encode", Symbol.Play, primary: true);
var expand = CompactUi.IconCommand(Symbol.FullScreen, "Open preview");
var name = new TextBox();
FieldLayout.SetRole(name, FieldWidthRole.Name);
var row = CompactUi.AlignedFormRow(
    new CompactFormField("Encoder", new ComboBox(), FieldWidthRole.NamedChoice),
    new CompactFormField("Bitrate", new NumberBox(), FieldWidthRole.Number),
    new CompactFormField("", new CheckBox { Content = "Enabled" }, FieldWidthRole.ShortChoice));
var card = CompactUi.Card(row);
var help = CompactUi.Help("Product-approved description");
```

AlignedFormRow supplies shared label/body rows and AutomationProperties.LabeledBy. Pass inputs with no native Header so labels are not duplicated. Labels wrap and body rows grow; controls align to the input body, not the entire header+input container. Width groups wrap together. It observes visibility changes and removes those callbacks on unload. Events and valid encoder settings remain the feature's responsibility.

FormRow is the compatibility recipe used by existing editors: its fields now bottom-align, with 8-pixel gaps. It does not extract/rewrite native headers or promise exact cross-control baselines. Migrate existing forms to AlignedFormRow during their visual review. WrapFields handles bounded header groups; main command rows pass their separate standard gap.

## Popups, lists and tabs

MenuItem applies shared 13 text, minimum 28 and padding, retaining native interaction states. Multi-line Queue items may grow; do not force them to 28. Help creates wrapped 12-point tooltip content so a nested TextBlock cannot accidentally inherit the application's 13-point body style.

SizePickerPopup(anchor, presenter) supplies a preferred 360, maximum 420 width, limited by the root viewport, and a maximum height of 60% of the root height. Call it when opening/resizing a future picker; the native popup owns placement and scrolling. This helper does not create a picker or implement keyboard navigation itself.

HBTabPreferredHeight = 32 is a design target only. Native TabView geometry/selection/focus/scrolling are not replaced or forcibly clipped to that value. Compact base text is 13; review any per-view/template font overrides in Windows. Tabs stay in a single row with native horizontal navigation when required.

## Migration and pending work

The shared profile is active application-wide; source header widths, its tooltip content, Workspace/Queue standard command scopes, page title sizes and preset tooltip/menu metrics are wired to the new roles. Feature-specific legacy inline font sizes, card layouts and field headers still need their section-by-section review; changing tokens is not a full UI redesign.

The native single-list ComboBox preset picker now uses PresetItem/PresetCategory recipes and shared name/category content templates. SizeComboBoxPopup applies the shared height limit and bounded item measure using public APIs, without modifying private popup parts. Default/Modified captions and inline Reload are implemented; native keyboard/scrolling and exact outer popup bounds need Windows acceptance. Save New Preset remains a separate functional gap under doc/winui-preset-picker-plan.md.

## Sources and verification

Microsoft Compact/style guidance:
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing
- https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-styles

Native implementation evidence: microsoft/microsoft-ui-xaml at 6583084e128aca2ac5aed38bd5521029e803ad66, including Button, TextBlock, ToolTip, TextBox, ComboBox and NumberBox resources. BodyTextBlockFontSize and native body/heading styles are consumed rather than inventing a command-font baseline. The reference source is not a substitute for checking the pinned Windows App SDK 2.5.1 package.

Static XML/resource/handler checks and source review are performed separately from compilation. No Windows build or runtime visual/accessibility verification is claimed. For the next aggregate build, verify input heights/headers/spin buttons, main command isolation, native weight/corners/colours, all range modes, narrow windows, 100/150/200% DPI, enlarged text, focus/tab order, High Contrast, popup scrolling and queue editing. Exact uniformity must be measured on Windows.

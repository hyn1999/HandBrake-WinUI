# Workspace source header revision

Scope: the area above the settings and Preview cards only. No encode behavior, card contents, destination validation or build workflow is changed.

## Layout

- Use standard WinUI Button controls with icon/text for Open Source, Add to Queue (Save changes during edit), Cancel edit and Start Encode. Start uses AccentButtonStyle; header controls explicitly use the theme ControlCornerRadius. No custom control templates are introduced.
- Keep commands left-aligned with the source identity and the lower card edges. Use 16-pixel icons, 32-pixel minimum button height, and consistent spacing.
- Put filename and truncated path in their own compact row. Keep the full path in its existing tooltip.
- Group Title, Angle, Range, optional Start/End and Duration together, with bounded widths instead of equal star columns spread across the page. Wrap whole groups at available-width boundaries. Keep Start/End together and retain control parents within a breakpoint to avoid unnecessary reparenting while resizing.
- Keep actual scan/edit/error state visible. Remove the initial instructional paragraph, shorten scan/edit status, and remove the long Save/Cancel explanation. Expose Save changes and Cancel edit as commands during a restored queue edit.

## WPF text provenance

Reference commit: 112b214569449daf810c521bc6a1a415f4cfe09b.

- Button captions and shortcut tooltip content: HandBrakeWPF/Properties/Resources.resx keys MainView_SourceOpen, MainView_AddToQueue, MainView_StartEncode; MainView.xaml toolbar tooltip definitions (Alt+O, Alt+A, Ctrl+E).
- Control descriptions copied verbatim from HandBrakeWPF/Properties/ResourcesTooltips.resx: MainView_Title, MainView_Angle, MainView_Range, MainView_StartPoint, MainView_EndPoint, MainView_Duration.
- Bind these descriptions only to their relevant controls, rather than showing paragraphs in the header or attaching a tooltip to the page root. Save changes / Cancel edit are labels for the WinUI edit transaction introduced earlier, not invented WPF help text.

This revision covers header descriptions only; explanatory text inside the lower cards is unchanged pending their review. It does not claim to diagnose the origin of every square corner elsewhere in the application.

## Validation

Performed: MainWindow.xaml XML parsing, unique-name checks, event-handler reference checks, verification that all three former AppBarButton references now target Button and no obsolete Label property assignments remain, and comparison of the header strings with pinned WPF resources. No Windows compilation, runtime UI verification or build was performed.

For the next aggregate Windows build, inspect 1920/1280/800/480-pixel window widths and high-DPI display scaling; verify left alignment, bounded gaps, header/control baselines, group wrapping, light/dark corner appearance, all range modes, disabled Angle, tooltip placement, keyboard shortcuts, and visible Save changes/Cancel edit behavior. Confirm no settings or Preview card templates changed.

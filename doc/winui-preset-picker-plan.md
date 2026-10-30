# Preset picker implementation and remaining work

The picker is now a native ComboBox with a single flat scrolling list. Category names are disabled, non-tab-stop ComboBoxItem headings using a category content template. They cannot become an active preset through the selection handler. There are no lateral category flyouts. This is a native ComboBox implementation with in-list visual headers, not a claim that ComboBox supports ListView-style semantic grouping.

Preset choices keep string Content for native text search and a trimming content template for display. Each item retains its category/native description tooltip. Closed and open item captions include (Default), (Modified) or (Default, Modified). Category prefixes appear in help/automation names rather than every visible item. Separate status badges were removed.

A guarded synchronization path updates captions and native selection without reapplying settings. Catalog items are matched by stable key, so a queued job's detached native preset snapshot is not replaced by a catalog definition merely because its selection display refreshes. Default is persisted using the existing user-default/native-default rules; Reload restores the catalog definition and clears current modifications.

## Commands and the WPF reference

The later WPF screenshot clarified the source layout: picker, Reload, Save New Preset. Reload is now a real inline command adjacent to the picker. Set as default remains a distinct action in the adjacent ellipsis management menu; it is not a substitute for Save New Preset.

Save New Preset remains unimplemented. A correct implementation requires durable user-preset storage and full task-to-preset export, including audio/subtitle/filter/geometry rules. The existing GetEditorPresetJson captures only part of that task graph. No placeholder Save button or silently incomplete saved preset is added in this change.

## Sizing and native behavior

The closed field uses the shared preferred width and shrinks to leave room for its inline commands. A trailing flexible column absorbs unused row space so the commands stay adjacent. The native dropdown MaxDropDownHeight is computed as 60% of XamlRoot height when opened. Shared item widths constrain measure around the popup-width preference while leaving room for native padding/scrollbars. The native popup still owns final placement, chrome and scrolling; exact outer width needs Windows inspection rather than assumptions about internal template parts.

All density values and content templates are in Design. No native ControlTemplate is copied or replaced. Existing TabView settings tabs are unchanged; a SelectorBar-based visual proposal is separate from this implementation.

## Validation

Performed: XML and HB resource checks, unique XAML-name/event-handler checks, native ComboBox selection/mapping review, and checks that removed DropDownButton/badge members have no source references. No .NET compilation, Windows UI test or build was run.

For the next aggregate build:

- Open every category, scroll and select a real preset; headers must be skipped by mouse/keyboard and never apply settings.
- Verify keyboard arrows, text search, Escape and selected-item visibility on opening.
- Verify default-only, modified-only and combined captions in both the closed field and dropdown; switch presets and Reload.
- Set a different default and restart; the persisted default must be marked, with no permanent hard-coded default label.
- Open Queue Edit and change one value; presentation refresh must retain the saved snapshot and unrelated fields.
- Verify tooltips show full category/name/status/description when the visible caption is trimmed.
- Resize the settings card and window; inspect popup height/width, row adjacency, scrolling and enlarged text.
- Test unavailable native presets: picker/actions/Reload disabled with an appropriate placeholder.

Status: picker/labels/native grouping-by-headers and inline Reload implemented in source; runtime acceptance pending. Save New Preset remains a separate functional gap.

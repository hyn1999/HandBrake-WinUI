# Settings appearance backlog

## Settings tabs — deferred, approved design direction

Replace the current document-shaped TabView headers with a native WinUI SelectorBar for Summary, Dimensions, Filters, Video, Audio, Subtitles, Chapters and Metadata. Use native selection indicator, hover, focus, keyboard behavior and font weights. Remove document-tab outlines and vertical separators. Use shared compact font-size/spacing resources; retain one row and accessible horizontal navigation when space is limited. Verify overflow and text scaling on Windows before acceptance. Preserve page state and selection when navigating. Do not implement as part of the current Summary review.

## Summary — proposed layout, not implemented

Use the WPF Summary as the information/functionality reference, retaining the separate WinUI preview card. Arrange a two-column label/value grid: Format (native ComboBox), container options stacked vertically under its value column; Tracks (actual video, audio, subtitles and chapter markers); Filters (active filters); Size (storage and display dimensions). Align labels and control edges, use shared compact spacing and native control templates. Let content wrap at narrow widths without splitting checkboxes across distant columns.

Remove visible developer diagnostics (preset/encoder counts, libhb references), duplicate source details, selected-preset name/description already available above, repeated container labels and implementation prose. Keep actionable help in tooltips sourced from the matching WPF resources. Summary values must reflect the configured output, not generic preset rules or source track counts. Keep the actual output summary visible; do not hide essential information only in tooltips. Do not add new capability or change encoding semantics during this visual cleanup.

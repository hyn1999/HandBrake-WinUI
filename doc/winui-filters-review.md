# Filters: native preset editing and compact presentation

## Scope

The tab now presents the screenshot's nine sections: Detelecine, Interlace Detection, Deinterlace, Denoise, Chroma Smooth, Sharpen, Deblock, Colourspace and Grayscale. A shared Auto label column aligns bounded native ComboBoxes; related values wrap within their own row using CompactUi.AlignedFormRow. HBCompactFormSideLabelStyle keeps labels aligned to the first input when a row wraps. Long implementation prose and the global Use preset filters switch are removed. Preset/tune/custom inputs collapse when irrelevant and retain accessible names and WPF help. Fixed-filter rows use Off in the primary preset list; algorithm families use Off plus the available algorithm choices. Grayscale uses the native CheckBox.

## Mapping and state

WPF resources provide captions/help through WpfText. Native GetFilterPresets/GetFilterTunes provide values and display names. Deinterlace supports Decomb, Yadif and Bwdif; Denoise supports NLMeans, HQDN3D and BM3D; Sharpen supports Unsharp and Lapsharp. The native preset value for Yadif is `deinterlace`, verified against libhb/preset.c. Custom strings are accepted only through native filter-settings generation, with a short error status and diagnostic tooltip on rejection. Algorithm changes reset incompatible private custom strings; fixed-filter preset changes retain their tune. Defaults prefer native default/medium rather than accidentally selecting Custom as the first item.

Each row writes only its own Picture* preset keys into filterPresetEdits. GetEditorPresetJson clones the active snapshot, applies advanced values and these patches, and validates edited filter parameters. The complete snapshot is captured for new queue jobs and previews, with UsePresetFilters=true and no legacy FilterOverrides. Native preset resolution creates and orders the filter chain, preserving unrelated effects and structural filters. Unknown imported selector values remain visible and are not silently replaced when another row changes.

FilterSettingsModified compares effective filter values against the catalog preset, including detached queue snapshots. Inactive parameters do not falsely mark a preset modified; custom text matters only for Custom and tune matters for non-custom active presets. Reload/selection resets row patches and restores the preset values.

Existing queued jobs with UsePresetFilters=false still use the unchanged BuildFilterOverrides/BuildNativeJob compatibility path. Opening an old job for editing materializes its old manual effect selection into native preset fields, beginning with all managed effects disabled as the former editor did. A subsequent save uses the complete snapshot. No existing saved queue schema or legacy filter record was removed.

## Source review and validation

Reviewed original WPF FiltersView/FiltersViewModel, HandBrakeFilterHelpers, HBPreset, original label/tooltip catalogs and libhb/preset.c at f1b4112498331ca85f9d59acda2f8628018f0ef0. The repository's current WPF view is a newer generic filter list; the requested compact row layout follows the user's comparison screenshots while using the actual native preset mappings. This change does not claim every additional filter in the newer generic WPF catalog (for example Deband) has been added.

Performed static XML, unique-name, event-handler, HB-resource and obsolete-control reference checks. Verified every edited native preset key and algorithm against libhb/preset.c and every used WPF label/help key against its original catalog. Confirmed the legacy filter-builder method is unchanged and that both queue and preview capture the complete edited snapshot. Native control templates remain unchanged. No .NET SDK, C# compiler or Windows runtime is available locally; no compilation, native execution, UI testing or build was performed.

Next Windows acceptance: every Off/algorithm/preset/tune/custom combination; native rejection and correction of custom input; modify and restore a preset value and confirm Modified; Reload; preserve all other effect/geometry/audio/subtitle settings when editing one row; saved unknown values; new-job queue restart/edit/save and untouched legacy-job encode; legacy manual jobs with zero/multiple effects; Summary and still/encoded previews reflecting identical filters; text scaling, narrow rows, focus/tab order, themes and WPF tooltip placement.

# Workspace review against the supplied WPF screenshots

## Implemented in this pass

- Smaller Workspace header and spacing; scoped compact control minimum heights.
- Adaptive native-control rows for Filters, Audio, internal and imported subtitles.
- Wider proportional preview with hide/show control on desktop and editor/preview switching on narrow windows.
- Editable destination plus output-folder picker. Manual paths are captured in staged jobs and must be absolute with an existing parent folder.
- Chapter-name editors saved per job and applied to native chapter output. Re-scan rejects changed chapter counts. Old drafts retain source names.
- Geometry and foreign-audio search dependent controls disabled until enabled.
- Separate source facts and configured output summary. Metadata source values appear as placeholders; overrides retain existing semantics.
- Root shortcut tooltip suppression was already committed before this pass.

## Native preset pipeline follow-up

- Jobs now start from libhb's `hb_preset_job_init` after scanning the exact encode instance. The handwritten approximation of preset geometry, PAR, frame-rate filters and video settings was removed.
- Built-in presets are retained as complete JSON snapshots in staged jobs. Unknown native fields are preserved.
- New jobs default to native audio, subtitle and effect-filter selection rules. The manual editors are enabled only when the corresponding preset-rules option is turned off.
- Old queue records retain their manual selection semantics. Re-stage a source to opt into the new native preset defaults.
- Preview uses the same job builder with audio and foreign-audio scanning disabled for still frames.
- Changing encoder removes incompatible preset-specific profile, tune and private encoder arguments.
- Native dictionaries are released through libhb; managed UTF-8 buffers are freed after calls. Submission checks hb_add_json's positive sequence ID.
- The diagnostic job file is the exact JSON submitted, with null fields omitted consistently. Failure reports include a bounded recent native log tail.
- Native cache fingerprints exclude managed wrappers. Recovery compares native inputs with the successful integrated build, and package validation checks required native exports.
- These are code changes, not proof that native error 3 has been resolved. A Windows end-to-end encode is still required.

## Remaining Workspace functionality

This is not complete WPF parity. These must be finished before declaring the replacement ready:
- Native encode initialization failure (error 3): diagnostic logging exists, but the cause remains unconfirmed.
- Title angle and chapter/time/frame range selection.
- Container selection and mux flags; preset save/reload.
- Full geometry editing: rotation, flip, anamorphic/PAR, scaling limits/upscaling, borders and computed final geometry display. Native preset mode now resolves these settings in libhb.
- Manual filter algorithm selection, custom settings and colours/grayscale. Native preset effects are now retained.
- Frame rate/mode, colour range, speed/tune/profile/level, multi-pass and advanced video options.
- Multiple audio outputs per input, passthrough/fallback, mixdown, sample rate, gain, DRC and selection rules.
- Subtitle default/selection rules and wider import support.
- Chapter duration display and import/export.
- Explicit metadata removal/pass-through choices.
- High-resolution Windows icon verification at all Explorer sizes.

Queue and Preferences redesign are intentionally outside this pass.

## Verification

XAML parsed, named event handlers resolved, and control names checked for uniqueness.
No Windows build, Actions run or interactive runtime validation was performed. This workspace has no .NET SDK. The P/Invoke signatures and JSON value ownership were reviewed against the matching libhb headers and implementation.
Visual density and responsive breakpoints require checking in the next batched Windows build.


## Next batched Windows validation

1. Verify all native dictionary and preset exports in the packaged hb.dll (the workflow now checks them).
2. Scan the reported 1080p source, re-stage it with General/Fast 480p30 and all three preset-rule options enabled, and encode to a fresh writable filename.
3. Confirm native error 0, a non-empty playable output, capped output geometry with correct aspect ratio, expected frame rate, and audio.
4. Repeat with Fast 1080p30, a source with multiple audio/subtitle tracks, and MKV output. Compare the exact submitted job with a native preset baseline.
5. Turn preset rules off individually: confirm custom selection/silent audio, forced/burn limits, imported SRT and selected effect filters.
6. Check manual geometry and renamed chapters, repeated jobs, cancellation, restart, and an older queue-drafts.json file.
7. If error 3 remains, inspect last-encode-error.txt plus native.log for the failing thread and its preceding error. Do not infer the cause from the numeric code alone.


## Preset picker presentation

- Native cascading menus group presets by their catalog category. Entries show the preset name, a selection checkmark, and the default marker.
- Each entry and the selected-preset button expose the native description in a wrapped tooltip and accessibility help text.
- The user-selected default is stored independently of the last-used preset. Startup selects that default; if it is missing, the native default, General/Fast 1080p30, then the first available entry are used in order.
- Default and Modified badges remain visible next to the selected preset. Choosing a new default stores the preset definition, not the current editor overrides.
- Modified is recalculated from current effective editor state, rather than latched on the first event. It covers video encoder/rate mode/active quality or bitrate, manual geometry, manual audio/subtitle/filter modes, chapter markers/names and metadata overrides.
- Inactive bitrate/quality controls, dormant manual editors, opening a source, navigating tabs, destination editing and preview navigation do not dirty the preset.
- Reload selected preset restores native rule modes and video values, clears manual geometry and metadata overrides, and restores source chapter names.
- Returning all effective fields to their original settings clears Modified. Applying a preset suppresses intermediate change events.

### Batched UI validation cases

- Open each category with mouse and keyboard; inspect a long-description tooltip, selection checkmark and menu scrolling.
- Set a different preset as default, choose another preset, restart: the saved default must be selected and marked. Invalid/missing saved keys must fall back to an available default.
- Change RF, bitrate in bitrate mode, encoder, each native-rule toggle, geometry override, chapter markers/name and metadata. Modified must appear immediately.
- Restore each change: Modified must clear once all effective changes are restored. Editing inactive fields must not affect it.
- Reload a modified preset; confirm both the editor fields and staged job are restored to preset rules.
- Check both themes, compact spacing, a narrow window and screen-reader names/help text.
- Structural checks pass; Windows compilation and interactive checks remain for the next batched build.


## Workspace header and preview follow-up

- Primary Workspace commands now use native CommandBar/AppBarButton icons and labels, with dynamic overflow. Start Encode has one Workspace location in the toolbar; the destination footer contains the filename and folder picker.
- Source identity and title selection occupy the next compact row. The complete truncated source path is available in a local tooltip.
- Open, add-to-queue, encode, preview, browse, scan cancellation and frame-navigation icons use native SymbolIcon assets. Icon-only frame buttons have accessible names and tooltips.
- Preview no longer has a fixed 220-pixel height. The image is centered and explicitly fitted to both viewport dimensions using the decoded bitmap's width/height. Frame controls remain outside that viewport, and source details have a bounded scrollable area.
- Narrow-window preview switching works even when the preview was hidden in the wide-window layout.
- Structural XAML and event/type references were checked. No build or Windows visual validation was run.
- Next Windows checks: landscape/portrait/square video, short and narrow windows, resize while decoding, preview hide/show, light/dark themes, 125–200% DPI, keyboard navigation and toolbar overflow. Confirm the complete video frame remains visible and proportional.

## Dedicated Preview card and window

- Removed the preview show/hide command from the main toolbar. The settings card and Preview card are separate Workspace regions: side by side at wide widths, stacked at narrow widths. The existing explicit preview visibility preference is respected.
- The Preview card owns an Open preview command. It opens or activates one resizable window showing the same decoded frame and previous/next navigation; it does not remove the inline card.
- Frame loading, reset, failure and successful decode are reflected in both views. Closing the main window also closes the preview window. Each view fits the complete bitmap within its own viewport.
- This window is a still-frame viewer. WPF's Encode Preview clip generation, live crop overlay and external-player integration remain unimplemented.
- XAML and handler references for both windows were checked. No build was run. Next Windows validation must cover repeated open/close, navigation from either view, reset during decode, short/narrow layouts, theme/DPI and main-window shutdown.

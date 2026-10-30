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

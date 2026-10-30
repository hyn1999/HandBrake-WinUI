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

## Remaining Workspace functionality

This is not complete WPF parity. These must be finished before declaring the replacement ready:
- Native encode initialization failure (error 3): diagnostic logging exists, but the cause remains unconfirmed.
- Title angle and chapter/time/frame range selection.
- Container selection and mux flags; preset save/reload and complete preset import fidelity.
- Full geometry pipeline: rotation, flip, anamorphic/PAR, scaling limits/upscaling, borders and computed final geometry.
- Filter algorithm selection, custom settings, colours/grayscale and preset import.
- Frame rate/mode, colour range, speed/tune/profile/level, multi-pass and advanced video options.
- Multiple audio outputs per input, passthrough/fallback, mixdown, sample rate, gain, DRC and selection rules.
- Subtitle default/selection rules and wider import support.
- Chapter duration display and import/export.
- Explicit metadata removal/pass-through choices.
- High-resolution Windows icon verification at all Explorer sizes.

Queue and Preferences redesign are intentionally outside this pass.

## Verification

XAML parsed, named event handlers resolved, and control names checked for uniqueness.
No Windows build, Actions run or interactive runtime validation was requested or performed for this pass.
Visual density and responsive breakpoints require checking in the next batched Windows build.

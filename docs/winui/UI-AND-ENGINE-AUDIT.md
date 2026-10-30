# UI and engine audit — 2026-10-29

## Density decision
Use standard WinUI density application-wide. App.xaml previously merged Compact.xaml globally; Microsoft supports compact sizing for Button, ComboBox and other common controls. Unequal apparent sizes are not evidence that ComboBox has no compact support. A custom compact template system would create additional focus, accessibility, flyout and theme maintenance before the frontend has reached feature parity. Default density is the initial consistent baseline; evaluate it on Windows 11 at 100%, 125%, 150% and 200% before introducing optional density modes. NumberBox spin-button placement is independent of application density.

Official references:
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar
- https://learn.microsoft.com/en-gb/windows/apps/develop/ui/controls/navigationview

## Shell corrections
Use the WinUI TitleBar control with SetTitleBar and Mica. Move the pane toggle into TitleBar and hide NavigationView's duplicate toggle. Use NavigationView Auto mode for expanded/compact/minimal navigation according to window width. Navigation compact mode describes the sidebar, not control density. Let theme resources style the XAML title bar; adapt system caption foreground to ActualTheme, including system theme changes.

## Icon
The former build-time PowerShell drawing is removed. Assets/HandBrake.png and the multi-resolution HandBrake-WinUI.ico are converted from the user-selected transparent PNG. No SVG is used by the previous generator: it drew its own image in PowerShell. Preserve the chosen artwork and alpha channel; do not redraw it during builds. ICO sizes: 16, 20, 24, 32, 40, 48, 64, 128, 256 pixels. Small-size legibility still needs visual review on Windows.

## Engine integration: source evidence, not runtime certification
| Area | Source implementation | Remaining limitation |
| --- | --- | --- |
| UI-only artifact | Managed app and interop, intentionally no hb.dll | Native scanning, presets, preview and encode unavailable |
| Integrated artifact | Native source hash cache/release or cross-build, hb.dll copied alongside executable | Matching DLL presence is not a runtime/ABI acceptance test |
| Source scanning | HandBrakeInstance.StartScan, native progress/title metadata | Cancellation/restart and shutdown lifecycle need runtime validation |
| Presets/video | Built-in native presets and encoder capabilities; quality/bitrate draft | No complete preset editor; encoder changes can retain incompatible tuning |
| Preview | GetPreview with crop/scale job, decoded native bitmap | Not a full reproduction of all output filters; native access synchronization pending |
| Encode | Sequential queue runner, rescan, StartEncode, progress, stop | Experimental job translation, not full WPF parity |
| Dimensions | UI disabled; job computes basic crop/scale and PAR | Complete geometry/anamorphic/upscale rules missing |
| Filters | UI disabled; encode maps crop/scale and VFR only | Detelecine/deinterlace/denoise/sharpen and other preset filters missing |
| Audio | Job applies first preset audio rule to all source tracks | Selection rules, passthrough/mixdown validation and multi-rule fidelity incomplete |
| Subtitles | Empty subtitle list and disabled search in generated job | Native subtitle selection/import/burn/default/forced behavior missing |
| Chapters/metadata | Job copies source chapters, metadata/artwork and preset markers | UI editing disabled/incomplete |
| Queue/settings | Draft JSON and separate WinUI preference storage | Recovery, pause, full settings and completion actions incomplete |

The screenshot's hb.dll warning is expected for the successful managed validation artifact. It does not demonstrate an integrated engine failure. Do not label the frontend as fully connected or equivalent to WPF. A full native package and real scan/preview/encode acceptance checks are required separately.

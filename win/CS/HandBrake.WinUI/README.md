# HandBrake WinUI 3 — compact professional workspace

A new **WinUI 3** frontend alongside HandBrake's unchanged WPF application.

## Current preview (UI milestone 0.6 — native presets and video configuration drafts)

- **Modern Professional** Windows 11 layout using Mica and WinUI 3 **Compact** density.
- LeftCompact navigation for Workspace, Queue, and Preferences.
- Editor with Summary, Dimensions, Filters, Video, Audio, Subtitles, Chapters, and Metadata tabs.
- **Fixed preview panel on the right** on wider windows. On narrow windows, use the **Preview / Editor** toggle to switch between the two areas without squashing controls.
- Persistent destination and Start Encode bar.
- Native **Open source** overlay with separate **Video file(s)** and **Folder** choices; the existing Workspace layout stays intact.
- Multiple-file selection and folder selection via Windows App SDK pickers.
- File Explorer **drag and drop** onto the app window, with a temporary Fluent-style drop indicator.
- Keyboard accelerators: **Ctrl+O** opens the file picker, **Ctrl+Shift+O** opens the folder picker.
- Selected filenames, counts, paths, and a *suggested* destination filename are shown. When a matching native `hb.dll` is available, source selection starts a real HandBrake scan.
- A compact status line replaces the large permanent InfoBar; the preview frame placeholder uses a fixed, video-like aspect ratio.

**This is not a working video converter yet.** Source scanning and built-in preset loading are wired to HandBrake.Interop, and a native-enabled manual CI pipeline is configured, but a successful Windows build and scan are not yet verified. Video encoder/rate controls can edit a configuration draft, not a full native encode job. **Start Encode remains disabled.** The queue stores source-only or partially configured drafts, not executable jobs. Preview frames are still placeholders, and suggested output paths are never written.

## Native source scanning (integration milestone)

The WinUI Workspace now connects its source intake to the **existing HandBrake.Interop `HandBrakeInstance`** scanner. It subscribes to native scan progress/completion events, updates the progress bar on the UI dispatcher, lists detected titles and displays **actual** duration, resolution, frame rate, source codec, audio/subtitle tracks and chapter count. The scan can be cancelled. Built-in preset and video encoder controls are enabled only when a matching native library successfully initializes; video frame previews are still placeholders.

**Native-enabled manual CI is configured but not yet verified.** The WinUI workflow now builds `hb.dll` from the matching checkout and places it next to the WinUI executable. If it is missing, the app clearly reports scanning is unavailable rather than inventing properties. Do not mix `hb.dll` from unrelated HandBrake releases. End-to-end scanning still needs a successful build and Windows 11 runtime test.

## Preview build policy

The GitHub Actions preview workflow is **manual-only** on this feature branch to avoid large artifacts on every UI commit. The requested framework-dependent test build was started via a one-time branch-scoped trigger (run #40850566845), which has now been removed without affecting that in-progress run. For future manual builds, open Actions > WinUI 3 Preview > Run workflow and select `feature/winui3-shell`. Artifacts expire after **2 days**.

## Build

GitHub Actions: `.github/workflows/winui3.yml` builds the unpackaged x64 preview. Download the entire ZIP artifact, extract it fully, and run `HandBrake.WinUI.exe` from the extracted folder. Do not move the EXE alone.

To publish on a Windows development machine with .NET 10:

```powershell
dotnet publish win/CS/HandBrake.WinUI/HandBrake.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained false -p:WindowsAppSDKSelfContained=false -o winui-preview
```

The project targets Windows App SDK 2.5.1, `WindowsPackageType=None`, and `WindowsAppSDKSelfContained=true`. The project includes an explicit step to ship `HandBrake.WinUI.pri`, which is required for the WinUI XAML resource lookup on startup.

## DPI diagnostics

The unpackaged EXE embeds `app.manifest` with **PerMonitorV2** DPI awareness. This is intended to prevent Windows bitmap scaling and keep WinUI text crisp at 125%, 150%, and mixed-DPI monitor setups. WinUI controls still use device-independent units; do not multiply font sizes by the Windows scale.

Open **Preferences → Advanced** or **About HandBrake** to see the current WinUI rasterization scale (for example, `125%`). This readout updates when the XAML root scale changes. For a meaningful test, fully exit the app, ensure Windows Compatibility > Change high DPI settings has no forced `System` or `System (Enhanced)` override, and relaunch it after changing Windows Display Scale. A successful build alone does not prove crisp rendering on the user's device.

## Next milestones

1. Validate the manual native-enabled WinUI build, preset catalog and source scanning on Windows 11.
2. Construct **complete** native encode jobs from the chosen preset and scanned title, including filters, dimensions, audio, subtitles and destination validation.
3. Upgrade configuration drafts to executable queue entries with progress, cancellation and recovery.
4. Display real still preview frames.
5. Implement queue-completion actions, notifications, localization and updates once their underlying services exist.

The original WPF application and native HandBrake encoder are unchanged. Review original HandBrake and dependency licenses before redistribution.


## Native libhb integrated preview workflow

Run **Actions → WinUI 3 Preview → Run workflow** on `feature/winui3-shell` only when a test build is needed. The manual workflow cross-compiles `hb.dll` on Ubuntu from the **same checked-out fork revision** using the upstream HandBrake MinGW toolchain, transfers it as a short-lived internal artifact, then publishes the Windows x64 WinUI application with `hb.dll` in the executable folder. The native library is cached by native and interop source hash. The final ZIP artifact is retained for two days. No automatic per-commit builds are scheduled.

The CI verifies required files and x64 architecture. Actual scanning must still be tested on Windows 11 with a known-good source; the existence of `hb.dll` alone does not prove that all its runtime dependencies or the managed/native ABI are compatible. If scanning fails, inspect the reported error and Actions build logs before shipping.


## Preferences — Windows 11 native experience

The primary navigation retains **Workspace**, **Queue** and **Preferences**. Preferences is a nested, responsive Fluent page with the original HandBrake category names (General, Output Files, When Done, Video, Advanced, Updates, Reset, About HandBrake), but it is **not a WPF port**.

- **Functional, saved immediately:** System/Light/Dark theme (live), show/hide the Workspace preview, default suggested output folder, filename underscore replacement, MP4/M4V suggestion, native scan preview count, minimum title duration and duplicate-title behavior.
- **Functional after restart:** native scan verbosity (a HandBrakeInstance is initialized once per app session).
- **Intentionally unavailable until integration:** language/RTL localization, completion actions (shutdown/sleep/notifications), auto-update checks and actual encoding/destination writes. These are visibly described as unavailable, not silently simulated.
- Preferences are stored at `%LOCALAPPDATA%\\HandBrake\\WinUI3\\preferences.json` to avoid corrupting or overwriting the original WPF `settings.json`. The reset button affects **only** the WinUI frontend.
- This code is not yet compiled or exercised on a Windows machine; validate UI navigation, persistence after restart, scanner parameter propagation and DPI scaling during the next manual integrated build.


## Queue draft staging (not encoding)

The Workspace **Add to queue** button stages selected file/folder paths as drafts. If **one** source has been successfully scanned and a native preset/compatible encoder are selected, the draft also captures the actual title index, built-in preset key, muxer, video encoder, rate-control mode, quality/bitrate and suggested destination. Multiple-source selection still creates source-only drafts until individual title scans are performed. Drafts can be removed and cleared, and survive restarts in `%LOCALAPPDATA%\\HandBrake\\WinUI3\\queue-drafts.json`. Old source-only draft JSON remains readable. These are **not HandBrake encoding jobs**: complete filter, geometry, audio and subtitle configuration is not yet mapped, so they cannot be started. Never treat saved draft destinations as validated writable output paths without rechecking them at encode time.


## Native preset / video editor integration (milestone 0.6)

- On WinUI load, when a matching `hb.dll` is present, initialize the same `HandBrakeInstance` used for scanning (required because the upstream preset service needs `hb_init`), then call **`HandBrakePresetService.GetBuiltInPresets()`**. No sample/fake presets are shown when libhb is unavailable. The catalog is grouped by category in the dropdown; the chosen built-in preset key persists in WinUI preferences.
- Populate the Video encoder dropdown from **`HandBrakeEncoderHelpers.VideoEncoders`**. The available quality/bitrate modes and quality slider bounds come from the selected native encoder's capabilities. Hardware encoders are labeled as unverified until runtime device checks exist. The preset's frame rate and encoder tuning are shown read-only.
- The output extension follows the selected preset's muxer (MP4/MKV/WebM). The suggested destination avoids existing files by choosing an unused `-encoded-N` suffix. **This is a suggestion only, never a write or overwrite.**
- Staging a scanned title stores the video settings snapshot, but only if the chosen encoder supports the preset's container. The built-in preset object is **not mutated**. Unsupported target-size rate control is not silently converted into bitrate mode.
- The native preset contains many more fields than this first editor exposes. Audio, subtitles, filters, cropping, chapter and metadata configuration still require faithful job construction; until that is complete **Start Encode remains disabled**.
- **Manual Windows 11 smoke test (after requesting a build):** Open app with matching `hb.dll`; verify preset categories and real encoder list; select two presets with different muxers; scan a known source and select a title; adjust quality/bitrate; stage a draft; inspect its title/preset/encoder/destination in Queue; restart to verify persistence; test missing `hb.dll` for an honest disabled state; verify the original WPF app still works. No CI build is triggered by these commits.


## Small framework-dependent test artifact

This preview intentionally **does not bundle the .NET 10 runtime or Windows App SDK runtime**. The Windows 11 test machine must already have a compatible x64 .NET 10 runtime and the matching Windows App SDK runtime installed. The HandBrake-native `hb.dll` and app-specific `HandBrake.Interop.dll` remain included because they are essential program components, not system prerequisites. The publish job checks for a framework-dependent `runtimeconfig.json` and rejects a bundled `coreclr.dll`. The CI artifact is named `HandBrake-WinUI3-FrameworkDependent-x64`.

# HandBrake WinUI 3 — compact professional workspace preview

A new **WinUI 3** frontend alongside HandBrake's unchanged WPF application.

## Current preview (UI milestone 0.3 — source intake)

- **Modern Professional** Windows 11 layout using Mica and WinUI 3 **Compact** density.
- LeftCompact navigation for Workspace, Queue, and Settings.
- Editor with Summary, Dimensions, Filters, Video, Audio, Subtitles, Chapters, and Metadata tabs.
- **Fixed preview panel on the right** on wider windows. On narrow windows, use the **Preview / Editor** toggle to switch between the two areas without squashing controls.
- Persistent destination and Start Encode bar.
- Native **Open source** overlay with separate **Video file(s)** and **Folder** choices; the existing Workspace layout stays intact.
- Multiple-file selection and folder selection via Windows App SDK pickers.
- File Explorer **drag and drop** onto the app window, with a temporary Fluent-style drop indicator.
- Keyboard accelerators: **Ctrl+O** opens the file picker, **Ctrl+Shift+O** opens the folder picker.
- Selected filenames, counts, paths, and a *suggested* destination filename are shown. No media decoding or scanning occurs yet.
- A compact status line replaces the large permanent InfoBar; the preview frame placeholder uses a fixed, video-like aspect ratio.

**This is a layout preview, not a video converter yet.** Presets, encoder settings, destination editing, queue actions, and Start Encode are deliberately disabled until the HandBrake engine is connected. The preview area is a placeholder, not a decoded video frame. File selection does not scan media. Never interpret example settings or suggested output filenames as actual encoder state.

## Native source scanning (integration milestone)

The WinUI Workspace now connects its source intake to the **existing HandBrake.Interop `HandBrakeInstance`** scanner. It subscribes to native scan progress/completion events, updates the progress bar on the UI dispatcher, lists detected titles and displays **actual** duration, resolution, frame rate, source codec, audio/subtitle tracks and chapter count. The scan can be cancelled. Preset/encoder controls remain disabled; video frame previews are still placeholders.

**Native dependency is not shipped in the current artifact.** The Git repository and current WinUI workflow do not build/package `hb.dll`. If a matching native library is absent from the app directory, the UI clearly reports that scanning is unavailable rather than inventing video properties. A `hb.dll` from an unrelated HandBrake release may have an incompatible ABI or missing dependencies; do not mix builds. A separate native packaging milestone is required before end-to-end scanning can be verified on Windows. Scan code has not yet been compiled/tested on Windows.

## Preview build policy

The GitHub Actions preview workflow is **manual-only** on this feature branch to avoid automatic ~90 MB artifacts on every UI commit. To request a test build, open Actions > WinUI 3 Preview > Run workflow and select `feature/winui3-shell`. Artifacts expire after **2 days**. Source changes can accumulate without repeatedly downloading the preview.

## Build

GitHub Actions: `.github/workflows/winui3.yml` builds the unpackaged x64 preview. Download the entire ZIP artifact, extract it fully, and run `HandBrake.WinUI.exe` from the extracted folder. Do not move the EXE alone.

To publish on a Windows development machine with .NET 10:

```powershell
dotnet publish win/CS/HandBrake.WinUI/HandBrake.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o winui-preview
```

The project targets Windows App SDK 2.5.1, `WindowsPackageType=None`, and `WindowsAppSDKSelfContained=true`. The project includes an explicit step to ship `HandBrake.WinUI.pri`, which is required for the WinUI XAML resource lookup on startup.

## DPI diagnostics

The unpackaged EXE embeds `app.manifest` with **PerMonitorV2** DPI awareness. This is intended to prevent Windows bitmap scaling and keep WinUI text crisp at 125%, 150%, and mixed-DPI monitor setups. WinUI controls still use device-independent units; do not multiply font sizes by the Windows scale.

Open **Settings** to see the current WinUI rasterization scale (for example, `125%`). This readout updates when the XAML root scale changes. For a meaningful test, fully exit the app, ensure Windows Compatibility > Change high DPI settings has no forced `System` or `System (Enhanced)` override, and relaunch it after changing Windows Display Scale. A successful build alone does not prove crisp rendering on the user's device.

## Next milestones

1. Connect native `libhb` source scanning and show actual video information.
2. Populate encoder options and presets from HandBrake's existing models.
3. Display real still preview frames.
4. Implement output destination, queue, encoding, progress, and cancellation.

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

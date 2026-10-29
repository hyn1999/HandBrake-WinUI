# HandBrake WinUI 3 — compact professional workspace preview

A new **WinUI 3** frontend alongside HandBrake's unchanged WPF application.

## Current preview (UI milestone 0.2)

- **Modern Professional** Windows 11 layout using Mica and WinUI 3 **Compact** density.
- LeftCompact navigation for Workspace, Queue, and Settings.
- Editor with Summary, Dimensions, Filters, Video, Audio, Subtitles, Chapters, and Metadata tabs.
- **Fixed preview panel on the right** on wider windows. On narrow windows, use the **Preview / Editor** toggle to switch between the two areas without squashing controls.
- Persistent destination and Start Encode bar.
- Functional **Open source** file picker that shows the selected filename, path, size, and a *suggested* destination filename.

**This is a layout preview, not a video converter yet.** Presets, encoder settings, destination editing, queue actions, and Start Encode are deliberately disabled until the HandBrake engine is connected. The preview area is a placeholder, not a decoded video frame. File selection does not scan media. Never interpret example settings or suggested output filenames as actual encoder state.

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

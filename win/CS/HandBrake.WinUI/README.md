# HandBrake WinUI 3 — first milestone

This folder contains a **new WinUI 3 front end** added alongside the unchanged WPF application.

## Scope

- A Windows 11-style shell with Mica, NavigationView, Home, Queue, and Settings.
- A working video file chooser that displays the selected file name, path, and size.
- Project references to the existing `HandBrake.App.Core` and `HandBrake.Interop` libraries.
- A separate GitHub Actions workflow that publishes an **unpackaged x64 preview**.

**Not implemented:** source scanning, presets, encoding, queue processing, playback, and native `hb.dll` deployment. The UI is a shell preview, **not a functional HandBrake transcoder yet**. Referencing the managed HandBrake projects does not by itself connect the native encoder.

## Building

From the repository root, on Windows with .NET 10 and the appropriate Windows build tooling:

```powershell
dotnet publish win/CS/HandBrake.WinUI/HandBrake.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -o winui-preview
```

The project targets Windows App SDK **2.5.1** and is intentionally **unpackaged** (`WindowsPackageType=None`) and **self-contained** (`WindowsAppSDKSelfContained=true`) for simple early testing. Run the generated `HandBrake.WinUI.exe` from its output directory on Windows 11. Do not move the EXE alone: the rest of the publish output is required.

GitHub Actions: [WinUI 3 Preview](../../../.github/workflows/winui3.yml). Download the entire artifact ZIP after a successful run, extract it, and open the EXE.

## Next milestones

1. Connect the existing HandBrake source scanning interface without changing `libhb`.
2. Reuse existing preset and title models.
3. Add output destination, queue management, encode progress, and cancellation.
4. Bundle the matching native HandBrake libraries and verify real media conversion.

The original WPF application and existing workflows remain untouched. The original HandBrake license files at the repository root apply; review component licenses when distributing a build.

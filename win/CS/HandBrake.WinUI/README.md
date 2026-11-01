# HandBrake WinUI for Windows 11

A WinUI 3 replacement for the WPF frontend, reusing the existing HandBrake engine and managed interoperability layer. The final goal is full capability parity, not a reduced feature set.

## Integration status

This branch reconciles modern window styling, media intake and generated icon assets with the separate scan, preview and sequential encode implementation from `feat/winui3-fluent-preview-encode`. Scan and native preview require a matching `hb.dll`. The encode implementation is experimental: audio selection, subtitles, filters, geometry and preset fidelity still require parity work and runtime validation. It is not a production-ready replacement for WPF.

The Workspace Start Encode action runs configured queue entries; stage a title with Add to queue first. Source-only drafts are not executable. Queue outputs must be unique and must not overwrite any queued source. Settings and drafts are stored independently of WPF under `%LOCALAPPDATA%/HandBrake/WinUI3`.

## Build and validation

- `winui3-pr-validation.yml`: one managed publish per relevant push to the integration branch; no native compilation, one-day artifact.
- `winui3-ui-only.yml`: manual managed UI preview without hb.dll.
- `winui3.yml`: manual integrated preview; managed preflight precedes native cache/release lookup or cross-compilation.

Windows build command (.NET 10, x64):

```powershell
dotnet publish win/CS/HandBrake.WinUI/HandBrake.WinUI.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -p:WindowsAppSDKSelfContained=false -o winui-preview
```

The package requires installed compatible .NET and Windows App SDK runtimes. Extract the entire artifact; do not move the EXE alone. Native binaries must match the source/interop revision. The Linux CI runner produces a Windows DLL, not a Linux application.

## Acceptance checks

Verify startup with and without hb.dll; real source scan; repeated scan and cancellation; title switching and preview navigation; preset and muxer changes; queue persistence; output validation; encode cancellation and actual output media; close during native work; high DPI, keyboard and accessibility behavior. A managed build does not certify native runtime behavior.

See [development baseline](../../../docs/winui/DEVELOPMENT.md) for migration inventory and known stabilization gates. Preserve the original HandBrake and dependency licenses.

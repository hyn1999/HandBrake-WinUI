# HandBrake WinUI: development baseline

## Product contract
Windows 11 only. Replace the WPF frontend with a native WinUI 3 application while retaining all HandBrake capabilities and the existing encoder. Preserve native engine behavior and upstream source paths. WPF remains the behavioral reference until parity is demonstrated.

## Branch audit (2026-10-29)
- master: upstream-oriented default branch, no WinUI project.
- feature/winui3-shell: shared baseline 7ee868b; existing PR #1.
- feature/modern-ui-engine-integration: audit base 2c458297ed125f821df166d563be65d3dbc715e7.
- feat/winui3-fluent-preview-encode: 642ea98397b49539c0bf34c0a0aa27f8db498a84.
The two development branches diverge: modern integration has 14 unique commits and preview/encode has 18. Neither is declared release-ready. This maintenance branch uses modern integration as a review base, not proof of a canonical stable version. Compare and reconcile preview/encode behavior before choosing the canonical branch. Do not discard either history.

## Build policy
- UI-only preview: manual Windows x64 publish, no hb.dll, one-day artifact.
- Integrated preview: manual managed preflight, then matching native cache/release lookup or source build, then final Windows publish; two-day app artifact.
- The Linux runner cross-compiles a Windows DLL; there is no Linux product target.
- Upstream product builds and IRC/bot automation are removed from this maintenance branch.
- No native or WPF source changes are needed for this phase.
- A successful build is not a Windows 11 runtime acceptance test.

Run 40850566845: native job 93m26s; cargo-c installation 4m45s; libhb build 88m09s. Managed publish subsequently failed. Preflight prevents this ordering from wasting a new native build. Existing matching native caches/releases must be retained.

## Migration inventory
Statuses describe observed source, not runtime certification. Every area needs comparison against WPF and Windows 11 acceptance evidence.

| Area | Observed WinUI state | Acceptance requirement |
| --- | --- | --- |
| Shell, theme, DPI, navigation | Implemented, evolving | Keyboard, resizing, mixed DPI, accessibility |
| File/folder intake, drag and drop | Implemented | Files, folders, discs, batch and inaccessible inputs |
| Scan and title selection | Native integration | Real metadata, cancellation, repeated scan, shutdown |
| Presets and video controls | Partial | Import/export, custom presets, encoder capability validation |
| Dimensions and filters | Incomplete mapping | WPF-equivalent geometry, PAR, cropping and all filters |
| Audio | Partial job mapping on alternate branch | Selection rules, passthrough, mixdown, multiple output tracks |
| Subtitles | Incomplete | Selection, import, burn/default/forced and search behavior |
| Chapters and metadata | Partial | Editing, markers, metadata and artwork parity |
| Preview | Different implementations across branches | Correct geometry, stale request rejection, serialized native access |
| Encode and queue | Alternate implementation; unverified | Complete job graph, progress, pause/cancel, recovery and output validation |
| Preferences | Subset persisted independently | Full applicable WPF behavior, migration and reset |
| Logs and error reporting | Incomplete | Actionable persistent native/application logs |
| Localization and accessibility | Pending | Resource-based strings, RTL, screen reader and keyboard support |
| Updates and completion actions | Pending | Windows-specific functional behavior |

## Stabilization gates
1. Reconcile branches and XAML handlers; obtain a managed build.
2. Select one canonical development branch and align the open PR.
3. Define explicit scan/preview/encode lifecycle and serialize native access.
4. Extract persistence and models, then services and view models incrementally.
5. Validate destination collisions within the queue and filesystem, never overwrite sources.
6. Validate full preset-to-job translation against WPF before claiming parity.
7. Test missing DLL, corrupt settings, cancel/restart, close during native work and actual output media.

## Actions history cleanup
Audit found 77 runs, including 65 Push Notification runs. The notification step is guarded for HandBrake/HandBrake, so the fork's runs provide no build evidence.

Candidate cleanup: completed Push Notification runs only. Preserve all build/validation runs, caches, native releases and app artifacts until a stable replacement has been tested. No historical runs have been deleted by this change. The connected GitHub tools in this session expose reads and retry operations, but no run-deletion operation. Cleanup must use an authorized Actions-capable interface.

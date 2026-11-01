# Queue Edit implementation and acceptance

This implements the first full Workspace edit flow for the configuration that the current WinUI editor supports. It does not extract the WPF domain into App.Core or claim complete WPF parity.

## Behavior

- Queue **Edit** takes a detached serialized snapshot and rescans its source. The original entry remains in the queue.
- Restore the saved title, angle/range, raw preset snapshot, video mode/value/options, audio/subtitle selections, imported SRT settings, manual filters, geometry, chapter names/markers, metadata and destination.
- Preserve unknown native preset fields through the raw preset JSON. Preserve additional queue JSON fields and metadata keys not exposed by the four-field editor.
- After restoration, generate native jobs from both the saved snapshot and restored controls. Require structural JSON equality before enabling **Save changes**. This is a runtime guard, not a completed test result. A conservative refusal is preferable to silently replacing a setting the editor cannot represent.
- Save validates the edited native job, retains the original queue ID and position, replaces that entry and resets attempt state to Waiting. It does not begin encoding. Destination checks reject overwriting source paths and colliding with another waiting output.
- Cancel, failed restoration, missing capabilities and unsupported/incomplete snapshots leave the original entry unchanged. Opening another accessible source exits edit mode.
- Disable encode and queue mutation during edit. Keep **Open source in Workspace** distinct from Edit. Compare preset Modified/default/menu selection against the catalog by stable key; Reload uses the catalog definition.
- Defer preview decoding until restoration is complete to avoid decoding an intermediate configuration.

## Compatibility boundaries

Snapshot version 1 is supported; old snapshots without an explicit version deserialize with that default. Entries without native preset JSON cannot be edited through this flow. Legacy manual audio selections without an explicit list are refused. Changed source titles/chapters, missing imports, unavailable options or unequal resolved jobs block saving. This deliberately does not invent replacements or silently discard unsupported settings.

The resolved-job comparison validates equivalent engine input after rescanning; it does not prove successful encoding, unchanged source content, equivalent codec output, or all future WPF features. Exact structural comparison can refuse semantically equivalent defaults; investigate such cases using fixtures before relaxing the guard.

## Validation in this change

Performed: source diff review against the parent commit, HBPreset property/type inspection, XML parsing of MainWindow.xaml, unique XAML name checks, and resolution of the two new command handlers. No .NET SDK is installed in this environment. No build, Windows execution or native runtime round-trip test was performed.

## Windows checks for the next aggregate build

1. Queue a task with a custom range, quality/options, crop, manual audio/subtitle/filter choices, SRT import, chapter names, metadata and destination. Edit without changes: resolved jobs must compare equal and Save must retain the ID, position and configuration.
2. Change just quality, save and reload the queue file. All unrelated fields must survive, including an added unknown preset field, queue extension field and metadata annotation.
3. Edit an existing completed/failed task. Save must produce one Waiting entry at the same position; no duplicate or encode should start. Previous diagnostic files remain until the next attempt begins.
4. Cancel after making changes; compare the original persisted entry. Repeat with canceled/failed scans, missing title/chapter/import and unsupported encoder/filter options. Never enable Save after a partial restoration.
5. Attempt a source/output collision and a duplicate waiting destination; reject without replacing the entry.
6. Verify preset Modified/default/menu state and Reload while editing a saved custom preset snapshot.
7. Change sources during edit, cancel while scanning, and attempt encode/remove/clear/reset via commands; no edit target may be silently mutated.
8. Save, restart, execute a short CPU encode and verify output and logs. The previously reported native error 3 remains unresolved until separately reproduced and diagnosed.

Continue batching builds after coherent implementation milestones, as requested.

# Workspace source header and commands

Source identity and scanned title facts share one line below the main commands: file name, resolution, frame rate, codec, duration and audio/subtitle counts. Long facts and names ellipsize with full-value tooltips. Size and input paths are available from the filename tooltip; its native context menu copies paths or opens the source folder. Preview no longer duplicates source details.

The Title/Angle/Range/Duration gate starts collapsed. Scanned titles reveal it with the existing animation helper (which checks system animation settings); native RepositionThemeTransition handles workspace movement. Rescans retain a disabled existing row until completion rather than collapsing/revealing it mid-scan. Terminal failures or cancellation with no titles collapse it. Routine scan success takes no permanent status row; actual activity and failures remain visible.

Workspace commands now use native CommandBar/AppBarButton with right-positioned labels, icons and native overflow. StandardCommands.xaml remains locally scoped, outside Compact settings geometry. There is no copied template, ordinary Button style applied to AppBarButton, or manual icon/label panel.

Start is disabled when neither pending queue jobs nor a scanned, available current source with video configuration and a usable destination exists. The click handler has the same empty-work guard. Queue execution retains its full validation. Pending count uses the same IsPending predicate as queue execution; native InfoBadge displays this count beside Start Queue and in the navigation Queue item, and hides at zero. No pending jobs changes the command label back to Start Encode.

## Verification

Static checks: XAML XML parsing, unique named elements, event handler resolution, removed-member reference search, review of initial/scan/queue/destination state updates. No Windows build or GUI test dispatched for this edit.

Windows acceptance still required: empty startup with/without restored jobs; scan success/cancel/failure and rescanning; destination edits; pending queue reset/remove/complete; native command alignment and overflow at narrow widths; keyboard access, screen reader names, light/dark/high contrast, text scaling and animations disabled.

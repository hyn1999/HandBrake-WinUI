# Workspace Start acceptance checklist

Implementation: Workspace Start uses waiting entries first; otherwise it captures one scanned source through CreateDraft, the same snapshot method used by Add to queue. Queue Start never captures Workspace. A new entry is persisted only after preflight and overwrite confirmation.

Validation performed: static review of the handler, selection refresh, shared snapshot call and queue mutation ordering. .NET SDK is unavailable in this environment. No build or Windows/native execution was performed.

Windows checks pending for the next aggregate build:

- Empty queue + scanned title + compatible preset + valid destination: Workspace Start creates exactly one configured entry and starts it.
- Existing Waiting entries: Start runs those entries without adding the current Workspace.
- Completed/failed history only: Workspace Start creates a fresh entry, retaining history; Queue Start stays disabled.
- Cancel output replacement: no new entry or diagnostic attempt is created.
- Invalid output/source or incomplete configuration: show an error and retain the queue unchanged.
- Title selection after scan enables Workspace Start; clearing/changing the source disables current-task capture until a new title is selected.
- Ctrl+E follows Workspace Start through its existing handler.
- Double-click Start while overwrite confirmation is open: no duplicate task.
- A native encode must still be tested with retained job JSON/logs. This change does not claim to resolve native error 3.

This is the first SRC-01 implementation step from the pinned parity audit. That audit remains a historical snapshot. Full shared-task extraction and lossless Queue Edit remain outstanding.

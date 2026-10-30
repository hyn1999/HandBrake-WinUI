# Preset picker visual decision (pending implementation)

Recorded from the user's WPF/WinUI comparison; implement after the shared Compact foundation.

- Replace nested category submenus with one bounded-height, single-column scrolling picker. Categories are non-selectable in-list headers with visual separation.
- Keep a normal WinUI combo-style closed selector, with bounded width. Do not build a pseudo-combo from an unrelated control template.
- Show names without repeated category prefixes in the grouped list; provide category in the selected field's tooltip when useful.
- Reuse each native preset's description for its tooltip.
- Move Reload / Set as default into a separate adjacent management menu.
- Put status text in the preset title itself, removing separate Default/Modified badges: Fast 1080p30 (Default), (Modified), or (Default, Modified).
- Default follows the saved user default, or the native HandBrake default for a fresh configuration. Never hard-code Fast 1080p30 as permanently default after the user chooses another.
- Modified describes the currently selected task's settings only; other saved presets must not become modified. Reload clears modifications; selecting a preset restores its saved definition.
- Use the Design Compact tokens/styles and native keyboard selection/focus behavior. Do not repeat menu heights/paddings per item or add lateral popups.

Acceptance: compact placement on narrow/wide windows, grouped scrolling, clear current selection, keyboard navigation, native tooltip descriptions, default persistence, modified/reload behavior and correct labels when both states apply.

Status: design recorded, not yet implemented. This commit only centralizes density and existing menu sizing; it does not claim the grouped picker is complete.

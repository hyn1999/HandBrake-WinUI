# WinUI presentation conventions

Read Design/README.md before adding or changing UI.

- Reuse the Design Compact library for new presentation code. Common controls inherit its application styles; use its named accent/icon/card/media-action roles when needed. Main command strips are explicitly standard-sized: use StandardCommands.xaml locally or CompactUi.MainCommand, never compact command styles.
- Keep density values in Design/CompactMetrics.xaml, reusable styles in Design/CompactStyles.xaml, and programmatic composition in Design/CompactUi.cs. Do not repeat density setters in individual pages or constructors.
- Preserve native WinUI control templates, font family/weight, theme brushes, corners, focus/interaction states and text scaling. Compact changes size/spacing, not those visual rules. Do not invent default-style resource keys: inspect the actual control style before deriving from it.
- Keep encoding rules, task state and native-engine mappings outside Design.
- Treat width, wrapping and feature visibility as layout concerns. Use FieldWidthRole for semantic field sizes and AlignedFormRow for new forms with shared label/body rows. FormRow remains a legacy adapter; WrapFields handles bounded header/command groups.
- Read doc/winui-preset-picker-plan.md for the implemented picker and pending runtime checks/Save New Preset work. Do not reintroduce lateral category menus or treat Set as default as Save New Preset.
- Follow the session's build policy: batch Windows builds after coherent changes; do not dispatch a build for each UI edit. Report static checks separately from compilation and runtime verification.

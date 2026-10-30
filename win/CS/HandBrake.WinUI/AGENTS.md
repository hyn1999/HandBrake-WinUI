# WinUI presentation conventions

Read Design/README.md before adding or changing UI.

- Reuse the Design Compact library for new presentation code. Common controls inherit its application styles; use its named accent/icon/card/media-action roles when needed.
- Keep density values in Design/CompactMetrics.xaml, reusable styles in Design/CompactStyles.xaml, and programmatic composition in Design/CompactUi.cs. Do not repeat density setters in individual pages or constructors.
- Preserve native WinUI control templates, theme brushes, focus/interaction states and text scaling. Do not invent default-style resource keys: inspect the actual control style before deriving from it.
- Keep encoding rules, task state and native-engine mappings outside Design.
- Treat width, wrapping and feature visibility as layout concerns. Use FormRow for existing responsive editor grids and WrapFields for bounded header/command groups.
- Read doc/winui-preset-picker-plan.md for the agreed pending preset redesign. A density change alone does not complete that plan.
- Follow the session's build policy: batch Windows builds after coherent changes; do not dispatch a build for each UI edit. Report static checks separately from compilation and runtime verification.

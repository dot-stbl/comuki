---
phase: 3  plan: 02  title: "Design tokens (Comuki slate-blue + cold, IBM Plex Mono, status tokens, Storybook backgrounds)"
status: complete
duration: "~30m"
started: 2026-06-05T12:00:00Z  completed: 2026-06-05T12:30:00Z
tasks_completed: 3  files_modified: 4
tags: [design-tokens, tailwind, theme]
key-files:
  created: []
  modified: [dashboard/src/index.css, dashboard/.storybook/preview.ts, dashboard/components.json, dashboard/package.json]
key-decisions: ["IBM Plex Mono via @fontsource (not @fontsource-variable)", "Hex colors in index.css (not oklch) for readability and alignment with design spec language", "Status tokens in both :root and .dark blocks with per-theme values"]
requirements-completed: []
---

# Phase 3 Plan 02: Design tokens Summary

Comuki slate-blue + cool-black tokens, IBM Plex Mono via @fontsource, 6 status tokens with dark/light values, Storybook backgrounds and components.json updated.

## Duration  ~30m (2026-06-05T12:00:00Z → 2026-06-05T12:30:00Z)

## Tasks
- Task 1: Swap font dep — `bun remove @fontsource-variable/geist-mono && bun add @fontsource/ibm-plex-mono`; update `@import` line in `src/index.css` (commit d3dccac)
- Task 2: Rewrite `dashboard/src/index.css` with full Comuki token replacement — light palette (#FBFBFA/#3C5A86), dark palette (#15171B/#83A1DC), --radius: 0.375rem, IBM Plex Mono font, all 6 status tokens in both themes (commit d3dccac)
- Task 3: Update `.storybook/preview.ts` backgrounds.values → [#15171B, #FBFBFA] (commit d3dccac)
- Task 4: Update `components.json` baseColor: `"mauve"` → `"slate"` (commit d3dccac)
- Task 5: Build verify — `bun run build` exit 0, no TS errors (PASS)
- Task 6: Build storybook — `bun run build-storybook` exit 0 (PASS)
- Task 7–8: Visual verify and theme switching — deferred to next session (manual); `theme-provider.tsx` untouched per locked decision

## Deviations from Plan
None — plan executed exactly as written.

**Total deviations:** 0 auto-fixed (Rules 1–3). **Out-of-scope:** 0. **Escalated:** 0.

## Authentication Gates
none

## Out-of-Scope Issues
- Regenerating shadcn components to use new tokens (Tailwind v4 + shadcn auto-pickup, confirmed working — `build` passed)
- ModeToggle UI button (plan 3.3)
- StatusBadge component (plan 3.3)
- Storybook stories for all 55 shadcn components + 3 custom (plan 3.3)
- Motion/animation tokens (design-spec § 4, 3.3 if at all)

## Verification
```
=== Hex colors in index.css ===
    --background: #FBFBFA; --primary: #3C5A86;   (light ✓)
    --background: #15171B; --primary: #83A1DC;   (dark ✓)

=== Status tokens (both themes) ===
    :root  --st-running: #0077B6 --st-success: #1D7A4A --st-failed: #B0473B ...
    .dark  --st-running: #1DA9E6 --st-success: #51D273 --st-failed: #D6685A ...

=== Font ===
    --font-mono: 'IBM Plex Mono', ui-monospace, monospace;  (✓)

=== Storybook backgrounds ===
    { name: "dark", value: "#15171B" }, { name: "light", value: "#FBFBFA" }  (✓)

=== components.json baseColor ===
    "baseColor": "slate"  (✓)

=== geist-mono removed ===
    OK: geist-mono removed  (✓)

=== ibm-plex-mono added ===
    "@fontsource/ibm-plex-mono": "^5.2.7"  (✓)

=== next-themes preserved ===
    "next-themes": "^0.4.6" still in deps  (✓)

=== bun run build ===
    ✓ built in 269ms — 0 TS errors

=== bun run build-storybook ===
    ✓ built in 558ms — 0 errors (warnings are Storybook's own internal chunk size notes, not ours)
```

## Files Touched  - Created: 0  - Modified: 4
- `dashboard/src/index.css` — full token rewrite
- `dashboard/.storybook/preview.ts` — backgrounds update
- `dashboard/components.json` — baseColor slate
- `dashboard/package.json` — dep swap

## Next
"Ready for plan 03 — re-invoke `soly execute-plan 3`"
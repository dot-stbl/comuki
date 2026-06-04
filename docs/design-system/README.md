# Comuki — Design System

Source of truth for the Comuki **operational dashboard** (Phase 7) visual
language. Files in this directory are the contract that:

1. **The dashboard implements** — `dashboard/src/index.css` (CSS vars),
   `tailwind.config.*` theme, shadcn component overrides, Storybook
   stories. Per-phase updates to all of these derive from this folder.
2. **Worker agents get told to follow** — `comuki-dashboard-designspec.md`
   (the current preview) is read by `comuki-agent-core/src/rules/` and
   surfaced via MCP, so agents don't drift on colors/spacing/type
   when generating UI in product repos.

## What lives here (planned)

| File | Status | Purpose |
|---|---|---|
| `tokens.md` | 🔜 planned | Color palette (bg, surface, surface-raised, border, text, text-muted, accent), status semantics (running, success, failed, waiting, queued, escalated), typography (display/body/mono), metrics (radius, spacing, shadow), motion |
| `components.md` | 🔜 planned | Comuki-specific components (StatusBadge, RunCard, StagePipeline, RunTimeline, etc.) built on shadcn primitives — variants, sizes, dark/light behavior |
| `patterns.md` | 🔜 planned | Recurring UI patterns — live runs board layout, trace view, approval queue, empty/loading/error states |
| `voice-and-copy.md` | 🔜 planned | Microcopy conventions — short, mono, no marketing voice, errors in "what to do" form |

## Current preview (will be replaced)

The single document we have today is
`.soly/docs/comuki-dashboard-designspec.md` — a design exploration
from earlier. It's serviceable as a starting point but doesn't have
the structure above. When the tokens/components/patterns split lands
in this folder, the old `comuki-dashboard-designspec.md` becomes
historical context (kept, but no longer authoritative).

## How the docs flow into the dashboard

```
docs/design-system/*.md        (here, committed)
    ↓ read by
dashboard/src/index.css        (CSS variables: --color-bg, --font-mono, …)
dashboard/.storybook/preview.ts (backgrounds, theme toggle)
dashboard/src/components/ui/    (overrides of shadcn primitives)
dashboard/src/stories/         (Storybook stories — visual contract)
    ↓ enforced via
lint rule / shadcn override
    ↓ applied to
product repos where workers write UI
```

`comuki-dashboard-designspec.md` (in `.soly/docs/`) is what we have
**today**. Drop the new docs in this folder whenever they're ready
and the Phase 3 plans will pick them up.

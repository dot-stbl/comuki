# Rationale for a11y-known-issues.json entries

The `color-contrast` entries (78 total: 39 unique storyIds × 2 themes) that
persist in `dashboard/storybook-tests/a11y-known-issues.json` after the
first a11y wiring pass represent a **deliberate design trade-off**:

- 52 entries were fixed by switching `--text-faint`/`--text-muted` →
  `--text-on-tinted` in 12 component CSS files (chat-message, chat-dock,
  chat-composer, chat-thread, chat-sessions, chat-side-panel,
  artifact-ref-card in chat/ui; profile-river, run-graph,
  work-item-inspector, runs-table, run-evidence-strip,
  anomaly-breakdown-dialog in runs/ui).
- 26 entries remain in the allowlist as **known debt**, accepted 2026-09-27
  by the owner. These are status-color text (e.g. `color: var(--st-running)`)
  on status-color tinted background (e.g. `background: var(--st-running-tint)`).
  Same-hue text on same-hue background = low contrast. The semantic hue is
  intentional (status badges "running" / "success" / etc.) and changing the
  text color to neutral would lose that signal.

If a future session wants to revisit this trade-off, the resolutions are:
- (a) Accept debt — current decision. The allowlist IS the long-term
  treatment; document in DESIGN.md and move on.
- (b') Replace `color: var(--st-*)` with `color: var(--text-on-tinted)`
  on those classes — loses semantic hue, gains AA.
- (c') Add brighter text-on-tint variants of each status color
  (4xx tokens).

The 26 affected classes (mostly in `chat-message.module.css`):
.phase, .thinkingWordsActive, .memoryCount, .denial, .stepDoneIcon,
.stepRunningIcon, .thinkingCount, .clock (when on tinted parent),
.byline, .thinkingWords, .thinkingWordsActive, etc.

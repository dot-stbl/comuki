# `test:stories` — storybook 10 stories under vitest

Vitest browser-mode harness for dashboard stories, built on
`@storybook/addon-vitest`'s `storybookTest()` plugin (Storybook 10.6, owner
resolution 2026-09-26). The plugin generates one vitest test per story in
`src/**/*.stories.tsx`, with `play` functions executed in real chromium via
`@vitest/browser-playwright`. `@storybook/addon-a11y` (`test: 'error'` in
`.storybook/preview.ts`) runs axe-core after each story and fails the test
on a new (story, theme, rule) violation.

```
bun run test:stories                          # browser-mode vitest, stories project
```

## What runs

One-shot in headless Chromium via the vitest `stories` project; no
dev/watch server is involved, no static build is run from here:

1. **Interaction** — the addon-vitest plugin generates a vitest test per
   story that composes the story through the real
   `.storybook/preview.ts` annotations and runs its `play` function. A
   throwing play fails that story's test, exactly like the old
   `@storybook/test-runner@0.23.0` line's built-in `__test` mechanism did.
2. **a11y** — `addon-a11y`'s `afterEach` runs `axe.run(document.body)` per
   story in browser mode. With `parameters.a11y.test = 'error'`, a
   violation that wasn't there before fails the run.

Every story runs inside ONE iframe served by the project's vite dev server
(the same vite that backs `bun run dev`, port 17184 — see
`.agents/rules/process/ports.md`). No scratch HTTP server, no per-story
`setCurrentStory` channel transition, no `/__sb-harness` middleware.

## Why this shape

The `@storybook/test-runner` package pinned WS16 to its last SB8 release
(0.23.0 — 0.24+ requires SB 10/11 and was a moving target at the time).
Storybook 10 ships `@storybook/addon-vitest` as the first-class way to
run story `play` functions under vitest, in the same in-browser context
the canvas already uses. The first attempt at this migration (previous
commit, since reverted here) hand-rolled `composeStory` +
`setProjectAnnotations` + `toId` and a vite middleware for baseline PNG
I/O — it reproduced what addon-vitest already does, plus the addon-a11y
axe pass. Going through the addon cuts ~440 LOC and aligns with the
upstream upgrade path: the next addon-vitest / addon-a11y release is
the only place to track their wire format.

## Scope: `ws16-batch1` only

`test:stories` runs the stories whose `meta.tags` include a tag from
the addon-vitest plugin's `tags.include` list. The default `ws16-batch1`
covers the WS16 first batch — `Runs/Run graph`, `Runs/Profile river`,
`Runs/Work item inspector`, `Chat/ChatDock`, `Chat/ChatMessage`. To
widen the run, pass `STORYBOOK_INCLUDE_TAGS=ws16-batch1,ws16-batch2` when
a future batch adds its own tag to its stories' `meta.tags`. Today the
`tags.include` default is `['ws16-batch1']` only.

## Portal-based stories

Under `@storybook/addon-vitest` every story is composed and rendered
inside the test iframe. addon-a11y's `axe.run(document.body)` and any
`data-test` lookup see `document.body` by definition — the
`ws16-portal` workaround from the SB8 test-runner era is gone, and the
`ws16-portal` tag carries no load-bearing meaning in this repo anymore
(it was dropped from `chat-dock.stories.tsx` when this harness landed).

History: `@storybook/test-runner@0.23.0`'s per-story channel transition
hung on portal content (its `setCurrentStory` round-trip never resolved
the channel `storyFinished` for `chat-dock`'s stories), and that
mechanism is gone now. The git log preserves the prior workaround.

## Pre-existing a11y debt: `a11y-known-issues.json`

The first batch surfaced a handful of story+theme combinations with real,
pre-existing violations (`color-contrast` everywhere, some `region` and
`scrollable-region-focusable`, `aria-allowed-role`, `listitem`, `label`
in a few places). They are inventoried as JSON in
`storybook-tests/a11y-known-issues.json`, committed and reviewable, so the
harness's `addon-a11y` `test: 'error'` model keeps the door open for
regression detection while the debt is fixed story by story:

- The file is the **backlog of "fix-this before next stories-batch
  lands"**. Not a live allowlist — addon-a11y's `afterEach` does not
  consult it, by design.
- A violation that is **already in the file** will still fail under
  `test: 'error'`. To make a story green while the underlying bug stays,
  flip its story-level `parameters.a11y.test = 'todo'` in the
  story's `meta` (or tag the whole batch with `a11y-known`) and follow
  up to fix the actual issue.
- A violation that is **not in the file** — a regression or a new
  story's own bug — still fails the run.

`color-contrast` alone accounts for the overwhelming majority of entries
and repeats across multiple components — worth investigating as one or two
shared token-level root causes (muted text / status colours against
certain surfaces) rather than individual fixes.

## Reports

The addon-vitest plugin runs the standard vitest reporter; failures
land in the vitest console output with the story's
`Click to debug the error directly in Storybook` link (from the plugin's
`setup-file.js`). No `storybook-tests/report.json` / `.md` is generated
in this model — vitest's default reporter and the addon's per-test
debug link cover the same ground. The gitignored `__screenshots__/`
subdir still catches failure screenshots from the browser provider.
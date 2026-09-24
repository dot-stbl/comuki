# `ui:probe` — agent-facing UI probe command

WS17 of `openspec/changes/add-agentic-test-contour` (branch
`feat/agentic-test-contour-spec`, depends on WS16). See that change's
`tasks.md` (WS16/WS17) and `design.md` ("Report format for agents", "Local
dev commands") for the spec this implements. It reuses WS16's
`scripts/lib/static-server.ts` and its `bun run build-storybook` build step.

```bash
# Render one story, dark theme, screenshot + DOM + aria tree + axe + console
bun run --cwd dashboard ui:probe -- --story runs-list--default

# Both themes, a portal-based story (see "The portal case" below)
bun run --cwd dashboard ui:probe -- --story chat-dock--panel-depth --theme both

# A full page against a static `vite build` preview (mock data, no dev server)
# NOTE: from Git Bash / MSYS on Windows, a leading "/" in an argument gets
# rewritten to an absolute Windows path before Bun ever sees it (classic
# MSYS path-conversion, not a bug in this script) — write "--page //runs",
# not "--page /runs", to get a literal "/runs" through. See "Known issues"
# below.
bun run --cwd dashboard ui:probe -- --page //runs --viewport 1280x800

# Iterate without rebuilding storybook-static on every run
bun run --cwd dashboard ui:probe -- --story runs-list--default --skip-build
```

## Flags

| Flag | Default | Meaning |
|---|---|---|
| `--story <storyId>` | — | Render one Storybook story (mutually exclusive with `--page`) |
| `--page <route>` | — | Render one dashboard route against a static `vite build` (mutually exclusive with `--story`) |
| `--theme light\|dark\|both` | `dark` | Which theme(s) to render; `both` renders twice |
| `--viewport WxH` | `1440x900` | Browser viewport, e.g. `1280x720` |
| `--build` / `--skip-build` | build | Rebuild the static artifact first, or reuse the existing one |
| `--out <dir>` | `artifacts/ui-probe/<slug>` | Output directory (created if missing, never wiped — see "Output") |
| `--port <port>` | picked from the pool | Scratch port for the static server, `17180`-`17200` |
| `--timeout <ms>` | `180000` | Hard timeout for the *whole* invocation (build included) |
| `--fail-on-console-error` | off | A console `error` message also flips that render's status to `error` |
| `--fail-on-axe` | off | Any axe violation also flips that render's status to `error` |

Every flag accepts both `--flag value` and `--flag=value`.

## What it does, and what it deliberately does not

Bounded, single-shot, self-cleaning:

1. Builds (or reuses) a static artifact — `storybook-static/` for `--story`,
   `dist/` (via `vite build --mode mock`) for `--page`.
2. Serves it from an in-process HTTP server (`scripts/lib/static-server.ts`,
   the same one WS16's `test:storybook` uses) on a scratch port from the
   `17180`-`17200` pool, skipping `17186` (`test:storybook`'s own default) —
   picked dynamically unless `--port` pins one.
3. Launches one headless Chromium instance (`@playwright/test`'s `chromium`),
   renders the target once per requested theme, captures artifacts, closes
   the browser and the server, and exits with a real code — `0` if every
   render's `status` is `ok`, `1` otherwise.
4. A hard timeout (`--timeout`, default 3 minutes, covering the build step
   too) forces the process to close the browser and server and exit `124`
   rather than hang — no orphaned Chromium or Node process, whatever goes
   wrong upstream.

**This is not the `bun run dev` / watch / serve pattern AGENTS.md §6 and
`.agents/rules/coding/frontend-construct-rules.md` §0/§7 forbid from an
agent session.** Those forbid a *long-lived* process an agent starts and
leaves running, because it out lives the command that started it and can
starve or wedge the harness. `ui:probe` starts a server, uses it, and shuts
it down — all inside one `bun run` invocation that returns control (and an
exit code) on its own, bounded by `--timeout` even in the worst case. Treat
it the same way you'd treat `bun run test:storybook` (WS16): safe to run
from an agent session, not safe to leave a bare `chromium.launch()` or
`vite preview` running in the background.

## The portal case

`BottomSheet` (and every kit primitive built on react-aria-components'
`Modal`/`Dialog` — `ConfirmDialog`, `FormDialog`, `Dialog` itself) portals
its content into `document.body`, not into `#storybook-root`. WS16's
`test:storybook` README documents `@storybook/test-runner@0.23.0`'s own
readiness check hanging on exactly this — it is (empirically) tied to
`#storybook-root`, which never gains children for a story whose content
lives elsewhere in `document.body`.

`ui:probe` does not wait on `#storybook-root` at all. It listens for
Storybook's own `storyFinished` channel event (`@storybook/core`'s
`PreparedStory.render` emits it after loading, rendering *and* any play
function complete, on both the success and the error path — see the
docblock on `installStorybookReadySignal` in `ui-probe.ts`), installed via
`page.addInitScript` before Storybook's preview bundle runs. That signal is
driven by Storybook's render lifecycle, not by where in the DOM the story
happened to mount its content, so it resolves correctly for a portal-based
story. The screenshot itself (`page.screenshot({ fullPage: true })`) and the
DOM snapshot (`document.body.outerHTML`) are unaffected by portals either
way — a full-page screenshot is pixels, not a DOM subtree, and `document.body`
already contains whatever portaled in.

Validated against `chat-dock--panel-depth` (one of the two portal stories
`storybook-tests/README.md` calls out as blocked for WS16's own harness).

## Output

Written to `--out <dir>` if given, else `artifacts/ui-probe/<slug of the
target>/` — the default location is covered by a `dashboard/.gitignore`
entry this change adds (`artifacts/ui-probe`, same reasoning as WS16's own
`storybook-tests/*` entries: per-run output, not source). A `--out` outside
that path is not automatically ignored. Per theme, filenames are
`<slug>--<theme>.*`:

- `<slug>--<theme>.png` — full-page screenshot
- `<slug>--<theme>.dom.html` — `document.body.outerHTML` (portals included)
- `<slug>--<theme>.aria.yaml` — `page.locator("body").ariaSnapshot()`
- `<slug>--<theme>.axe.json` — axe-core violations (`axe-playwright`, whole
  document — not scoped to `#storybook-root`, for the same portal reason)
- `<slug>--<theme>.console.json` — `{ console: [...], pageErrors: [...] }`

Plus, once per invocation:

- `summary.json` — the design.md agent report envelope
  (`schemaVersion`, `tier: "ui-probe"`, `mode`, `summary`, `renders[]`,
  `failures[]`, `cost`)
- `summary.md` — the same data as a table + a "First failure" section with
  artifact paths inlined, generated from the same object (see
  `scripts/lib/ui-probe-report.ts`)

`ui:probe` also prints a one-line verdict to stdout
(`PASS 2/2` / `FAIL 1/2 — see <out>/summary.md`).

A render's `status` is `error` only for the render itself failing (readiness
timeout, `storyMissing`, an uncaught exception during load/play, a
navigation error in `--page` mode) — console errors and axe violations are
always captured and counted but do not flip `status` unless
`--fail-on-console-error` / `--fail-on-axe` opts in. The default matches
WS16's own allowlist stance (`storybook-tests/README.md`): a probe whose job
is to hand an agent data to look at should not itself go red on pre-existing
debt it didn't introduce.

## `--page` mode

Builds the dashboard with `bunx vite build --mode mock` (mock data, no host
required — `VITE_USE_MOCK` defaults to `true` when unset, `.env.mock` makes
it explicit) and serves `dist/` the same way `--story` mode serves
`storybook-static/`. `scripts/lib/static-server.ts`'s SPA fallback (unknown
path → `index.html`) is what lets a client-routed path like `/runs/123`
resolve correctly. Theme is set by writing `localStorage["theme"]` before
the app's first script runs (`src/app/theme-provider.tsx` reads that key on
boot) — the same thing a returning visitor's browser already has stored, no
DOM hack needed.

**Known limitation, tracked as follow-up, not implemented here:** the
dashboard has two independent appearance axes — `mode` (dark/light/system,
what `--theme` drives) and `themeId` (a palette, `theme-name` in storage,
see `src/app/theme-provider.tsx`'s own comment on why they don't collapse
into one). `--page` mode only drives the `mode` axis; picking a palette
would need its own flag (e.g. `--palette <id>`) and is left for whoever next
needs it, per the WS17 brief's "only if it doesn't require a dev server;
otherwise document as follow-up."

## Testing the pure parts

`scripts/lib/ui-probe-args.ts` (flag parsing) and
`scripts/lib/ui-probe-report.ts` (report building + markdown rendering) are
plain functions with no I/O beyond `writeReport`'s two `writeFileSync`
calls, and have unit tests next to them
(`ui-probe-args.test.ts`, `ui-probe-report.test.ts`).

Run them with Bun's own test runner, scoped to this directory:

```bash
bun test scripts/lib
```

Not `bun run test` — `vitest.config.ts`'s `test.include` is
`src/**/*.test.{ts,tsx}` on purpose (jsdom + Testing Library environment for
app code); `scripts/` is tooling, and `storybook-test.ts`'s own
`lib/report.ts` (WS16) has the same shape and the same gap today. Widening
that glob, or otherwise wiring `scripts/lib` into the vitest run, is out of
this change's file scope — `bun test scripts/lib` is a complete, standard,
zero-config way to run these tests on their own and is what
`dashboard typecheck && dashboard lint && dashboard test` (the actual CI
gate, `AGENTS.md` "Команды") does not need touched to keep passing:
`tsconfig.app.json`'s `include` is `["src"]`, so `scripts/**` is outside
`bun run typecheck`'s scope today the same way it already was for
`storybook-test.ts`; `bun run lint` (`eslint .`) has no such exclusion and
does lint every file under `scripts/`, including these two.

## Known issues / cost

- **Git Bash / MSYS on Windows rewrites a leading `/runs`-style argument
  into an absolute Windows path** (`C:/Program Files/Git/runs`, or wherever
  the shell lives) before Bun's `process.argv` ever sees it — a shell-level
  behaviour, not something `ui-probe-args.ts`'s parser can detect or fix.
  Confirmed empirically: `--page /runs` from Git Bash arrives as `--page
  "C:/Program Files/Git/runs"` and fails to navigate; `--page //runs`
  (double leading slash, MSYS's own escape convention) arrives correctly as
  `/runs`. Only matters for `--page`'s route argument — `--story` ids never
  start with `/`.

- A `--story` run's default (`--build`) always runs the *full*
  `build-storybook` (there is no "build just this story" mode in Storybook
  8) — the first call in a session is the slow one; use `--skip-build` on
  repeat calls against an unchanged `storybook-static/`.
- `--page`'s build (`vite build --mode mock`) is the production build, not
  incremental — same trade-off.
- No visual regression / baseline comparison here — that's WS16's
  `test:storybook` (`storybook-tests/visual-baselines/`). `ui:probe` is a
  point-in-time capture for an agent to *read*, not a pass/fail gate against
  a prior snapshot.

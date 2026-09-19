# React feasibility decision — Windows (this spike only)

> Narrow conclusion only for **this spike on Windows**. Not a general
> statement about `@opentui/react` on every platform — every other
> environment we have not exercised and not asserted.

## Decision

**REACT DEFERRED. Core selected for v1.**

The spike set out to make `@opentui/react@0.5.11` render a non-trivial
chat shell (1 000-row transcript + composer + medium-risk approval
card) against the **official** `testRender` path. We could not make
the React path honest within the iteration budget. The Core path
remains green (`bun run test:core` × 2 → 19 pass / 0 fail, no
warnings).

## Environment

| Item | Version |
| --- | --- |
| Bun | 1.3.10 |
| `@opentui/core` | 0.5.11 |
| `@opentui/react` | 0.5.11 |
| `react` | 19.3.0 (requires `^19.2.0`) |
| `react-reconciler` | 0.33.0 (transitive from `@opentui/react`) |
| OS | Windows (the spike's worktree) |

## Reproduction commands

```sh
cd cli/spikes/opentui
bun install
bun run test:core
```

(`test:react` is intentionally absent from `package.json` — see
*Cleanup* below.)

## What we tried (in order)

1. **Mount with `testRender`** from `@opentui/react/test-utils`. The
   function builds an in-memory `CliRenderer` (`bufferedOutput:
   "memory"`), wraps the initial mount in `act()`, and tears the React
   tree down when the renderer is destroyed. This is the supported
   path.
2. **Stable control surface**. Caller-owned `controls` object; the
   React tree writes its live setters onto it from `useEffect`
   (post-mount), not during render.
3. **External state updates wrapped in `act()`**, plus
   `flushSync()` from `@opentui/react` to force the React commit
   synchronously before reading `captureCharFrame`.
4. **Distinctive prefixes** on every approval field
   (`REACT-APPROVAL-INTENT:`, `…-SCOPE:`, `…-RISK:`, `…-PLAN:`,
   `…-STEP-…`, `…-DIFF:`, `…-ACTION-APPROVE`, `…-ACTION-REJECT`)
   so `captureCharFrame` matches are exact, not approximate.
5. **Real Core renderer for the chat shell**, no parallel React
   tree — same `CliRenderer`, same `createTestRenderer`, one React
   root only.

## Observed behavior

Across six iterations of test code:

### Passes (one-shot, then flake)

- The chrome (`comuki · opentui-spike (react) · focus-mode`) renders
  at the top of `captureCharFrame` on first mount.
- The transcript marker (`Answer #99\d:`) is visible in the
  sticky-bottom viewport.
- The medium-risk approval card renders **all** six distinctive
  fields (`INTENT`, `SCOPE`, `RISK`, `PLAN`, `STEP`, `DIFF`) plus the
  two action markers (`ACTION-APPROVE`, `ACTION-REJECT`) when given an
  explicit `height` so the `SelectRenderable` isn't auto-clipped.

### Stale-frame failures

- `setDraft("DRAFT")` after mount does **not** reach the captured
  frame. `controls.getDraft()` returns the new value (so React
  state did update), but the rendered composer still shows the
  placeholder branch (`draft.length === 0`).
- `act(async () => { setDraft; flushSync(); await waitForVisualIdle(); })`
  does not fix it. `flushSync()` alone does not fix it. Adding
  `key={`composer-text:${draft}`}` to force a remount does not fix
  it (the rendered frame still shows the placeholder).

### Resize failures

- After `setup.resize(48, 16)` the chrome is still visible but the
  draft text disappears. Re-running the same test in the same bun
  process yields different captured frames across runs (not
  deterministic).

### React act warnings

- `IS_REACT_ACT_ENVIRONMENT` is set true by `testRender`. State
  updates from `await act(async () => { ... })` are flushed, but the
  capture pattern `setup.captureCharFrame()` after `await
  waitForVisualIdle()` still triggers the "An update to
  ReactChatShell inside a test was not wrapped in act(...)"
  warning. The warning fires **after** the assertion, suggesting the
  OpenTUI renderer's paint cycle reaches React's scheduler outside
  the act scope.
- Two consecutive `bun run test:react` invocations produce different
  pass counts (sometimes 2/3, sometimes 0/3) for the same source.

## Why we did not push further

- `@opentui/react`'s reconciler does not propagate `ref` callbacks on
  the intrinsic catalogue (`<textarea>`, `<select>`, `<box>`), so we
  cannot drive `setText()` / `setSelectedIndex()` directly the way
  the Core shell does. The intrinsic `<textarea>` only reads
  `initialValue` at mount, with no in-place value sync.
- `<textarea>` and `<select>` are auto-sized in their parent's flex
  container; an approval card built with these intrinsics clips the
  second option unless the parent has an explicit `height`.
- React's concurrent mode (the only mode `createRoot` exposes here)
  schedules commits and the OpenTUI render-loop scheduling does
  not always line up — `flushSync()` from `@opentui/react` is not
  enough to guarantee that the captured frame reflects the latest
  state.
- The captured frame can show stale content even after `setDraft`
  succeeds at the React level. Multiple iterations of `act + flushSync
  + waitForVisualIdle` with different combinations did not produce a
  deterministic pass.

## Production direction

The spike's evidence shows the Core shell (`@opentui/core` only,
no React) is honest: 19 tests / 75 expects pass twice in a row with
zero act warnings and zero listener leaks. The React shell on this
stack on this OS is not. **We do not claim OpenTUI React is broken in
general** — only that this stack, this OS, this test path, and this
fixture size do not give us honest evidence yet.

If a future iteration wants to revisit React:

- `@opentui/react@^0.6.x` or later may lift the `ref`/`initialValue`
  constraint. Re-run this spike against that version.
- Or use `@opentui/react`'s `Solid` adapter which has the same
  primitives but a different scheduling model — and re-test.
- Or write the host entirely on `@opentui/core` (this spike's Core
  shell) and skip the React layer entirely. That is what v1 ships.

## Cleanup (this iteration)

- Deleted `src/react/chat-shell.tsx` and `tests/react.test.ts`.
- Removed from `package.json`:
  `@opentui/react`, `react`, `react-reconciler`, `@types/react`,
  `test:react`. The remaining `@opentui/core` + `@opentui/keymap`
  dependency set is enough for the Core spike and matches the
  production CLI's separation (Ink + React 18 stays untouched).
- Removed the React branch from `bin/opentui-spike.ts` — the host
  binary now mounts only the Core chat shell.
- Removed `scripts/react-probe.ts` (the diagnostic used to scope
  the React path; no longer needed).

## Gate status after cleanup

```text
$ bun run typecheck
$ bun run test:core        # 19 pass, 0 fail, 75 expect, 1.4s
$ bun run test:core        # 19 pass, 0 fail, 75 expect, 1.3s
```

Zero act warnings, zero listener leaks, zero native allocation
failures. Core acceptance is preserved.

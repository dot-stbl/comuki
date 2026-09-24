# HarnessEngine migration step 1 — wire `session-focused` + `session-closed`

> **Status:** plan only. No code changes yet.
> **Parent:** `openspec/changes/rewrite-cli-for-shared-contracts/` (CLI
> rebuild epic — adr-0002 §9 strangler migration).
> **Goal of this step:** introduce a single new module — a thin
> `HarnessEngineAdapter` that fronts the existing `ClientKernel` — and
> route **two** reducer events (`session-focused`, `session-closed`)
> through `reduceHarness` while leaving everything else untouched.
> After this lands, `chat.tsx` still owns its render-model; the reducer
> is exercised end-to-end for one bounded lifecycle path with no
> regression in the rest.

## 1. Context (read it before changing anything)

Three facts that this plan rests on, all sourced from the current
repo and not invented:

- **`reduceHarness` already handles both events.** `cli/src/harness/reducer.ts:97-131`
  — `session-focused` clears `unread` and sets `activeSessionId`;
  `session-closed` removes the session, focuses its neighbour, emits
  `persist-sessions` + `set-subscriptions`. Pure, no I/O.
- **Pure-reducer tests already cover them.** `cli/src/harness/reducer.test.ts:182-209`
  (`"marks background output unread and clears it on focus"`) and
  `cli/src/harness/reducer.test.ts:230-250` (`"closes the active session
  and focuses its neighbour"`). New wiring has unit-level proof
  before integration touches a render.
- **chat.tsx already routes focus / close through a kernel.**
  `cli/src/commands/chat.tsx:1049-1095` — both `focusSession` and
  `closeSession` already call `kernelRef.current.dispatch({ kind:
  "focus-session" | "close-session" })` and additionally patch the
  legacy `tabs` state. That existing dispatch path is the **seam**
  this plan slots into, not a new one.

What the plan deliberately does **not** touch in step 1:

- `openPendingTab`, `stopTurn`, all turn-streaming, signal, the
  `ClientSnapshot` mirror in `lib/kernel-mirror`, auth-flow, status
  footer, persistence-on-every-change. Out of scope.
- Render chrome, layout, fixed prompts, slash-menu, mention menu.
  Conversation-first presentation is invariant; per adr-0002 §2 and
  `commands/chat.tsx:1-19` header.

## 2. Goal of the first wiring step

One **bounded** swap: route the two intents `focus-session` /
`close-session` (which already exist in the kernel) through the
`reduceHarness → runEffect → ports` triad for a `chatSessionAdapter`
that wraps `ComukiClient` + `SessionStore`, and **only for these two
events**. Everything else continues to flow through the existing
`ClientKernel` and `mirror` unchanged. The reducer is now the single
source of truth for the **focus / unread** invariant, but only for
that path. Render model stays `SessionsState`.

Codebase-design translation of that goal:

| Concept | Realised as |
|---|---|
| **Module** | `HarnessEngineAdapter` (one module, one interface) |
| **Interface** | `dispatch(event): { effects: HarnessEffect[] }; subscribe(fn): unsubscribe` |
| **Seam** | the call site at `focusSession` / `closeSession` in `chat.tsx:1049-1095` |
| **Adapter** | the file implementing `dispatch` over `reduceHarness + runEffect + chatSessionAdapter(ports)` |
| **Deep?** | Deep: the small `dispatch + subscribe` interface fronts `reduceHarness` (~480 LoC, 40+ events), `runEffect` + 4 ports (HTTP/SignalR/workspace), and pure tests. The call site grows 2 calls, not 480. |

## 3. Adapter Interfaces that must exist

These are **only** the adapter types; the implementations live in the
files listed in §5. Order matters: each sits behind the one above it.

### 3.1 `HarnessEngineInterface` — the only thing `chat.tsx` imports

```ts
// cli/src/harness/engine-types.ts (new, planned)
export interface HarnessEngineInterface {
  /** Fire one reducer event; run resulting effects through the port
   *  layer; effects that mutate session/connection state feed back as
   *  new events. UI never calls ports directly. */
  dispatch(event: HarnessEvent): Promise<void>;

  /** Push-subscribe to state. Used to mirror `harnessState.sessions`
   *  into `tabs` for the Ink render. Returns the unsubscribe. */
  subscribe(listener: (state: HarnessState) => void): () => void;

  /** Sync accessor for the same shape selectors need (focus, close
   *  path can read `activeSessionId` once, dispatch, then react to
   *  the next snapshot — no `getState()`-style coupling). */
  readonly snapshot: HarnessState;
}
```

**Single source for the boundary.** `chat.tsx` does not import
`reduceHarness`, `runEffect`, or `HarnessState` directly in step 1
beyond reading the type for the subscriber callback. Module-Interface-Adapter
discipline: this interface is the **only** thing the renderer knows.

### 3.2 `HarnessEnginePorts` — the effect adapter contract

Reuse the existing `HarnessEffectPorts` from
`cli/src/harness/effect-runner.ts:96-101`. **No new port shapes** in
step 1. Concretely the four ports stay:

| Port | Adapter in step 1 | Effect subset it serves for the focus / close path |
|---|---|---|
| `ConversationPort` | `ChatSessionAdapter : ConversationPort` | (none in step 1; future steps need it for open/submit/cancel) |
| `ApprovalPort` | unused in step 1 | — |
| `RealtimePort` | the existing SignalR kernel adapter | `set-subscriptions` emitted by `session-closed` (`reducer.ts:127-130`) |
| `WorkspaceStore` | the existing `JsonWorkspaceStore` | `persist-sessions` emitted by `session-closed` |

This is important: the **focus path emits zero effects** (just
`withPersistence`? no — `session-focused` returns `state` with no
`persist-sessions` because the reducer does not call `withPersistence`
for it; see `reducer.ts:97-106`). So in step 1 the focus arm goes
through `reduceHarness`, sets state, and notifies subscribers; nothing
else happens. The close arm runs two effects through the existing
`RealtimePort` and `WorkspaceStore` adapters that already ship.

### 3.3 `ChatSessionAdapter` — the eventual port for create/submit

Not built in step 1. Named here only so we know where the seam will
land when subsequent steps wire `pending-session-opened` /
`turn-queued`. Stub the others; do not implement them.

### 3.4 What does **not** become an adapter in step 1

| Candidate | Why not | Where it stays |
|---|---|---|
| `ComukiClient` → `ConversationPort` | Step 1 needs no `create-remote-session`. Building it now is "future-proofing" that the existing tests would not exercise. | Lives in `cli/src/kernel/adapters/http.ts` already. |
| `SignalRKernelTransport` → `RealtimePort` | Step 1 only runs `set-subscriptions` (which the kernel's `SignalRKernelTransport` already accepts through a different surface). | The engine reuses the kernel's transport **by composition**: `HarnessEngineAdapter` is constructed with a thin `setSubscriptions` thunk that delegates to `kernelRef.current.dispatch({ kind: "set-subscriptions", sessionIds })`. No new adapter class. |
| `JsonWorkspaceStore` → `WorkspaceStore` | Step 1 only runs `persist-sessions`. | Same pattern: thunk over the existing kernel JSON write, reused. |

**Discipline:** one adapter means a hypothetical seam. `ConversationPort`
becomes real when a second implementation lands; **we do not introduce
the port now** because there is no second implementer in this step. The
existing kernel's surfaces (`JsonWorkspaceStore`, `SignalRKernelTransport`)
play the role of "the second port implementation" by composition over
thunks. Step 2 (turn lifecycle) is where the second port gets named
and built.

## 4. Specific event sequence to wire

### 4.1 The renderer's two intents → events

| UI action | chat.tsx hook | Dispatched event (existing) | Reducer case |
|---|---|---|---|
| User switches tab (palette, ctrl+n, mouse click on tab, `/goto`) | `focusSession(index)` at `chat.tsx:1066` | `{ type: "focus-session", sessionId: … }` | becomes `{ type: "session-focused", sessionId }` (after a 3-line mapping) |
| User closes tab (palette action, `ctrl+w`, mouse close tab, `/archive --close`) | `closeSession(index)` at `chat.tsx:1049` | `{ type: "close-session", sessionId: … }` | becomes `{ type: "session-closed", sessionId }` |

The two existing dispatches already fire correct intents to the
`ClientKernel`. The harness's `session-focused` / `session-closed`
events carry the **same payload** as the kernel's intent shape except
for the kind tag. A 3-line `kind`-rename at the boundary (the only
non-trivial mapping in step 1) is the only new glue in chat.tsx.

### 4.2 The two-event reducer path

What `reduceHarness` does for these events (already implemented):

```
session-focused  ──► state: clear unread + set activeSessionId
                   effects: []                          (reducer.ts:97-106)

session-closed   ──► state: remove session, focus neighbour,
                           drop its draft
                   effects: [persist-sessions,
                              set-subscriptions]        (reducer.ts:107-131)
```

Effects run through `runEffect` (already implemented, exit 0 in
existing tests). The two effects call:

- `ports.workspace.write(workspaceDocumentFromState(state))` → fires
  `sessions-persisted` (reducer is no-op on that, `reducer.ts:426-430`).
- `ports.realtime.setSubscriptions(remoteIds)` → fires
  `subscriptions-set` (also reducer no-op).

So the **observed behaviour path** for step 1 is:

1. User presses the focus key.
2. `chat.tsx` translates the existing `focus-session` intent to a
   harness event; `HarnessEngineAdapter.dispatch` calls
   `reduceHarness`. No effects.
3. The subscriber receives the new `HarnessState`. Step 1 maps that
   back into the existing `tabs` state via a function already named
   `mirrorTabsFromHarness` (see §5.1 below).
4. Render flips. End of path; no SignalR call, no disk write. **Same
   observable behaviour as today.**

5. User presses the close key.
2. `chat.tsx` translates `close-session` → `session-closed`. Same flow.
3. Reducer emits two effects. `runEffect` invokes the existing
   `JsonWorkspaceStore.write` and the existing SignalR
   `setSubscriptions` thunk. Both succeed (the workspace write
   already goes through the kernel; `setSubscriptions` flows through
   the existing transport hook).
4. The two confirmation events `sessions-persisted` /
   `subscriptions-set` flow back through dispatch and re-enter the
   reducer; both are no-ops, so the next subscriber callback
   receives exactly the post-`session-closed` state. **Same
   observable behaviour as today.**

**What is therefore provable after step 1, today:** for focus and
close, the user-visible sequence — tab flips, neighbour focuses, no
regressed text, no missed persistence — is identical to the pre-step
behaviour. The reducer is now the source of truth **only** for that
slice; everything else stays on the kernel.

## 5. Component changes in `cli/src/commands/chat.tsx`

The component is **not** rewritten. Three bounded edits only. Names
below refer to places inside the existing component, not new files.

### 5.1 New: one render-model bridge function (`harness-tabs-bridge.ts`)

A pure, renderer-agnostic function that takes a `HarnessState` and
the prior `SessionsState` and returns the next `SessionsState`. **Pure
logic, no React, no Ink.** Lives next to `compat.ts` because the
direction is symmetric (`compat.ts` is legacy → harness; the bridge
is harness → legacy). This is the "deletion test" boundary: the
bridge is the **only** thing in the migration that knows both shapes.
When (much later) `tabs` is gone, this file's deletion will collapse
the whole move.

```ts
// cli/src/harness/tabs-bridge.ts (new)
export function mirrorTabsFromHarness(
  harness: HarnessState,
  tabs: SessionsState
): SessionsState
```

Behavior the function must encode:
- Order: `harness.sessions` order preserved.
- Active: `harness.activeSessionId` resolves to its index in
  `tabs.sessions`; -1 if none.
- `unread: true` for any session whose `harness.sessions[i].unread`
  is `true`.
- `hydrated: true` for any `harness.sessions[i].transcriptLoad.kind
  === "loaded"`. All other fields keep their incoming values from
  `tabs` (i.e. the bridge **does not** synthesise presentation state
  it has no source for — `blocks`, `liveText`, `blocksExpanded`,
  `runsFeed` survive untouched until later steps address them).

### 5.2 In `focusSession(index)` — replace local patch with dispatch

Today (`chat.tsx:1066-1095`): the callback dispatches to the kernel
**and** locally patches `tabs` via `setTabs`. After step 1:

- Keep the `kernelRef.current?.dispatch({ kind: "focus-session" })` as
  fallback when the harness engine is absent (the legacy path stays
  hot for tests that mount without `startKernel()`).
- After the kernel call returns, also call
  `harnessEngine.dispatch({ type: "session-focused", sessionId })`.
- **Delete** the `setTabs((current) => ...)` block. Replaced by a
  re-render driven by the bridge subscriber.

Net: callback **loses** lines (the local-patch closure) and **gains**
two lines (a dispatch to the new adapter). The closure around the
`patchSession` helper is no longer reachable from this callback.

### 5.3 In `closeSession(index)` — same change

Today (`chat.tsx:1049-1064`): kernel dispatch + `setTabs(removeSession)`.
After step 1:

- Keep the kernel fallback dispatch (unchanged) for the no-engine mount.
- Add `harnessEngine.dispatch({ type: "session-closed", sessionId })`.
- **Delete** the `setTabs(removeSession(...))` line.

The `if (kernel && closing)` early-return today suppresses the local
patch when the kernel exists. After step 1 the **engine** is the gate
that decides whether to run the reducer path: when absent, the legacy
local patch remains (no regression for tests / no-hub mode); when
present, the dispatch runs and the local patch goes away.

### 5.4 One new `useEffect` — install the bridge subscriber

A 5-line `useEffect(() => engine?.subscribe((state) =>
setTabs((current) => mirrorTabsFromHarness(state, current))), [...])`.
Re-runs only when `engineRef` identity changes, which is once per
mount.

### 5.5 One new ref / closure — the engine itself

Constucted inside `startKernel()` (which already runs once per mount
of the component, and once per `loginAndStore` cookie swap). The
adapter is built from four dependencies:

```
new HarnessEngineAdapter({
  initial: harnessSeed,                  // one-shot seed from kernel snapshot
  ports: {
    conversation: noopConversationPort,  // unused in step 1; throws on use
    approval:     noopApprovalPort,      // idem
    realtime:     { setSubscriptions },  // thunk → kernel.dispatch(set-subscriptions)
    workspace:    { read, write },       // thin wrappers over kernel JSON IO
  },
  runEffects: true,                      // run runEffect; future steps may flip
})
```

This is the **only** place in `chat.tsx` that learns about the
harness. Once step 1 lands, no other render code touches the
harness types.

### 5.6 What does **not** change in chat.tsx

- `tabs`, `tabsRef`, `sessionsRef`, `activeSession*` derived
  calculations — all stay.
- `openPendingTab`, `stopTurn`, send flow, stream render,
  approval-card render, slash-menu, mention-menu, keymap, status
  footer, terminal title, bell.
- `mirrorRef` / `MirrorMemory` / `kernel-mirror.ts`.
- Header comment at `chat.tsx:1-19` (conversation-first invariant).
  Re-stated in the test name only, not in code.
- `chat.tsx:2239,2290,2318` line numbers of the keyboard
  chords bound to `closeSession` / `focusSession` — the bindings
  stay; only their handlers change.

## 6. Test plan

Three layers, in order, all single-run (`bun test` exits). No
watching, no browser.

### 6.1 Pure-reducer scenarios (already in `reducer.test.ts`)

Existing coverage is sufficient for the events in question. Add only
one new case to lock the bridge's expectation:

```ts
it("selectors read the post-focus state", () => {
  const state = reduceHarness(populatedState(), {
    type: "session-focused",
    sessionId: firstId,
  }).state
  expect(activeSession(state)?.identity.id).toBe(firstId)
  expect(attentionCount(state)).toBe(0)
})
```

Files touched: `cli/src/harness/reducer.test.ts` (1 case). No type
or runtime change.

### 6.2 Bridge unit tests (new file)

`cli/src/harness/tabs-bridge.test.ts` — a new file, no other
touches. Tests the bridge function `mirrorTabsFromHarness`:

- "preserves session order"
- "translates activeSessionId to its index"
- "marks unread from harness.unread, ignores unrelated fields"
- "hydrated=true only when transcriptLoad.kind === 'loaded'"
- "carries blocks / liveText through unchanged"

About 5 cases; pure, no React.

### 6.3 Adapter unit tests (new file)

`cli/src/harness/engine-adapter.test.ts` — wraps the adapter in a
fake-port harness and asserts the two-event flow without rendering:

- "dispatch(focus) advances activeSessionId and emits no effects"
- "dispatch(close) removes the session, emits persist-sessions and
  set-subscriptions in order"
- "subscriber receives the post-reduce state on each dispatch"
- "subscribers receive the same state again when the no-op
  `sessions-persisted` event re-enters dispatch"

Mocks: a `RecordingPorts` implementation that records effect
executions and lets the test inject `sessions-persisted` /
`subscriptions-set` back into dispatch. No `ComukiClient`,
no React.

### 6.4 Thin integration test (new file)

`cli/src/commands/chat-harness-bridge.test.ts` — uses Ink's
`ink-testing-library` to mount the trimmed `chat.tsx` shell with a
fake engine and a fake client kernel. Asserts:

- "switching to a tab with background output marks the just-focused
  session `unread=false` via the reducer"
- "closing a focused tab focuses the next tab and removes the
  closed one from the rendered list"
- Existing `chat.tsx` tests do not regress: `bun test
  cli/src/commands/` exits 0 with the wiring in place.

**Bounded mount**: a test helper builds a minimal `SessionsState`
seeded from one pre-state harness snapshot, wraps it in a no-op
`HarnessEngineAdapter` for the bridge events, and renders only the
chat surface (no SignalR, no ComukiClient). The full component test
suite lives in the existing `chat-app.test.tsx`; this file does not
replace it.

### 6.5 Verification gate (per `~/.agents/rules/typescript/workspace-and-i18n.md`)

```bash
cd cli && bun run typecheck      # exits 0
cd cli && bun run lint           # exits 0
cd cli && bun test harness/      # reducer+selectors+compat+bridge+adapter
cd cli && bun test commands/     # chat-app + chat-harness-bridge
cd cli && bun run test           # everything
```

No dev server, no watcher. All commands exit on their own.

## 7. Risks and rollback

### 7.1 Risks (named, bounded by step 1's scope)

| Risk | Likelihood | Where it bites | Detection |
|---|---|---|---|
| Harness `session-focused` semantics drift from today's local patch | low | The unread → false transition only fires today through `setTabs(patchSession(..., { unread: false }))`. The reducer does the same. | bridge test in §6.2 + integration test §6.4. |
| Reducer `session-closed` neighbour-focus differs from today's neighbour choice | low | Today `closeSession(index)` removes by index and `setTabs(removeSession)` then auto-advances if needed. Reducer focuses by session-key order — same in practice. | reducer test §6.1 already pins neighbour behaviour; integration test §6.4 catches divergence on a populated state. |
| `sessions-persisted` / `subscriptions-set` re-entry causes re-render loops | low | Both events are `unchanged(state)` (`reducer.ts:426-430`). Subscriber only sees state-equality differences. | adapter test §6.3.3. |
| The `if (kernel && closing)` early-return today bypasses local patch — after step 1 the local patch is gone in the engine branch, so an absent engine still uses the local patch. | low | Test mounts with engine absent (existing chat-app test path) must still pass. | existing `chat-app.test.tsx` (no engine mounted) + the new integration test §6.4. |
| Persistence write (`persist-sessions`) ordering: reducer emits it on `session-closed`; today's flow already persists the same way. Duplicated write is possible if the existing kernel keep-alive also writes. | low (existing behaviour) | Compare files on disk with and without step 1. Observable side-effect (disk) — listed in §6.4 as a side assertion. | integration test logs file-write count. |

### 7.2 Rollback

Each step below is a **revert single commit** in the working tree.
No migration data changes (state shape unchanged).

1. **Step 1 rollback (≤5 min):** revert the chat.tsx diff for
   `focusSession` and `closeSession` to the original kernel-dispatch
   + `setTabs` pair; delete the new `tabs-bridge.ts`,
   `engine-adapter.ts`, `engine-types.ts`; remove the
   `engineRef.current?.dispatch` lines and the bridge subscriber
   `useEffect`. Tests in `harness/` stay (they pass independently —
   pure logic). No data loss; the kernel still owns the source of
   truth via the same intents it owned before step 1.
2. **Cross-step rollback safety:** because step 1 keeps the kernel
   dispatch side-by-side with the harness dispatch (`kernel &&
   closing` branch remains the same), undoing step 1 leaves chat.tsx
   in the pre-step state by construction. The newly introduced
   `mirrorTabsFromHarness` function is the only state-pulling path,
   and removing it does not touch any kernel behaviour.

### 7.3 Re-open triggers (do not retry if these fire)

- Existing `chat-app.test.tsx` snapshots change because of
  presentation order: that means the bridge function is wrong; fix
  the bridge, do not change `tabs`.
- Existing reducer tests need to change: that means the interface
  drifted; re-write §3.1 against the new interface, not the other way
  round.

## 8. What this plan does **not** deliver

Naming these so the next step has a clean slate:

- Turns (`turn-queued`, `thinking-started`, `thinking-chunk-received`,
  `turn-completed`, `turn-failed`, `turn-cancel-requested`).
- Approval pipeline (`approval-decision-sent`, `approval-resolved`).
- Connection / auth state (`connection-*`, `auth-*`).
- `pending-session-opened` / `remote-session-adopted` /
  `remote-session-create-failed` (these need `ConversationPort` for
  real, which step 1 deliberately does not name).
- Overlay toggles (`overlay-*`).
- Workspace persistence beyond what `session-closed` already emits.
- Inline Wire-in port definitions for `ConversationPort` /
  `ApprovalPort`. Step 2 (turn lifecycle) is where they get written.
- A renderer rewrite. The Ink component stays.

The migration per `adr-0002 §9` continues one event (or one small
coherent cluster) at a time; **step 1 is two events, both already
present in the reducer, both already passing pure tests**. The risk
budget for this commit is: no observable behaviour change, no new
dependency, no new port.

## 9. Files touched (explicitly, no others)

| Status | Path | Why |
|---|---|---|
| **new** | `cli/src/harness/engine-types.ts` | The adapter interface (§3.1). |
| **new** | `cli/src/harness/engine-adapter.ts` | The adapter implementation; one `dispatch`, one `subscribe`, one `snapshot`. |
| **new** | `cli/src/harness/tabs-bridge.ts` | Pure bridge from `HarnessState` → `SessionsState` (§5.1). |
| **new** | `cli/src/harness/tabs-bridge.test.ts` | Bridge unit tests (§6.2). |
| **new** | `cli/src/harness/engine-adapter.test.ts` | Adapter unit tests (§6.3). |
| **edit** | `cli/src/harness/reducer.test.ts` | One new case for selectors + focused state (§6.1). |
| **edit** | `cli/src/commands/chat.tsx` | Bound to `focusSession` (1066-1095) and `closeSession` (1049-1064): drop local `setTabs` patch, add engine dispatch. Add one `useEffect` for the bridge subscriber. Re-export nothing; import nothing new from `./harness/state` beyond what it already imports. |
| **new** | `cli/src/commands/chat-harness-bridge.test.ts` | Thin integration test (§6.4). |

Eight files; six new, two edited, **zero deleted**, **zero outside
`cli/src/{commands,harness}/`**.

## 10. Commit shape (when the day comes)

`[hybrid](chore(cli/harness): wire chat focus/close to reduceHarness)`
prefix per `[hybrid]` commit rule (`AGENTS.md` §Critical patterns
row 2). Two-adapter signature: a first commit "harness: introduce
HarnessEngineAdapter + tabs-bridge (red)" with the new files and
failing integration test, then "chat: route focus/close through the
engine (green)" once the integration test is signed off.

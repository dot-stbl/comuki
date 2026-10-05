## MODIFIED Requirements

### Requirement: Agent invocation and stream parsing

The Translator SHALL invoke the agent through the **harness abstraction** (`IHarness`, `harness-spi` capability). pi declares `Capabilities.LiveSession = true` and is spawned in **session mode** (`pi --mode json` with an in-process session transport that delivers `TurnInput` commands as session turns). The v1.x one-shot `pi -p BRIEF --no-session` invocation is **removed** with this change — every `PiHarness.StartAsync` runs pi as `pi --mode json` (no `--no-session` flag), and the in-process session transport is the only path for `TurnInput` to reach pi. The Translator does not consult `TranslatorOptions.PiExecutable` for the session question; the decision lives in `IHarness.Capabilities.LiveSession`. Stream-json event parsing (deltas, authoritative assistant text, tool invocations) remains the wire contract; the harness parses each `stream-json` line into the canonical event record.

> **Coordination note (2026-10-04).** The v1.x one-shot `pi -p BRIEF --no-session` invocation is the v1.x path; this change is the v2 path (session mode). The capability's existing text (this section's MODIFIED block) is preserved in v1.x `WorkerId` terminology on the surrounding requirements; the v2 session-mode invocation lives in this block and rides the harness abstraction (the `harness-spi` capability). The session-mode decision is `IHarness.Capabilities.LiveSession` — never a Translator flag, never a per-call override.

#### Scenario: Translator spawns pi in session mode

- **WHEN** `PiHarness.StartAsync` is invoked for a worker
- **THEN** pi is spawned as `pi --mode json` (no `--no-session` flag) and the in-process session transport is open for `TurnInput`; a one-shot `--no-session` invocation never happens under this change

#### Scenario: Translator never reads the v1.x one-shot path

- **WHEN** the Translator's harness resolver evaluates the harness
- **THEN** the decision is `IHarness.Capabilities.LiveSession`; `TranslatorOptions.PiExecutable` is unchanged in shape but no longer read by the shell to choose between session and one-shot

#### Scenario: One garbage line survives

- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

### Requirement: Stream semantics — end on events completion

One stream per connection, bound to one WorkerId. The call SHALL end when the worker completes its events enumeration (the worker's "I'm done" signal); commands flow orchestrator → worker only while events are still coming. When the events side finishes, the command pump is cancelled — no pending `MoveNextAsync` is left for dispose. A worker dropping the stream mid-events is an expected close path, not a fault; real event-pump faults propagate.

> **Coordination note (2026-10-04).** This change's **Baton** phase delivers live-session steering through the existing `WorkerCommandHub` channel against the *v1.x* `WorkerId` ownership model. The slot/execution identity contract (`WorkerHostId` / `SlotId` / `ExecutionId`) is the **cowork 11.1** work-pool slice (`add-mission-cowork` §11); when 11.1 lands, the same `WorkerCommandHub` channel carries the same commands against the slot identity. Baton's surface here is the v1.x surface; the slot surface is the v2 surface. The capability's existing text (this section's MODIFIED block) is preserved verbatim — Baton does not redesign the channel, it consumes it. The slot-binding language from the cowork delta is recorded here as a future-cite (not adopted as MODIFIED text): *«Each execution slot SHALL open an independently authenticated command/event stream bound to `WorkerHostId`, `SlotId`, `ExecutionId`, `WorkItemId`, and fencing generation»* — that text lands in main when cowork 11.1 archives.

#### Scenario: Two slots connect (v2 future)

- **WHEN** two slots on one warm host open streams concurrently (cowork 11.1 v2 surface — illustrative, the v1.x Baton surface carries one stream per `WorkerId`)
- **THEN** both remain active and commands are routed only to the addressed execution

> **L5 note (2026-10-04).** The scenario name `Two slots connect (v2 future)` is a local name added by this delta (not part of the v1.x preserved-verbatim body). The v1.x surface carried a single stream per `WorkerId`; the v2 future-cite uses the slot-binding language from cowork 11.1. The scenario body is consistent with the v2 future-cite; only the name is local. The body of the parent requirement is preserved verbatim from the v1.x main spec.

#### Scenario: Report then complete ends the call

- **WHEN** one slot finishes its event stream after sending the final StageReport
- **THEN** that slot's command loop ends and its stream closes without affecting sibling slots

### Requirement: Orchestrator command handling in the worker

`Stop`, `InjectContext`, and `LeaseExpired` SHALL target one WorkerId. `Stop` SHALL cancel the agent process (whole tree kill) — the run reports `cancelled`. `InjectContext` SHALL append the context to `comuki-injected-context.md` in the working directory. `LeaseExpired` SHALL cancel pi AND mark ownership gone so the loop will not complete/fail the item. The new `TurnInput` command carries a structured `{ Text, Role, Metadata }` turn and is forwarded as a *session turn* on the live agent process — authoritative only when the harness declares `Capabilities.LiveSession = true` (`session` capability, Phase 1); otherwise the platform falls back to "stage a new research WorkItem" per cowork 11.1's existing fallback.

> **Coordination note (2026-10-04).** `TurnInput` and the `LiveSession` fallback are the add-orchestra addition; the existing v1.x `Stop` / `InjectContext` / `LeaseExpired` text is preserved verbatim (the v1.x Baton surface — see ARCHIVE-ORDER CONSTRAINT below). The cowork 11.1 slot-binding rewording of this requirement (commands target one `ExecutionId`) is recorded here as a future-cite and adopted in main at cowork 11.1 archive time.

#### Scenario: Runtime lacks live injection

- **WHEN** `TurnInput` is sent for a worker whose harness declares `Capabilities.LiveSession = false`
  - **THEN** the system creates a follow-up execution path (per coworker tasks 11.1) and the original process is not reported as having consumed the turn

#### Scenario: Soft stop

- **WHEN** the orchestrator sends `Stop` for one WorkerId with a reason
- **THEN** only that worker's agent process tree is cancelled and its report is `cancelled`

#### Scenario: One garbage line survives

- **WHEN** the agent emits a non-JSON line mid-stream
- **THEN** the run continues and the line surfaces as an unparseable event

### Requirement: Worker REST surface

The worker REST surface SHALL claim, heartbeat, complete, and fail on behalf of one authenticated WorkerId. Claim results and subsequent mutations SHALL carry a WorkerId fencing generation. Ownership is derived from the worker token credential and server assignment; a worker cannot claim another worker's identity.

> **Coordination note (2026-10-04).** The v1.x text is preserved verbatim (the v1.x Baton surface — see ARCHIVE-ORDER CONSTRAINT below). The cowork 11.1 slot-binding rewording of this requirement (`execution slot` / `ExecutionId`) is recorded here as a future-cite and adopted in main at cowork 11.1 archive time.

#### Scenario: Wrong worker rejected

- **WHEN** worker B attempts to complete a WorkItem leased to worker A
- **THEN** the mutation is rejected without changing the WorkItem

#### Scenario: Ownership miss is 409, not 500

- **WHEN** a slot completes an item the reaper already requeued or fenced
- **THEN** the endpoint answers 409 with a stable ownership code

#### Scenario: Stale generation after cancellation is rejected

- **WHEN** a slot completes a WorkItem using a generation from before the Run's cancellation or supersession
- **THEN** the answer is 409 with code `work-item.not-owner` and the WorkItem/Run outcome is unaffected by the late completion

#### Scenario: Current generation still succeeds

- **WHEN** a slot completes a WorkItem using the Run's current generation
- **THEN** the completion is accepted exactly as before this change

#### Scenario: Claim body without envClass is 400

- **WHEN** a slot posts `/workers/claim` with profileKey and profilesRef but no envClass
- **THEN** the answer is 400

### Requirement: Translator loop

The worker's outer loop SHALL run claim → execute → report → repeat until the process stops; an empty claim waits the poll interval (default 10 seconds) before retrying. One cycle:

1. claim over REST (empty → return)
2. prepare the working directory: clone the target repository, then run the accepted `[restore]` opcodes from `.comuki/environment.toml` at that ref (host-side, not the coding agent); then prepare profiles material under `profiles/` (copy from the mounted path when configured, else shallow-clone the public git URL at the pinned ref, else warn and skip). Restore failure fails the item and skips spawn
3. open the gRPC session, send StageStart
4. spawn the agent executable through the **harness abstraction** (`IHarness.StartAsync`, `harness-spi` capability, Phase 8) and pump its stream-json output, forwarding text deltas / authoritative assistant text / tool invocations as Activity events while a heartbeat task extends the lease every interval (default 30 seconds) and a command task consumes orchestrator commands
5. send the final StageReport, close the session
6. if the lease was lost (rejected heartbeat or a `LeaseExpired` command): skip completion entirely — the reaper owns the item
7. else complete on `success` (result JSON = the serialized StageReport) or fail with a `status: error-text` reason otherwise

Failures propagate and stop the host — an ephemeral worker is meant to die and be replaced, not limp along.

> **Coordination note (2026-10-04).** The v1.x loop body is preserved verbatim (the v1.x Baton surface — see ARCHIVE-ORDER CONSTRAINT below). The cowork 11.1 slot-binding rewording (slot loop, host/slot credential, etc.) is recorded here as a future-cite and adopted in main at cowork 11.1 archive time.

#### Scenario: One agent process fails

- **WHEN** an agent process exits non-zero in one of twenty concurrent workers
- **THEN** that WorkItem follows failure policy while the other nineteen worker loops continue

#### Scenario: Non-zero pi exit fails the item

- **WHEN** a spawned pi process exits non-zero
- **THEN** that worker reports failed with the exit code and safe stderr detail

#### Scenario: Lease lost mid-run

- **WHEN** a heartbeat is rejected while the agent still runs
- **THEN** that agent is cancelled and the worker does not complete or fail the item authoritatively

#### Scenario: Result text is authoritative

- **WHEN** an agent streams deltas and later emits authoritative final assistant text
- **THEN** the worker's authoritative-text rule replaces accumulated deltas with the final wording

#### Scenario: Restore runs before pi

- **WHEN** the claimed item's class declares `[restore] dotnet = "comuki.slnx"`
- **THEN** `dotnet restore comuki.slnx` completes before pi is started

## ADDED Requirements

### Requirement: Harness abstraction is the Translator public surface

The Translator SHALL consume the harness abstraction (`IHarness`, `harness-spi` capability). The v1.x `IPiRunner` / `PiRunner` is replaced by `IHarness` with two implementations: `PiHarness` (existing pi path, renamed; declares `Capabilities.LiveSession = true`) and `TestFakeHarness` (lifted from `Comuki.TestFakePi`; declares `Capabilities.LiveSession = false`). Harness selection is read from `ProjectSettings.HarnessId` (a new optional field) and the profile's `harness:` frontmatter.

#### Scenario: PiHarness is the first implementation

- **WHEN** the host composes with the harness abstraction
- **THEN** `PiHarness` is registered with `Name = "pi"`, `Capabilities.LiveSession = true`, and the existing pi spawning path runs through `IHarness.StartAsync`

#### Scenario: TestFakeHarness is the second implementation

- **WHEN** tests run with the harness abstraction
- **THEN** `TestFakeHarness` is registered with `Name = "test-fake-pi"`, `Capabilities.LiveSession = false`, and the test fake's stream-json emission runs through `IHarness.ParseEvent`

#### Scenario: Profile declares its harness

- **WHEN** a profile has `harness: test-fake-pi` in its `control-plane/profiles/<name>.md` frontmatter
- **THEN** claims with that profile match against `test-fake-pi` workers and not against `pi` workers

### Requirement: Capabilities.LiveSession is the single source of truth for steering

`IHarness.Capabilities.LiveSession` (a `bool` field of `HarnessCapabilities`) is the single source of truth for whether `TurnInput` is authoritative on the worker. The Translator reads the value at worker start; the bidi command channel is open when `true` and absent when `false`. No other field is consulted.

#### Scenario: Pi declares LiveSession = true

- **WHEN** a Translator starts a `PiHarness` execution
- **THEN** `Capabilities.LiveSession` is `true` and the bidi command channel is open for `TurnInput`

#### Scenario: Test-fake harness declares LiveSession = false

- **WHEN** a Translator starts a `TestFakeHarness` execution
- **THEN** `Capabilities.LiveSession` is `false` and the steering endpoint refuses `TurnInput` for that run with `code = session.livesession_unavailable`

### Requirement: Live-session mode is the harness's choice, not a Translator flag

The Translator SHALL NOT carry a "live session" / "no-session" flag. The decision lives in `IHarness.StartAsync`. `PiHarness.StartAsync` runs pi in **session mode** (`pi --mode json` with an in-process session transport) — the bidi command channel carries the session turn; the Translator does not consult `TranslatorOptions` for the session question. There is no per-call override: pi never goes through `--no-session` under this change.

#### Scenario: Translator does not consult PiExecutable for session

- **WHEN** the Translator starts a worker
- **THEN** the session-mode decision is `IHarness.Capabilities.LiveSession`; the Translator never consults `TranslatorOptions.PiExecutable` to choose between session and one-shot (the v1.x one-shot `--no-session` form does not exist under this change)

#### Scenario: PiHarness runs in session mode

- **WHEN** `PiHarness.StartAsync` is invoked for a worker
- **THEN** pi is invoked as `pi --mode json` (no `--no-session` flag) and the in-process session transport is open for `TurnInput`; a one-shot `--no-session` invocation never happens under this change

#### Scenario: Test-fake harness rejects authoritative session turns

- **WHEN** a `TurnInput` is delivered to a `TestFakeHarness` execution (the test fake declares `Capabilities.LiveSession = false`)
- **THEN** the steering endpoint returns `409 Conflict` with `code = session.livesession_unavailable`; no session-mode pi is spawned for the fake

## ADAPTER Notes

`WorkerCommandHub` ships on master with `Stop` / `InjectContext` / `LeaseExpired` / `Exec` plumbing and zero production callers; the kill switch (`Stop`) is wired to the reaper, the other commands sit on the registry without live callers. Baton adds `TurnInput` and the first caller. The v1.x `WorkerId` model is the contract Baton delivers against; cowork 11.1's slot / execution identity is the contract the v2 worker-runtime declares — the change does not pre-empt cowork 11.1, it consumes the existing channel.

The Translator's pi invocation is fully session-mode under this change: `pi --mode json` with an in-process session transport, `TurnInput` delivered as a session turn. The v1.x one-shot `pi -p BRIEF --no-session` path is removed; nothing in the Translator or the harness abstraction re-introduces it.

## ARCHIVE-ORDER CONSTRAINT

`worker-runtime` MODIFIED blocks above are authored in **v1.x `WorkerId` terminology** with the slot-binding language from cowork 11.1 quoted as a coordination note ("future-cite"). This is deliberate: archiving the add-orchestra change before cowork 11.1 lands would land the slot-binding text in main while the runtime still speaks v1.x `WorkerId` — a main-spec that promises slot identity before the contract exists. The constraint:

- `worker-runtime` deltas of this change SHALL be archived **only after** cowork 11.1 (the work-pool slice) is archived; **or**
- re-authored in slot terminology (WorkerHostId / SlotId / ExecutionId) at archive time, so the MODIFIED text lands in main in lockstep with the slot-binding contract.

A checklist entry: the auditor reading this change before archiving confirms (a) the open cowork 11.1 has landed, or (b) the slot-rewrite of every MODIFIED block in this file is in the archive commit.
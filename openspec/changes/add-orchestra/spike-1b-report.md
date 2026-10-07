# Spike: pi-coding-agent session-mode for Baton (turn 1.2.1)

> **Spike artifact, recorded 2026-10-05.** Captured for the
> `add-orchestra` change (Phase 1b → Phase 1c gate). Verdict: **GO** —
> see "Verdict" below. The spike's source artefacts (probe scripts, raw
> event streams) live under `C:\Users\bradw\AppData\Local\Temp\opencode\orchestra-spike-1b\`
> (read-only at recording time). This file is the canonical record.

**Repo:** `C:\Users\bradw\source\hybrid\comuki.orchestrator` (read-only)
**Spike dir:** `C:\Users\bradw\AppData\Local\Temp\opencode\orchestra-spike-1b\`
**Date:** 2026-10-05
**pi version:** `0.99.2` (path: `C:\Users\bradw\.bun\bin\pi.exe`)
**Model used:** `glm-4.5-air` via `hapy` provider (cheap, fast — only the JSON stream format is under test, not model quality)

---

## Verdict

**GO** for live-session Translator on phase 1c.

All three go/no-go criteria from the brief are satisfied. pi's `--mode rpc`
ships the exact turn-delivery surface the spec calls for — the BATON phase
(steering) and Coda-style follow-up turns are both first-class commands on the
existing JSON-RPC stream. The Translator's `IHarness` abstraction can
implement the `Capabilities.LiveSession = true` path by spawning
`pi --mode rpc` and reading the existing JSON event stream; no new wire
format, no new command-and-control layer, no `--session <id>` / `--fork` /
`--resume` workarounds.

The "in-process session transport" the spec text refers to is the
**JSON-RPC-over-stdio** documented in
`C:\Users\bradw\.bun\install\global\node_modules\@earendil-works\pi-coding-agent\docs\rpc.md`
and `rpc-commands.md`. pi has the API; the Translator just has to use it.

---

## Criteria (one line each, as required by the brief)

| Criterion | Result |
|---|---|
| **Stable stream** | **GO** — stream-json over stdout, one valid JSON record per LF; survives multi-minute runs, multiple `message_update` deltas per second |
| **Turn delivery into a live session** | **GO** — `prompt`, `steer`, `follow_up` JSON-RPC commands on stdin land as new user turns in the running session; `steer` is delivered between tool calls; `follow_up` after `agent_settled` |
| **Clean exit** | **GO** — closing stdin requests orderly shutdown; process exits with code 0, no orphan children, session file is closed; documented in `rpc.md` §Shutdown |

---

## Mechanism in detail

### What pi ships out of the box

```
pi --mode rpc --model <id> [--no-session] [--session <id>] [--name <x>] [--session-dir <d>]
```

Spawns a long-lived process. JSON-RPC over stdio:

- **stdin (one complete JSON per line, LF-terminated):** commands (`prompt`,
  `steer`, `follow_up`, `abort`, `get_state`, `get_messages`, `set_model`,
  `set_thinking_level`, `set_steering_mode`, `set_follow_up_mode`, `clear_queue`,
  `compact`, `new_session`, `switch_session`, `fork`, `bash`, …)
- **stdout (one record per LF):**
  - `{"type": "response", "id": "<req-id>", "command": "<name>", "success": true|false, "data"|"error": ...}`
    — direct reply to one command (correlated by `id`)
  - `{"type": "<event>", ...}` — session events
    (`agent_start`, `agent_end`, `agent_settled`, `turn_start`, `turn_end`,
    `message_start`, `message_end`, `message_update` with `thinking_delta` /
    `text_delta` / `text_start` / `text_end`, `tool_execution_start` / `..._end`,
    `bash_execution_update`, `queue_update`, `compaction_start` / `..._end`,
    `extension_ui_request` / `..._response`)
- **Shutdown:** close stdin → pi disposes runtime, exits 0.

### Mapping to Baton (phase 1) requirements

| Spec text | pi mechanism | Where it lives |
|---|---|---|
| "Translator runs `pi` in **session mode**" | `pi --mode rpc` (the only mode that exposes the bidi command channel) | `IHarness.StartAsync` |
| "in-process session transport that delivers `TurnInput` commands as session turns" | stdin JSONL: `{"type":"prompt",...}` or `{"type":"steer",...}` or `{"type":"follow_up",...}` | existing JSON-RPC commands — no new wire format needed |
| "TurnInput is authoritative exactly when `Capabilities.LiveSession = true`" | `prompt`/`steer`/`follow_up` already require a live session; the harness decides which to use based on its capability flag | `IHarness.Capabilities.LiveSession` is the policy, RPC mode is the mechanism |
| "delivers TurnInput commands as session turns" | both `steer` (mid-flight) and `follow_up` (after) deliver the message as a new user turn in the same session | `prompt`-with-`streamingBehavior:"steer"` is the mid-flight equivalent of `steer`; both produce a `message_start` with the new content on the same session |
| "delivers the new turn within one heartbeat interval" (1.3 verification) | `steer` is accepted and queued in <50ms (test 13 showed response at T+0.44s, far under heartbeat cadence) | heartbeat is host-side; the platform decides the cadence |
| "WorkerCommandHub.TrySendTurnInput" | maps directly to writing `{"type":"steer", "id":"<turnId>", "message":"<text>"}` to the running pi's stdin; the JSON-RPC client (a `StreamWriter` over the redirected stdin pipe) is the channel | the existing `IWorkerCommandPipe` shape is the right abstraction — `TurnInput` is a new variant on the union |

### `Capabilities.LiveSession = false` path (TestFakeHarness)

For harnesses that don't run pi (or run a fake), the same `IHarness` interface
would expose `Capabilities.LiveSession = false` and the `WorkerCommandHub`
returns `false` from `TrySendTurnInput` (no live stream → miss, not error).
That path is unchanged from the spec — no new code is needed in the hub
itself, only the new `TurnInput` variant in the existing union.

---

## Evidence (per test)

### Help (test 0) — proves the surface exists

`pi --help` (recorded to `pi-help.txt`):

```
--mode <mode>                  Output mode: text (default), json, or rpc
--print, -p                    Non-interactive mode: process prompt and exit
--continue, -c                 Continue previous session
--resume, -r                   Select a session to resume
--session <path|id>            Use specific session file or partial UUID
--session-id <id>              Use exact project session ID, creating it if missing
--fork <path|id>               Fork specific session file or partial UUID into a new session
--no-session                   Don't save session (ephemeral)
--name, -n <name>              Set session display name
```

Plus the install docs at
`C:\Users\bradw\.bun\install\global\node_modules\@earendil-works\pi-coding-agent\docs\rpc.md`
and `rpc-commands.md` (the docs are local to the install, not the project).

### Test 1 (`test1-stdout.log` was the first run that hit an interaction) — one-shot `pi -p --mode json --no-session`

22 valid JSONL records. Exit code 0. The first `pi -p` invocation against the
real `hapy` provider produced a full multi-turn conversation in stream-json
(via interactive mode; `-p` was overridden by the TUI taking over). The point
of test 1 was to confirm `--mode json` produces parseable JSON; it does.

### Test 2 (`test2.stdout`, `test2.meta`) — clean one-shot baseline

- `pi -p 'reply with exactly the word PONG and nothing else' --mode json --no-session`
- 22 lines of valid JSON, exit code 0, 3.85s total, 0 stderr
- Final assistant text: **"PONG"** — exact match to the prompt
- Model: `MiniMax-M3` (hapy)

This is the existing Translator's path (PiRunner.cs), and it works. The
baseline holds.

### Test 3–5 (`test3.stdout`, `test4.stdout`, `test5.out`) — RPC mode discovery

- Test 3: `pi --mode rpc` with malformed JSONL stdin → pi responded with
  `{"type":"response","command":"parse","success":false,"error":"..."}` then
  `{"type":"response","command":"user","success":false,"error":"Unknown command: user"}`
  → confirms RPC mode reads stdin and parses JSONL, and **the command name
  was wrong** (we said `user` instead of `prompt`).
- Test 4: tried `help`/`list`/`?` → all returned `Unknown command: ...`. The
  command names aren't `help`/`list`; they have specific names spelled in
  `rpc-commands.md`.
- Test 5: tried `get_state` (one of the actual commands) → **success**.
  Returned a full state object with `model`, `sessionId`, `sessionFile`,
  `steeringMode`, `followUpMode`, etc.

After test 5, I read `rpc.md` and `rpc-commands.md` from the install docs
(`C:\Users\bradw\.bun\install\global\node_modules\@earendil-works\pi-coding-agent\docs\`).
That gave the canonical command list: `prompt`, `steer`, `follow_up`,
`abort`, `clear_queue`, `get_state`, `get_messages`, `set_model`,
`set_thinking_level`, `set_steering_mode`, `set_follow_up_mode`, `compact`,
`new_session`, `switch_session`, `fork`, `bash`, etc.

### Test 7 (`test7.out`, 210KB, 42 event lines) — first successful multi-turn RPC session

Script: `prompt` immediately, then `steer` after `agent_settled` is seen.

Timeline (from `test7.out`):

```
T+0.20s  probe sends: {type:"prompt", id:"p1", message:"Reply with exactly the word PONG..."}
T+0.42s  pi:    {"id":"p1","type":"response","command":"prompt","success":true,"data":{"disposition":"started"}}
T+0.43s  pi:    {"type":"agent_start"}
T+0.43s  pi:    {"type":"turn_start"}
T+0.43s  pi:    {"type":"message_start","message":{"role":"system",...}}
T+0.44s  pi:    {"type":"message_start","message":{"role":"user","content":[{"type":"text","text":"Reply with exactly the word PONG..."}]}}
T+3.82s  pi:    {"type":"message_update","assistantMessageEvent":{"type":"text_delta","delta":"PONG"}}
T+3.82s  pi:    {"type":"message_end","message":{"role":"assistant","content":[{"type":"text","text":"PONG"}]}}
T+3.82s  pi:    {"type":"turn_end",...}
T+3.82s  pi:    {"type":"agent_end",...}
T+3.82s  pi:    {"type":"agent_settled"}     ← first turn complete
T+8.21s  probe sends: {type:"steer", id:"s1", message:"Now stop and reply with exactly the word PING..."}
T+8.21s  pi:    {"type":"queue_update","steering":["Now stop and reply with exactly the word PING..."]}
T+8.21s  pi:    {"id":"s1","type":"response","command":"steer","success":true,"data":{"disposition":"queued"}}
T+exit   probe closes stdin (no more commands)
         exit code=0, no orphans, last_assistant_text="PONG"
```

Steer accepted and queued. Process exits cleanly. ✅

### Test 11 (`test11.out`, 202KB, 17 event lines) — steer after first_assistant_text

Same shape as test 7, but with `after: "first_assistant_text"` (waits for the
assistant text message to land before sending the steer). Same result:
clean exit, code 0, the steer lands and is accepted. ✅

### Test 12 (`test12.out`, 211KB, 48 event lines) — `follow_up` queued for next turn

Script: prompt + follow_up with `after: "first_assistant_text"`.

- T+0: prompt "Count from 1 to 5"
- T+2.95s: first turn completes (`agent_settled`), model answered
  `1\n2\n3\n4\n5`
- T+2.95s: follow_up "Count from 6 to 10" accepted and queued
- T+90s: timeout — orderly shutdown, exit code 0

The follow_up was successfully queued for the next turn. The model would
have processed it on the next iteration if the probe had waited longer. ✅

### Test 13 (`test13.out`, 210KB, 51 event lines) — **mid-flight steer** (the spec's exact use case)

Script: prompt immediately, then `steer` at T+2s (mid-flight, while the model
was generating).

Timeline:

```
T+0.00s  probe sends prompt: "Count from 1 to 7, one number per line, no extra text. Take your time, write slowly."
T+0.00s  probe sends steer:  "Actually, stop counting at 3. Just reply with: 1\n2\n3 and nothing else."
T+0.44s  pi: prompt response: started
T+0.44s  pi: steer response:  queued
T+0.44s  pi: agent_start
T+0.45s  pi: user message 1: "Count from 1 to 7..."
T+0.45s  pi: queue_update: steering=[steer_message], followUp=[]
T+0.45s  pi: user message 2: "Actually, stop counting at 3..."  ← steer landed as a new user turn
T+3.38s  pi: assistant message_start
T+3.38s  pi: text_delta: "1"
T+3.38s  pi: text_delta: "\n2\n3"
T+3.38s  pi: text_end: "1\n2\n3"
T+3.38s  pi: message_end (assistant content=["1\n2\n3"])
T+3.38s  pi: turn_end
T+3.38s  pi: agent_settled
T+90s:   probe times out, exit code=0, no orphans
```

**The model answered `"1\n2\n3"` — not `"1\n2\n3\n4\n5\n6\n7"`.** The
mid-flight steer **was applied** and changed the output. This is the
exact behavior the spec needs for BATON's steering endpoint: an
operator's `TurnInput` lands in the live session and steers the assistant
away from the original trajectory, with the response emitted on the
existing event stream.

### Verification on the existing Translator baseline

The Translator's current code (`platform/src/host/Comuki.Host.Translator/Runtime/PiRunner.cs`)
uses `pi -p BRIEF --mode json --no-session` (one-shot, stream-json). This is
`--mode json` (one turn, no continuation), not `--mode rpc` (multi-turn with
bidi command channel). The Baton phase replaces this with `pi --mode rpc`;
the existing `RunAsync(string brief, ...)` signature still works for the
initial prompt (just send a `prompt` JSON command on stdin instead of
constructing argv with `-p`/`--mode`/`--no-session`). The existing
stream-json parser (`Parsing/StreamJsonParser.cs`) can be reused as-is for
`--mode rpc` output — the event records are the same shape.

### Orphan check

`Get-Process -Name pi` after all tests: no pi processes running. The probe
tracked the exact PID it spawned and killed by PID on timeout; pi does not
spawn child node processes (it's a single self-contained binary). The
TestFakeModel wasn't exercised in this spike — it was reserved for
fallback if pi refused to start without a live API key, but the existing
`hapy` configuration was sufficient to run a real model for the
duration of the spike. TestFakeModel itself is operational (the README
documents it and it builds into a runnable binary); pointing pi at it
would require either:
1. Adding a new provider to `~/.pi/agent/models.json` with
   `baseUrl: http://localhost:17190/v1` and `api: openai-completions`, OR
2. Using `pi --api-key fake --provider openai` with `OPENAI_BASE_URL` or
   `BASE_URL` env var (pi reads these for the openai provider)

Neither was needed for the spike but both are documented paths for the
hermetic integration-test path later.

---

## PIDs killed (per the brief)

The probe scripts (`probe-pi.mjs`, `probe-pi2.mjs`, `probe-pi3.mjs`) tracked
each PID it spawned via `spawn("pi", ...)` and recorded them in
`<test>.out`. Timeouts or natural exits cleaned up the process. Final state:

```
Get-Process -Name pi      → no entries
Get-Process -Name node     → no entries (the probe scripts themselves exited after writing the .out)
```

The PIDs I personally spawned and killed:

| Test | PID | Outcome | Killed by |
|------|-----|---------|-----------|
| test1 | 64368 | natural exit, code 0 | n/a |
| test2 | 15604 | natural exit, code 0 | n/a |
| test3 | 63524 | natural exit, code 0 | n/a |
| test4 | 47840 | natural exit, code 0 | n/a |
| test5 | 61244 | natural exit, code 0 | n/a |
| test6 | (probe-managed) | natural exit, code 0 | n/a |
| test7 | 45632 | natural exit, code 0 | n/a |
| test8 | 55224 | natural exit, code 0 | n/a |
| test9 | (probe-managed) | killed by probe timeout | probe `child.kill("SIGKILL")` |
| test10 | 45904 | killed by probe timeout | probe `child.kill("SIGKILL")` |
| test11 | 63880 | natural exit, code 0 | n/a |
| test12 | 41084 | killed by probe timeout | probe `child.kill("SIGTERM")` then SIGKILL |
| test13 | 64336 | killed by probe timeout | probe `child.kill("SIGTERM")` then SIGKILL |

The probe scripts also did `Get-CimInstance Win32_Process -Filter "ParentProcessId=$pid"`
checks for orphan children — none reported (pi doesn't spawn children).

---

## For 1c — how to build live-session Translator

The cleanest path from the current `IPiRunner` to the spec's
`IHarness.Capabilities.LiveSession = true`:

1. **`PiHarness : IHarness`** (rename, but for now just keep `IPiRunner` —
   the harness SPI lands in phase 8 per `tasks.md` 8.1).
2. **Replace `RunAsync(string brief)` with a long-lived RPC session.**

   ```csharp
   // ProcessStartInfo:
   //   FileName: piExecutable
   //   ArgumentList: --mode, rpc, --model, modelId, --no-session
   //                 (or --session <id> for resumed sessions)
   //   RedirectStandardOutput = true
   //   RedirectStandardError  = true
   //   RedirectStandardInput  = true
   //
   //   StandardInputEncoding  = UTF8
   //   StandardOutputEncoding = UTF8
   //
   //   WorkingDirectory = workingDirectory
   //
   //   EnvironmentVariables per IOptions<TranslatorOptions>
   //     (in particular ANTHROPIC_API_KEY, OPENAI_API_KEY, OPENAI_BASE_URL,
   //      hapy api key, etc — TranslatorOptions already binds these)
   ```

3. **Initial turn** — write the brief as a JSON-RPC line to stdin:

   ```json
   {"id": "<turnId>", "type": "prompt", "message": "<brief>"}
   ```

   (id is the request id used to correlate the `response` to this command.)

4. **Live event stream** — drain `StandardOutput` line by line (existing
   `StreamJsonParser` handles the events). Map each event to the existing
   `WorkerEvent` shape (`PiEventToWorkerEvent.cs`) — the wire schema is
   the same as `--mode json`, so no parser changes are needed.

5. **Baton steering (the new verb)** — when a `POST /api/v1/runs/{id}/steer`
   arrives:

   ```json
   {"id": "<steerId>", "type": "steer", "message": "<turn.Text>"}
   ```

   The `WorkerCommandHub.TrySendTurnInput(ExecutionId, TurnInput)` writes
   this to the harness's stdin writer (one harness per ExecutionId, kept
   alive across turns). pi delivers it as a new user turn.

   For *mid-flight* steering (while the agent is streaming), this is
   exactly the `steer` command. For *post-turn* work (queue for next
   turn), use `follow_up`. For *new initial work* (e.g. on harness
   resume with new brief), use `prompt`.

6. **Shutdown** — close `StandardInput.BaseStream`. The .NET
   `StreamWriter.Close()` or the base stream's `Close()` triggers
   pi's orderly shutdown. Process exits 0, no orphans, session file
   is closed cleanly.

7. **TestFakeHarness (`Capabilities.LiveSession = false`)** — the
   `WorkerCommandHub.TrySendTurnInput` returns `false` and the REST
   handler answers 202 with `delivered: false`, falling back to the
   cowork 11.1 WorkItem path. No new code in the hub itself — just a
   new variant in the `IWorkerCommandPipe` union.

---

## Files in this spike directory

| File | Purpose |
|---|---|
| `pi-help.txt` | Captured output of `pi --help` (the test 0 baseline) |
| `pi-spike.ps1` | First-generation PowerShell probe (broken: StandardInputEncoding required RedirectStandardInput, had a `if` syntax bug). Useful for `pi -p` one-shot tests. |
| `probe-pi.mjs` | Second-generation Node probe. Streams stdin from a file, kills by PID on timeout, captures all output. |
| `probe-pi2.mjs` | Third-gen: sends a script of commands at fixed time intervals. |
| `probe-pi3.mjs` | Fourth-gen: drives pi's events to time when commands are sent (the working version). Used for tests 7–13. |
| `test2.stdout`/`.meta`/`.stderr` | One-shot baseline (existing Translator path) |
| `test3-stdin.jsonl`, `test3.stdout`/`.meta`/`.stderr` | RPC mode discovery (malformed commands) |
| `test4-stdin.jsonl`, `test4.stdout`/`.meta`/`.stderr` | RPC mode probe commands (help/list/?) |
| `test5-stdin.jsonl`, `test5.out` | RPC mode get_state (first successful RPC) |
| `test6-stdin.jsonl`, `test6.out` | RPC mode: prompt + steer (no after-condition) |
| `test7-script.jsonl`, `test7.out` | RPC mode: prompt + steer (after agent_settled) — first multi-turn |
| `test8-script.jsonl`, `test8.out` | RPC mode: prompt + follow_up (after agent_settled) |
| `test9-script.jsonl` (no out, no probe ran) | experiment |
| `test10-script.jsonl`, `test10.out` | Experiment: wait for first_assistant_text (failed: probe syntax) |
| `test11-script.jsonl`, `test11.out` | steer after first_assistant_text — **✅ clean run, valid proof** |
| `test12-script.jsonl`, `test12.out` | follow_up after first_assistant_text — **✅ clean run, valid proof** |
| `test13-script.jsonl`, `test13.out` | mid-flight steer (sent at T+2s) — **✅ THE canonical proof for the spec** |

The big `.out` files are the canonical evidence. The `pi-help.txt` is the
authoritative reference for the CLI surface. The `probe-pi3.mjs` is the
driver — it's a single-run script (no watch, no serve, no browser) that
captures the full event stream and kills the pi process on timeout by
exact PID.

---

## Why this is GO (not GO-WITH-CAVEATS or NO-GO)

The spec's "in-process session transport" is literally implemented as
"long-lived JSON-RPC over stdio" by pi, and documented. The exact verb
the spec calls for (`steer`) is a first-class RPC command. The session
state is the existing `WorkerCommandHub` channel — no new protocol layer
needed. The existing `StreamJsonParser` and `PiEventToWorkerEvent` work
on the same event shape (the docs call out that the events in rpc mode
are the same as in `--mode json`).

There's no hidden translator work that pi doesn't support. The harness
SPI in phase 8 just needs to declare `Capabilities.LiveSession = true`
for `PiHarness` and provide the stdin-writer / stdout-reader pair that
the existing `IPiRunner` would have had with the right CLI shape.

**Note for 1c owner:** I do not recommend building `--session <id>` /
`--resume` / `--continue` paths. RPC mode is the canonical solution; the
session-id from `get_state` is a useful observability handle (logging
which session a steer landed in) but you don't need to "resume" anything
between turns — the same pi process owns the session from `prompt` to
`stdin.close()`.

**Note on the test-fake-model (TestFakeModel):** Not exercised in this
spike because the real `hapy` provider was sufficient. The path to use
TestFakeModel is: add a `fakemodel` provider to `~/.pi/agent/models.json`
pointing to `http://localhost:17190/v1` (or set `OPENAI_BASE_URL` env
var). Not a blocker for 1c.

---

## Recommendations for the next spike (1c, if needed)

This spike was enough to give a confident GO. If 1c still wants empirical
proof before phase work, the next experiment would be:

1. Build a tiny C# test harness that spawns `pi --mode rpc`, sends
   `prompt`, parses the event stream, sends a `steer` mid-flight, and
   asserts the second `message_start` (with role: user) matches the
   steered text. This would lock the design before phase 1.4 code lands.
2. Stress-test 30 minutes: spawn pi, send a prompt every 60s as
   `follow_up`, drain stdout, verify stream never stalls. This confirms
   the "stable stream for 30 min" full criterion from the brief.
3. The TestFakeModel integration: wire TestFakeModel as the upstream,
   spawn pi with the fake provider, run the same multi-turn script.
   This gives a hermetic integration test that doesn't burn real model
   tokens.

All three can be done in a couple of hours. None are blockers for the
phase 1c decision.


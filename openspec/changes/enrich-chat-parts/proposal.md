## Why

`chat_messages.content` is a single `varchar(8000)`, so rich payloads are
smuggled by string concatenation: the turn journalist glues the plan JSON
onto the reply with a blank line. A message cannot carry prose, three tool
calls and a continuation — today that is five unrelated rows, and the
console has to guess which ones belong together.

Two more gaps ride along. The chat module was the last place in the
solution persisting enums as `integer` (`chat_messages.role`,
`chat_sessions.status`); the other 25 configurations store strings. And
`IBrainClient` had exactly one implementation — an in-process stub — even
though the real agent loop already ships as `Comuki.Host.Brain` behind a
contract-first gRPC surface.

## What Changes

- Add a `MessagePart` discriminated union to `Shared.Contracts/Chat`
  (`text`, `code`, `diagram`, `thinking`, `tool`, `handoff`, `plan`),
  discriminated on the wire by `kind`, camelCase under the frozen web
  JSON options.
- Add two nullable jsonb columns to `chat_messages`: `parts` (the ordered
  array) and `meta` (model, tokens in/out, cost micros, latency, stop
  reason). `content` stays, now written by exactly one method as the flat
  text projection of the parts — the memory digest, search and every
  pre-parts client keep reading one column.
- Journal a turn as parts: the reply and its plan card become two parts of
  ONE assistant row; the brain's progress fragments become the row's
  thinking part; `create_ticket` is journalled as a tool part carrying its
  arguments, status, observation and duration.
- Store `ChatMessageRole` and `ChatSessionStatus` as strings, with a
  backfill in the same migration.
- Add the gRPC `IBrainClient` implementation and select it when
  `brain:endpoint` is configured; `BrainStub` stays the fallback when it is
  not, so a single-container install still boots. A configured-but-down
  brain answers 503 `chat.brain_unavailable` instead of an invented reply.

## Capabilities

### Modified Capabilities

- `chat`: message parts + metadata, the flat-content invariant, string
  enum storage, the live brain port and its unavailable mapping.

## Impact

`Shared.Contracts/Chat`, `Modules.Chat` (domain, application, infrastructure
+ one migration), `Comuki.Host/Chat`. The transcript API grows two optional
fields; nothing existing changes shape. One line of `HostComposer` selects
the brain port.

## Non-goals

- `question` and `decision` parts (the interactive P2 kinds). The union is
  left open to them; nothing emits or stores them yet.
- Streaming parts to the console as they are produced. A turn is still
  journalled once, at the end.
- Populating `meta` from the model. The column, contract and read path
  land here; the brain does not report tokens or cost yet.
- Widening `content` beyond 8000 characters — the projection clamps.
- Retro-filling `parts` for rows written before this change; they keep
  reading through `content`.

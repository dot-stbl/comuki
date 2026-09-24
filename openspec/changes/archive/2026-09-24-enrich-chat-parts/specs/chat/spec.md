## Purpose

Defines the operator chat surface: subject-owned sessions, Voluta graph turns
with plan-approve interrupts, transcript paging, and the merged slash-command
catalog. Chat is the human loop into Brain/Orchestration — not a free-form
LLM playground.

## ADDED Requirements

### Requirement: Messages are lists of parts
A transcript row SHALL be able to carry an ordered list of message parts in
a nullable `parts` jsonb column. Each part SHALL be discriminated on the
wire by a `kind` property from the closed set `text`, `code`, `diagram`,
`thinking`, `tool`, `handoff`, `plan`. Prose, tool records and a plan card
produced by one turn SHALL be parts of ONE row, never separate rows and
never concatenated into one string. A row written before parts existed, or
whose payload this build cannot read, SHALL surface with no parts rather
than failing the read.

#### Scenario: One turn, one row, many parts
- **WHEN** a turn produces an assistant reply and a plan awaiting approval
- **THEN** the transcript gains one assistant row whose parts are the prose
  and the plan, and the plan part carries the same nodes and edges as the
  pending approve card

#### Scenario: Tool call is a record, not a payload dump
- **WHEN** the graph applies an approved plan through `create_ticket`
- **THEN** the tool row carries one `tool` part with the tool name, the
  arguments it was called with, a status of `running` | `success` |
  `failed`, the observation and the call duration

#### Scenario: Unknown kind degrades
- **WHEN** a row's `parts` payload carries a `kind` this build does not know
- **THEN** the row reads with no parts and its `content` is returned
  unchanged

### Requirement: Content is the flat projection of the parts
`chat_messages.content` SHALL remain populated on every row and SHALL be
the flat markdown projection of that row's parts, written by a single
journalling seam. No other code path SHALL compose `content`. The
projection SHALL be clamped to the column bound rather than failing the
turn. Readers that predate parts — the memory digest that feeds the brain
the recent history window, transcript search, and any API client reading
only `content` — SHALL keep working unchanged.

#### Scenario: Digest still reads one column
- **WHEN** the think node assembles the brain context from the recent
  history window
- **THEN** each entry is that row's `content`, and a row whose parts
  included a plan still shows the plan in its projection

### Requirement: Message cost metadata
A transcript row SHALL be able to carry a nullable `meta` jsonb object
holding the model id, prompt and completion token counts, cost in USD
micros, latency in milliseconds and the stop reason. Every field SHALL be
optional, so a row no model produced carries none of them.

#### Scenario: Metadata is optional
- **WHEN** a row is journalled without cost information
- **THEN** `meta` is absent and the row is valid

### Requirement: Live brain port
The orchestrator SHALL call the brain host over the contract-first gRPC
`IBrainService.Think` stream when `brain:endpoint` is configured, draining
progress chunks as the turn's reasoning and the final chunk as the result.
When no endpoint is configured the host SHALL fall back to the in-process
brain stub and boot normally. When an endpoint is configured but the call
does not complete, the turn SHALL answer 503 with the stable code
`chat.brain_unavailable`, and the orchestrator SHALL NOT substitute an
invented reply.

#### Scenario: No brain configured
- **WHEN** the host starts with no `brain:endpoint`
- **THEN** it boots and chat turns are answered by the in-process stub

#### Scenario: Brain configured but unreachable
- **WHEN** `brain:endpoint` is set and the brain host does not answer
- **THEN** the turn answers 503 with code `chat.brain_unavailable` and the
  detail names no endpoint or credential

## MODIFIED Requirements

### Requirement: Voluta graph checkpoints
Each session SHALL keep a Voluta graph checkpoint in `chat_checkpoints`
(jsonb state + pending interrupt). The transcript SHALL be append-only
`chat_messages` with role, body, optional parts and optional metadata.
`chat_messages.role` and `chat_sessions.status` SHALL be persisted as the
enum member name (string), not as an ordinal, so a reordered enum cannot
relabel history. Checkpoints SHALL make sessions resumable across host
restarts.

#### Scenario: Resume after restart
- **WHEN** the host restarts mid-session
- **THEN** the next turn loads the stored checkpoint and continues from the
  pending node rather than resetting the graph

#### Scenario: Roles read in the database
- **WHEN** a transcript row is inspected directly in PostgreSQL
- **THEN** `role` reads `User` | `Assistant` | `System` | `Tool` and
  `status` reads `Active` | `Archived`

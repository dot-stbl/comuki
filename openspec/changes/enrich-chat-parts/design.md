## Design

### The union

`Comuki.Shared.Contracts.Chat.MessagePart` — an abstract record with a
private constructor and nested sealed records, the shape
`Comuki.Host.Translator.Parsing.PiEvent` already uses. Polymorphism is
declarative: `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]`
plus one `[JsonDerivedType]` line per kind, keys in `MessagePartKinds`.
The private constructor closes the union to nested records, so adding the
P2 `question` / `decision` kinds is two lines in this file and nothing
else.

`PlanPart` reuses `Plans.PlanNode` / `Plans.PlanEdge` rather than
re-declaring them: a plan rendered in a message and a plan queued as a run
cannot drift.

Serialization goes through `MessagePartsJson` on the frozen
`JsonSerializerOptions.Web`. Reads are tolerant — an absent, malformed, or
newer-than-this-build payload answers "no parts", never a fault, because
`content` is always there to fall back on.

### Storage

Two nullable jsonb columns on `chat_messages`, held on the entity as
strings (`PartsJson`, `MetaJson`) — the shape `intake.admission_rules`
(`filter`) and `scheduler.scheduled_jobs` (`brief_json`) already use.
The parts contract lives in `Shared.Contracts`, which the domain must not
reference; the string keeps the module boundary intact and the payload
shape is enforced at the one seam that writes it.

`ChatTranscriptRow.Of` is that seam: it is the only place a transcript row
is built, it serializes the parts and it derives `content` from them
(`MessagePartText.Flatten`, clamped to the column bound). Nothing else in
the module composes `content` by hand — that is what let the plan JSON get
glued onto the reply.

### Enum columns

`HasConversion<string>()` + `HasMaxLength(16)` on `chat_messages.role` and
`chat_sessions.status`. EF cannot express the PostgreSQL `USING` clause an
`integer -> character varying` cast needs, so the generated migration is
seeded by the tool and a `Sql()` backfill is added ahead of each
`AlterColumn`, with the inverse in `Down()`. The enum members do not
change.

### The brain port

`BrainGrpcClient` drains the server-streaming `IBrainService.Think`:
progress chunks become `BrainReply.Chunks` (and then the turn's thinking
part), the final chunk's payload becomes `FinalJson`. `RpcException` is
translated to `BrainUnavailableException` at that boundary and mapped to
503 by `ChatEndpointRunner`; the message never carries the endpoint.

`AddChatBrainClient` registers it only when `brain:endpoint` is set, and
runs before the composition's `TryAddSingleton<IBrainClient, BrainStub>()`
— so an unconfigured install boots on the stub and the branch lives in one
place.

Voluta wraps a node's exception in `GraphRunFailedException`, which would
have hidden the typed fault behind a generic 500. `ChatTurnService` now
unwraps it on the invoke path, matching what the resume path already did.

### Non-goals

- `question` / `decision` parts (P2).
- Streaming parts as they are produced.
- Populating `meta` from the model.
- Back-filling `parts` for pre-existing rows.

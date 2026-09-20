# C# Producer JSON Fixtures

These fixtures are hand-written, byte-faithful reproductions of what the
C# producer (`platform/src/host/Comuki.Host`) would serialise via the
default ASP.NET Core `System.Text.Json` web options:

- camelCase property names (the .NET default since the `AddJsonOptions`
  web defaults).
- ISO 8601 timestamps with a real offset
  (`2026-09-20T10:15:30.125+00:00` / `+02:00`). ASP.NET Core's default
  serializer writes `DateTimeOffset` with the offset of the value, not
  `Z`, unless the value was constructed from UTC and the runtime emits
  `Z`. The fixtures honour the C# record's authoring intent: a UTC
  offset for stable wire snapshots.
- Null fields emitted as JSON `null` (not omitted) — the kernel
  codecs normalise both to `null` on the kernel side.
- `JsonDocument` (the `pendingPlan` field) is serialized as its
  underlying JSON structure.
- Numeric scalars (counts, durations) are emitted as numbers — never
  strings. Kubb types declare `number | string` because the OpenAPI
  integer format is loose; the producer emits numbers.

Each fixture carries a sibling `_<file>.note` field at the top with
provenance:

- which C# record it mirrors (`platform/src/host/Comuki.Host/...`);
- the ASP.NET Core JSON serializer assumption (above);
- the exact `DateTimeOffset` value the fixture was anchored to
  (handy for the unix-ms assertion in the codec test).

## Files

| Fixture | Source record |
|---------|---------------|
| `chat-session-view.json` | `Comuki.Host.Chat.Models.Views.ChatSessionView` |
| `chat-message-view.json` | `Comuki.Host.Chat.Models.Views.ChatMessageView` (parts populated, one of every kind) |
| `chat-message-view-nulls.json` | Same record; pre-parts-era row with `parts`, `meta`, `toolName` all `null` |
| `chat-messages-page-view.json` | `Comuki.Host.Chat.Models.Views.ChatMessagesPageView` |
| `chat-turn-result-view.json` | `Comuki.Host.Chat.Models.Responses.ChatTurnResultView` (awaiting approval, plan present) |
| `chat-turn-result-view-plan-null.json` | Same record; `pendingPlan` `null`, no approval |
| `chat-chunk.json` | `Comuki.Shared.Contracts.Realtime.ChatChunkView` (SignalR hub) |
| `chat-turn-complete.json` | `Comuki.Shared.Contracts.Realtime.ChatTurnCompleteView` |
| `problem-details-404.json` | RFC 9457 `application/problem+json` (404) |
| `problem-details-409.json` | RFC 9457 (409) |
| `problem-details-429.json` | RFC 9457 (429) |
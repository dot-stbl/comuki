## 1. Contract

- [x] `MessagePart` union + `MessagePartKinds` + `ToolPartStatuses` in
      `Shared.Contracts/Chat`
- [x] `ChatMessageMeta` (model, tokens in/out, cost micros, latency, stop
      reason)
- [x] `MessagePartsJson` (serialize + tolerant parse for both columns)
- [x] `MessagePartText` — the one flat projection, with the column clamp

## 2. Storage

- [x] `ChatMessage.PartsJson` / `ChatMessage.MetaJson` + the content bound
      on the aggregate
- [x] `parts` / `meta` jsonb columns in `ChatMessageConfiguration`
- [x] `role` / `status` as strings (`HasConversion<string>()` +
      `HasMaxLength(16)`)
- [x] Migration `ChatMessageParts` with the `USING` backfill in both
      directions

## 3. Journalling

- [x] `ChatTranscriptRow.Of` — the single seam that writes a row
- [x] Journalist emits parts: digest text, tool part, thinking + prose +
      plan on ONE assistant row
- [x] `ActNode` records the tool arguments, status and duration
- [x] `ThinkNode` keeps the brain's progress fragments as the thinking part
- [x] `ChatTurnService` seeds user rows as parts and unwraps
      `GraphRunFailedException`

## 4. Live brain

- [x] `BrainGrpcClient` over `IBrainService.Think` (streaming drain)
- [x] `BrainClientOptions` (`brain:endpoint`) + `AddChatBrainClient`
- [x] `BrainUnavailableException` → 503 `chat.brain_unavailable`
- [x] `BrainStub` stays the fallback when no endpoint is configured
- [x] Verified `RouteNode` still emits only `BrainRequestKindKeys` values

## 5. Read surface

- [x] `ChatMessageView.Parts` / `.Meta`
- [x] OpenAPI regenerates with the `kind` discriminator mapping

## 6. Verification

- [x] `dotnet build comuki.slnx -c Debug` — 0 warnings, 0 errors
- [x] `dotnet run --project tests/unit/Comuki.Modules.Chat.Unit` — green
- [ ] Integration suite (Testcontainers) — migration apply/rollback on a
      real Postgres
- [ ] Sync the delta into `openspec/specs/chat/spec.md` and archive

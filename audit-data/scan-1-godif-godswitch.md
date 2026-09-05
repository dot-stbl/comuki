# Scan 1 — Godif / godswitch detection

**Scan tool:** PowerShell `Get-ChildItem` + regex over 899 .cs files in
`platform/src/**`.

**Thresholds (per task brief):**

| Smell | Threshold | Finding |
|-------|-----------|---------|
| File >800 lines | 800 | **0 hits** (max 384 — `Mcp/McpServer.cs`) |
| Class >30 public methods | 30 | **0 hits** (max 13 — `IIntakeStore` / `IntakeStore`) |
| Class with >8 ctor params | 8 | **1 candidate** (see below) |
| Method >10 branches (godswitch) | 10 | **0 hits per-method** (PlanValidator has 19 across methods but every method is shallow) |

## 1.1 Top 15 largest files (`platform/src/**`)

| Lines | Path |
|------:|------|
| 384 | `platform/src/host/Comuki.Host/Mcp/McpServer.cs` |
| 305 | `platform/src/host/Comuki.Host.Translator/Parsing/StreamJsonParser.cs` |
| 298 | `…/modules/Intake/…/Migrations/20260903141326_InboundTicketKind.cs` *(migration, excluded)* |
| 297 | `platform/src/host/Comuki.Host/HostComposer.cs` |
| 295 | `…/modules/Intake/…/Migrations/IntakeDbContextModelSnapshot.cs` *(migration)* |
| 293 | `…/modules/Intake/…/Migrations/20260902040700_InitialIntakeSchema.cs` *(migration)* |
| 293 | `…/modules/Intake/…/Migrations/20260903085546_UseSchemas.cs` *(migration)* |
| 278 | `…/modules/Memory/…/Persistence/Stores/EfMemoryStore.cs` |
| 270 | `platform/src/shared/Comuki.Shared.Filtering/Evaluator/FilterOperatorRegistry.cs` |
| 253 | `…/modules/Intake/…/Persistence/Stores/IntakeStore.cs` |
| 251 | `…/modules/Identity/…/Migrations/20260904162022_AddOidcState.cs` *(migration)* |
| 248 | `…/modules/Identity/…/Migrations/IdentityDbContextModelSnapshot.cs` *(migration)* |
| 232 | `…/modules/Memory/…/Migrations/20260905120000_AddPgvectorKnowledgeSchema.cs` *(migration)* |
| 230 | `…/modules/Memory/…/Migrations/MemoryDbContextModelSnapshot.cs` *(migration)* |
| 225 | `platform/src/host/Comuki.Host/Auth/Controllers/IdentityAdminController.cs` |

Excluding EF migrations (`**/Migrations/**` is design-time generated,
excluded from analysis per `ef-migrations.md`), the production source top
is McpServer.cs at **384 lines** — well under the 800 threshold.

## 1.2 Top 10 most-complex files (sum of `if`/`case`/foreach branches)

| Branches | Path | Notes |
|---------:|------|-------|
| 19 | `…/Shared.Contracts/Plans/PlanValidator.cs` | flat validation, not a godswitch |
| 17 | `…/Host/Mcp/McpServer.cs` | dispatcher + tool handlers |
| 12 | `…/Shared.Filtering/Parser/FilterParser.cs` | recursive-descent, ≤3 branches/method |
| 11 | `…/Host.Translator/Parsing/StreamJsonParser.cs` | streaming-state machine |
| 11 | `…/Host/ControlPlane/Parsing/ControlPlaneDocumentParser.cs` | YAML-frontmatter |
| 11 | `…/modules/Knowledge/…/Chunking/Chunker.cs` | chunk strategies |
| 10 | `…/modules/Intake/…/Tickets/WebhookIntakeService.cs` | webhook providers |
| 10 | `…/Shared.Filtering/Lexer/FilterFunctions.cs` | token-kind dispatch |
| 10 | `…/Host.Brain/ControlPlane/ControlPlaneProfileCatalog.cs` | profile builders |
|  9 | `…/Shared.Filtering/Lexer/FilterLexer.cs` | lexer |

Even the worst file (PlanValidator.cs) **does not** have a godswitch —
its 19 branches split over 4 methods (`Validate`, `FindCycleNode`,
`Visit`, deduplication loop), all using early-return style with depth ≤2.
No method hits >10 branches.

## 1.3 Top 10 classes by public-method count

| Methods | Path |
|--------:|------|
| 13 | `…/Modules/Intake/Application/Ports/Tickets/IIntakeStore.cs` *(port)* |
| 13 | `…/Modules/Intake/Infrastructure/Persistence/Stores/IntakeStore.cs` *(impl)* |
|  8 | `…/Host/Intake/Controllers/SourcesController.cs` |
|  8 | `…/Modules/Memory/Infrastructure/Persistence/Stores/EfMemoryStore.cs` |
|  7 | `…/Host/Auth/Controllers/IdentityAdminController.cs` |
|  7 | `…/Shared.Telemetry/ComukiTelemetry.cs` |
|  6 | `…/Modules/Memory/Infrastructure/Persistence/MemoryDbContext.cs` |
|  6 | `…/Shared.Filtering/Ports/FilterableFieldSet{TEntity}.cs` |
|  6 | `…/Host/Chat/Controllers/ChatSessionsController.cs` |
|  6 | `…/Engine.Orchestration/Infrastructure/Queue/WorkItemQueueEf.cs` |

The 13-method pair (IIntakeStore/IntakeStore) is a port + implementation
with one CRUD method per aggregation component — coherent shape, **not**
a god-class smell.

## 1.4 Classes with >8 ctor parameters (≥8 dependencies)

| Ctor deps | Class |
|----------:|-------|
| **13** ⚠ | `platform/src/host/Comuki.Host/Auth/Controllers/IdentityAdminController.cs` |
|  9 | `platform/src/host/Comuki.Host/Auth/Controllers/AuthController.cs` |
|  9 | `platform/src/host/Comuki.Host.Translator/Execution/Loop/TranslatorLoop.cs` |
|  9 | `platform/src/host/Comuki.Host/Auth/Oidc/OidcCallbackHandler.cs` |
|  8 | `platform/src/engine/Comuki.Engine.Compute/Supervision/ScaleSupervisorCycle.cs` |
|  8 | `…/Host/Auth/Security/ApiKeyAuthenticationHandler.cs` |
|  7 | `…/Host.Translator/Grpc/WorkerGrpcService.cs` |

The single **godif candidate (IdentityAdminController, 13 ctor params)**
is the only borderline violation — it's also registered in `class-layout-and-tooling.md`
§1a/godif-landscape as a fanout controller.

## 1.5 Godswitch candidates

**None.** PlanValidator.Validate is the worst file (19 branches across
methods) but every method is shallow and intent-clear; cycles via DFS
_inherently_ need a switch. McpServer.CallToolAsync uses a switch
expression over tool names — explicitly the recommended pattern from
`anti-patterns.md` §1.3.

## 1.6 Recommended refactors

### IdentityAdminController (godif-flagged, 13 ctor params)

**File:** `platform/src/host/Comuki.Host/Auth/Controllers/IdentityAdminController.cs:29`

The controller fans out to 7 command handlers + 6 `IValidator<>` instances
+ `ILogger`. Each action consumes a different subset of the deps.

**Recommended refactor:**

1. Move validators to **per-action `[FromServices]`** (per
   `anti-patterns.md` §5) — drops ctor from 13 → 8 dependencies.
2. Consider splitting along resource axis: `UsersAdminController`,
   `GrantsAdminController`, `KeysAdminController`, `OidcLinkController`
   (per `api-design.md` §6 "Hybrid resource controller + lifecycle
   controller"). Each sub-controller takes 2-3 handlers.

If neither refactor is adopted, at minimum the file size (260 lines) and
ctor count should be flagged in code review.

### Knowledge edges (see Scan 4)

Cross-module edges (Knowledge→Memory, Proxy→Costs) make any single
class to behave like a godif even though no individual file hits the
800-line bar. Documented separately under architecture-drift findings.

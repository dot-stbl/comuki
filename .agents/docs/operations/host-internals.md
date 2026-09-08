# Host internals — `HostComposer.Compose`

> Single composition point of the orchestrator host. Lives at
> `platform/src/host/Comuki.Host/HostComposer.cs`. `Program.cs` resolves the
> database connection once via [`HostDatabase.Resolve`](./install.md)
> and flows it into `Compose`; integration tests boot the exact same
> composition on a test port (InternalsVisibleTo test project). Read this
> when wiring a new module, registering a new background service, or
> debugging "the host doesn't see my service" issues.

## 1. The composition order (top-to-bottom, intentional)

`Compose(builder, database)` follows a fixed pipeline. Each section is
the only place its concern lives; reordering breaks the wiring.

| Phase | What it wires |
|-------|---------------|
| **0 — Telemetry** | `AddComukiTelemetry(builder.Configuration)` — ActivitySource / Meter registration, OTLP exporter when `Telemetry:OtlpEndpoint` is set. |
| **1 — Control-plane catalog core** | `AddControlPlaneCatalogCore` — profile catalog reader (markdown files in `control-plane/profiles/`). |
| **2 — Identity** | `AddIdentityApplication` · `AddIdentityPersistence` · `AddIdentityAuth` (cookie + API-key + OIDC manual code-flow). |
| **3 — Projects** | `AddProjectsApplication` · `AddProjectsPersistence`. |
| **4 — Costs** | `IBudgetGate` host-composed `OrchestrationBudgetGate` · `IProjectBudgetSettings` adapter · `AddCostsApplication` · `AddCostsPersistence`. |
| **5 — Chat** | `AddChatApplication` · `AddChatPersistence` · `IBrainClient` (in-process stub fallback) · `IMemoryDigest` (empty fallback) · `IChatToolExecutor` (host-composed, scopes into orchestration) · `IRunsReader` adapter · `RunsListHandler` · approve/cancel ports · `ChatRunStarter`. |
| **6 — Intake** | `AddIntakeApplication` · `AddIntakePersistence` · `AddIntakeProviders` (Refit) · `IRunLauncher` adapter · `IRunStatusReader` adapter · `IntakeProfileRouter`. |
| **7 — Artifacts** | `AddArtifactsApplication` · `AddArtifactsPersistence` · `IRunArtifactJournalSource` adapter · `IRunArtifactRunSource` adapter · `RunArtifactPackagerHostService` (BackgroundService). |
| **8 — Knowledge** | `AddKnowledgeApplication` · `AddKnowledgePersistence` · `AddKnowledgeInfrastructure` (pgvector embeddings + `KnowledgeIngestBackgroundService`). |
| **9 — MCP server** | `McpServer` singleton — JSON-RPC 2.0 over `/api/v1/mcp`. |
| **10 — Scheduler** | `AddSchedulerApplication` · `AddSchedulerPersistence` · `ISchedulerDispatcher` adapter (host knows engine shape) · `SchedulerOptions` (poll interval) + `SchedulerWorkerDefaults` (image / profiles-ref). |
| **11 — Engine adapters** | `IProjectScaleSettings` adapter (live project settings back into compute scale). |
| **12 — OIDC + Auth** | `OidcOptions` binding · `ICookieSigner` host adapter · `OidcStateSweeper` BackgroundService (`Host:OidcSweep`). |
| **13 — Bootstrap admin** | `BootstrapAdminOptions.Resolve` · `BootstrapAdminSeeder` · `BootstrapAdminStartupService`. |
| **14 — Validators (FluentValidation)** | Request-level validators for admin endpoints (#31–#45). |
| **15 — Security pass** | `AddComukiCors` (dashboard allow-list) · `AddComukiRateLimit` (per-endpoint partitions). |
| **16 — OpenAPI** | `AddOpenApi` — runtime document at `/openapi/v1.json`; build-time mirror to `artifacts/openapi.json`. |
| **17 — Realtime** | `AddComukiRealtime` — `RunsHub` + `RunEventsBroadcastInterceptor` (appends to the orchestration `DbContext` interceptor chain). |
| **18 — Proxy** | `AddProxyApplication` · `AddProxyInfrastructure` (YARP + virtual-key auth). |
| **19 — Health checks** | `PostgresHealthCheck` (SELECT 1) · `ProxyKeysHealthCheck` (catalogue shape). Both registered under the `ready` tag. |
| **20 — Ambient subject scope** | `AsyncLocalSubjectScopeAccessor` singleton — read by every per-request query filter; written by `SubjectScopeMiddleware`. |
| **21 — Build + post-build** | `builder.Build()` · `HostDatabase.WarnLegacyAlias` · `ProductionSecretValidator.Validate` (runs in `Production` only — throws on dev-default secrets). |
| **22 — Middleware pipeline** | `UseExceptionHandler` · `UseAuthentication` · `UseAuthorization` · `UseCors` · `UseRateLimiter` · `UseMiddleware<SubjectScopeMiddleware>`. **Order is enforced** — `SubjectScopeMiddleware` must sit between `UseAuthorization()` and `MapControllers()`; a custom host that reorders gets `no subject scope` at the first authorized request (`runbook.md` §Troubleshooting). |
| **23 — Endpoint mapping** | `/health` · `/health/ready` · `MapControllers` · `MapProjectsEndpoints` · `MapCostsEndpoints` · `MapKnowledgeEndpoints` · `MapMcpEndpoints` · `MapComukiRealtime` · `MapProxyEndpoints` · `MapReverseProxy` (`/v1/chat/completions`, etc., virtual-key scheme). · `MapOpenApi` (runtime document). |

## 2. The host-composed adapter pattern

Modules don't reach into each other. Anything that needs to cross
the module boundary goes through an **adapter** registered in
`HostComposer`:

| Adapter (host) | Wires | Used by |
|----------------|-------|---------|
| `IProjectBudgetSettings` → `ProjectBudgetSettingsAdapter` | Reads project settings · feeds into `Costs` budget gate. | `IBudgetGate` (Costs) |
| `IBudgetGate` → `OrchestrationBudgetGate` | Cancels run + emits journal event when budget exceeded. | Orchestration cancel path |
| `IBrainClient` → `BrainStub` (fallback) | Real client ships in `Comuki.Host.Brain`; the host registers the stub when the brain process isn't running. | Chat tools (`brain.invoke`) |
| `IMemoryDigest` → `EmptyMemoryDigest` (fallback) | Same pattern — empty until the memory store ships. | Brain context assembly |
| `IChatToolExecutor` → `HostChatToolExecutor` | Scopes tools into orchestration + projects. | Voluta graph |
| `IRunsReader` → `OrchestrationRunsReader` | Reads runs from the orchestration schema (chat needs the run list). | Chat tools |
| `IApproveRunPort` → `HostApproveRunAdapter` | Approves a run from chat (`approve_run` tool). | Voluta graph |
| `ICancelRunPort` → `HostCancelRunAdapter` | Cancels a run from chat (`stop_run` tool). | Voluta graph |
| `IRunLauncher` → `IntakeRunLauncher` | Creates a run from an intake admission. | Intake module |
| `IRunStatusReader` → `OrchestrationRunStatusReader` | Sync-back: feeds run status into the intake tracker. | `RunStatusBridge` (intake) |
| `IRunArtifactJournalSource` → `OrchestrationArtifactJournalSource` | Reads journal rows from the orchestration schema for the bundle packager. | `RunArtifactPackager` |
| `IRunArtifactRunSource` → `OrchestrationArtifactRunSource` | Reads run metadata for the bundle. | `RunArtifactPackager` |
| `IProjectScaleSettings` → `ProjectScaleSettingsAdapter` | Live project settings back into compute scale decisions. | `ScaleSupervisor` |
| `ICookieSigner` → `CookieSignerAdapter` | Host owns the ASP.NET cookie plumbing; modules emit claims. | OIDC callback + login |
| `ISchedulerDispatcher` → `SchedulerRunLauncher` | Maps a fired scheduled job to a run claim. | `ScheduledJobDispatcherWorker` |

The pattern is always the same: **interface in the module, adapter in
the host, registration in `HostComposer`**. A module that imports
another module's namespace is a NetArchTest violation; cross-module
calls only happen via these adapters.

## 3. Background services (`IHostedService`)

Five host-owned background services run in `Compose`:

| Service | Interval / trigger | What it does |
|---------|---------------------|--------------|
| `BootstrapAdminStartupService` | Once at startup | Idempotent bootstrap admin seed (`auth:bootstrap` or `COMUKI_BOOTSTRAP_ADMIN_*`). |
| `OidcStateSweeper` | `Host:OidcSweep:Interval` (default 5 min) | Prunes abandoned OIDC `state` rows past TTL. |
| `RunArtifactPackagerHostService` | Two-phase poll (Scoped-lifetime) | Bundles `{brief,result,pins}.json` to MinIO on terminal runs. |
| `KnowledgeIngestBackgroundService` | (registered by `AddKnowledgeInfrastructure`) | Knowledge ingest queue drain. |
| `ScheduledJobDispatcherWorker` | `Scheduler:PollInterval` (default 30 s) | Polls `FOR UPDATE SKIP LOCKED` due jobs → dispatches ephemeral workers. |

`Scheduler` (S15) is the most recent; the dispatcher runs in-process
inside the host — there is no separate scheduler service. A future
slice may move it to its own host if the dispatch path becomes
CPU-bound.

## 4. Endpoint surface (v1)

| Route | Auth | Notes |
|-------|------|-------|
| `GET /health` | anonymous | Liveness; returns `{ status: "ok" }`. |
| `GET /health/ready` | anonymous | Readiness — Postgres `SELECT 1` + proxy-keys catalogue shape (both `ready` tag). |
| `POST /api/v1/auth/login` · `/logout` | anonymous | Cookie session. |
| `GET /api/v1/auth/me` | cookie / API-key | Returns the resolved session. |
| `GET /api/v1/auth/oidc/{provider}/start` · `/oidc/callback` | anonymous | Browser-driven OIDC flow (PKCE S256). |
| `/api/v1/projects` · settings · budgets | RBAC (`project:read|admin`) | |
| `/api/v1/runs` · approve · cancel · `/artifacts` | RBAC (`run:read|create|stop|inject`) | |
| `/api/v1/chat/sessions` · slash | RBAC (`chat:use`) | |
| `/api/v1/intake/{admission-rules,inbox,sources,tickets,webhooks}` | RBAC (`intake:read|claim`) | |
| `/api/v1/sources/{id}/{connect,update,probe,test-draft,test-connection}` | project-admin+ | Admin surface (#38–#42). |
| `/api/v1/costs/projects/{id}` | operator+ | |
| `/api/v1/controlplane/{profiles,chat-commands}` | member+ | Catalog reads. |
| `/api/v1/identity/{users,keys,grants}` | platform-admin | #31–#37, #45. |
| `/api/v1/knowledge/ingest` | `knowledge:write` | |
| `/api/v1/mcp` | cookie / API-key | JSON-RPC 2.0 (`search_knowledge`, `list_runs`). |
| `/api/v1/scheduler/projects/{id}/schedules` | project-admin+ | S15. |
| `/api/v1/host/grpc` | worker token | Translator gRPC channel. |
| `POST /api/hooks/{source}/{16-char-key}` | webhook signature | Provider-specific HMAC; secret in env only. |
| `/v1/{chat/completions,embeddings,messages,models}` | `Bearer vkey_…` | Proxy passthrough (YARP). |
| `GET /openapi/v1.json` | anonymous | Runtime OpenAPI document. |
| `GET /hubs/runs` (SignalR) | cookie / API-key | Realtime stream. |

## 5. Why `Program.cs` is `top-level` and `Program` is `internal`

Per `.agents/rules/csharp/code-shape.md` and `di-installer.md`, the host
is the **only** composition root. `Program.cs` calls `HostComposer.Compose`
and `Run`; integration tests reach the same composition through the
`internal HostComposer.Compose` + `InternalsVisibleTo` test assembly —
there is no `public partial class Program` for `WebApplicationFactory<Program>`.
This is a deliberate choice (`AGENTS.md` §"Critical patterns" #1): the
host's surface is one method, the test assembly is the only external
consumer, and a `public partial` would tempt test code to lean on
things it shouldn't.

## 6. Failure modes (operator-visible)

| Symptom | Where in `Compose` | Fix |
|---------|--------------------|-----|
| `no subject scope` exception | §22 middleware order | Confirm `UseMiddleware<SubjectScopeMiddleware>()` is between `UseAuthorization()` and `MapControllers()`. |
| `refusing to start the host in Production: … still on its committed dev default` | §21 `ProductionSecretValidator` | Set the env var (`Artifacts__Minio__SecretKey`, etc.) to a real secret. |
| `migration history table is missing` on `artifacts` (or any module) | Migrator ran without `EnsureSchema` for that schema | Run `dotnet run --project platform/src/host/Comuki.Migrator` — creates the per-schema history on first apply. |
| `OpenIdConnectConfiguration` rejects Keycloak 26+ discovery | §12 `OidcDiscoveryCache` (already hand-parses the four endpoints we need — strict STJ on the framework type rejects bool fields). | Nothing to fix; the cache uses `JsonDocument.Parse` and only reads `issuer`, `authorization_endpoint`, `token_endpoint`, `jwks_uri`. |
| Realtime hub handshake fails | SignalR connection is anonymous | Confirm `withCredentials: true` on the `@microsoft/signalr` `HubConnectionBuilder`. |

## 7. Source pointers

- `platform/src/host/Comuki.Host/HostComposer.cs` — the file itself.
- `platform/src/host/Comuki.Host/Program.cs` — calls `Compose(builder, HostDatabase.Resolve(configuration))`.
- `platform/src/host/Comuki.Host/HostDatabase.cs` — connection-string resolution.
- `tests/integration/Comuki.Host.Integration.*` — integration tests boot `HostComposer.Compose` on a test port (`WebApplicationFactory`).

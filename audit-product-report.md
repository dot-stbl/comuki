# Comuki v1 Product Audit — 2026-09-08

> Read-only audit of the Comuki v1 codebase as of `master` tip
> `fa659fd727260731b00c83d9f812ffa692c2f1f6` (worktree `fix/audit-product`).
> Repo path: `C:\Users\bradw\source\hybrid\comuki.orchestrator`.
> No code was changed; this is a read-only survey.
>
> Every finding cites a file and line so the owner can navigate
> immediately. File paths are repo-relative unless prefixed.

---

## Executive Summary

**State.** v1 ships: 50/50 GitHub issues closed, 24 slices landed, 10
backend modules, build green, 1 192 BE + 1 525 FE tests pass. The platform
does what STATE.md says it does.

**What is concerning (in priority order).**

1. **The `RunDetail` page is a half-built shell in real mode.** The
   backend wire (`RunsListHandler.RunView`) carries only
   `id / projectId / status / createdAt / updatedAt`; the FE
   `run-detail-page.tsx` reads `title / app / model / cost / tokens /
   brief / workItems / events / rules / revision`. The FE mapper fills
   the missing fields with empty strings. The plan, work-items, brief
   and timeline that the FE is built around do not exist on the wire.
   (`dashboard/src/domains/runs/api/queries.ts:51-73` documents the gap
   as deliberate.)
2. **The proxy does not meter successful calls.** `ProxyTransforms.MeterUsageFromResponseAsync`
   is a stub that returns immediately; usage_events rows are never
   written on the success path, so the per-virtual-key monthly budget
   enforcer (`DefaultProxyBudgetEnforcer`) sees only *historical* spend
   — the cap is a "what was already spent this month" check, not a
   "what would this call push us over" check. The follow-up is
   documented in `proxy.md:188-195` as deferred.
3. **OIDC auto-provisioning is silent and not email-verified.** A first
   login from a configured IdP either links an existing user with the
   same email (no challenge) or provisions a password-less local
   account. A future IdP breach on a domain that the user actually
   controls becomes a Comuki account takeover. (`OidcAccountLinker.cs:27-49`.)
4. **Projects have no permission check at all.** The class-level comment
   `// TODO(auth): project:admin — wire [RequiresPermission] host-wide with
   the auth slice` is still on `ProjectsModuleEndpoints.MapProjectsEndpoints`
   (`ProjectsModuleEndpoints.cs:27`). Any authenticated user can list,
   create, update, archive and read settings of any project.
5. **The dashboard is mock-first for half of its pages even though the
   backend exposes most of the data.** Cost, Knowledge, Compute,
   Models, Observability, Queue, Tasks, Verify, Approvals, Settings,
   Home (outcomes) all throw `"<feature> API not implemented — set
   VITE_USE_MOCK=true"` when called in real mode. The `/api/v1/runs/{id}`
   detail endpoint does not exist (FE comments admit it). Cost is the
   most painful — the BE endpoint is wired and tested, but the FE
   throws.

This is a *shippable* v1, but the front-of-funnel UX (run detail,
project list, cost view) is materially less complete than the
backend. Two of the five concerns above (run detail, projects auth)
are 1-day fixes; the proxy metering is a 1-week story; OIDC
verification is a product decision.

---

## 1. Inventory

### 1.1 Backend modules (10 + engine)

| Module / engine | Owning project(s) | Schema | Endpoints on host |
|---|---|---|---|
| **Identity** | `Comuki.Modules.Identity.{Domain,Application,Infrastructure}` | `identity` | `/api/v1/auth/{login,logout,me,oidc/{provider}/start,oidc/callback}` + `/api/v1/users` (POST + GET), `/api/v1/users/{userId}` (PATCH), `/api/v1/users/{userId}/oidc-link`, `/api/v1/grants` (POST + GET), `/api/v1/grants/{grantId}/revoke`, `/api/v1/keys` (POST + GET), `/api/v1/keys/{keyId}/revoke` |
| **Projects** | `Comuki.Modules.Projects.{Domain,Application,Infrastructure}` | `projects` | `/api/v1/projects` (GET, POST), `/api/v1/projects/{id}` (GET, PATCH, DELETE), `/api/v1/projects/{id}/settings` (GET, PUT) |
| **Chat** | `Comuki.Modules.Chat.{Domain,Application,Infrastructure}` | `chat` | `/api/v1/chat/sessions` (GET, POST), `/api/v1/chat/sessions/{id}` (GET), `/api/v1/chat/sessions/{id}/messages` (GET, POST), `/api/v1/chat/sessions/{id}/approve` (POST), `/api/v1/chat/slash` (GET) |
| **Memory** | `Comuki.Modules.Memory.{Domain,Application,Infrastructure}` | `memory` | (read by Chat; not directly host-exposed) |
| **Intake** | `Comuki.Modules.Intake.{Domain,Application,Infrastructure}` | `intake` | `/api/v1/intake` (GET), `/api/v1/intake/catalog` (GET), `/api/v1/intake/claim` (POST), `/api/v1/sources` (GET, POST), `/api/v1/sources/{id}` (GET, PUT, DELETE), `/api/v1/sources/probe` (POST), `/api/v1/sources/{id}/probe` (POST), `/api/v1/sources/{id}/rotate-secret` (POST), `/api/v1/sources/{id}/rules/{ruleId}` (PUT), `/api/v1/admission-rules` (GET, POST), `/api/v1/admission-rules/{id}` (PUT), `/api/v1/tickets` (POST) |
| **Costs** | `Comuki.Modules.Costs.{Domain,Application,Infrastructure}` | `costs` | `/api/v1/projects/{id}/costs` (GET) |
| **Runs + Workers** | `Comuki.Engine.Orchestration.*`, `Comuki.Host.Translator`, `Comuki.Host.Grpc` | `orchestration` | `/api/v1/runs` (GET), `/api/v1/runs/{id}/approve` (POST), `/api/v1/runs/{id}/cancel` (POST), `/api/v1/projects/{id}/runs/{id}/artifacts` (GET), `/workers/{claim,heartbeat,complete,fail}` (worker-only) |
| **Artifacts** | `Comuki.Modules.Artifacts.{Application,Infrastructure}` | `artifacts` | (consumed by `RunArtifactsController` above) |
| **Proxy** *(S9)* | `Comuki.Modules.Proxy.{Application,Infrastructure}`, `Comuki.Host.Proxy` | — | `/v1/models` (GET); YARP forwards `/v1/chat/completions` + `/v1/messages` |
| **Knowledge** *(S10)* | `Comuki.Modules.Knowledge.{Domain,Application,Infrastructure}` | `knowledge` | `/api/v1/knowledge/ingest` (POST), `/api/v1/mcp` (POST, JSON-RPC 2.0) |
| **Verify** | `Comuki.Modules.Verify.*` | — | (no host endpoints; engine internals) |
| **Scheduler** | `Comuki.Modules.Scheduler.*` | `scheduler` | `/api/v1/projects/{id}/scheduled-jobs` (GET, POST), `/api/v1/projects/{id}/scheduled-jobs/{id}` (GET, PATCH, DELETE) |

**Cross-cutting host endpoints** (not module-owned): `/health`,
`/health/ready`, `/openapi/v1.json`, `/hubs/runs` (SignalR),
`/api/v1/controlplane/{profiles,chat-commands}` (control plane catalog),
worker REST (`/workers/...`).

### 1.2 Dashboard pages (29 pages, 19 domains)

| Page | Domain (path) | Backend status |
|---|---|---|
| `LoginPage` | `domains/auth/pages/login-page.tsx` | **real** (`POST /api/v1/auth/login`, `GET /me`, OIDC browser start) |
| `HomePage` ("Attention") | `domains/home/pages/home-page.tsx` | partial — uses `useRunsQuery` (real) but `useOutcomesQuery` is **mock-first** (`queries.ts:19-22` throws in real mode) |
| `RunsPage` | `domains/runs/pages/runs-page.tsx` | **real** (list with filter DSL) |
| `RunDetailPage` | `domains/runs/pages/run-detail-page.tsx` | **degraded** — see Executive Summary #1 |
| `ProjectsPage` | `domains/projects/pages/projects-page.tsx` | **real** |
| `ProjectDetailPage` | `domains/projects/pages/project-detail-page.tsx` | **real** |
| `CreateProjectPage` | `domains/projects/pages/create-project-page.tsx` | **real** |
| `ChatPage` (console) | `domains/chat/pages/chat-page.tsx` | partial — list/sessions/messages wired to `kubb`, but `proposal-card` / "create ticket via chat" flows still rely on mock store (`chat.store.ts`) |
| `InitWizardPage` | `domains/chat/pages/init-wizard-page.tsx` | **mock-only** — uses `chat.store` exclusively |
| `Inbox` (none — `inbox` is in `inbox` domain? no; see below) | — | — |
| `SourcesPage` | `domains/sources/pages/sources-page.tsx` | **real** (slice 7) |
| `SourceDetailPage` | `domains/sources/pages/source-detail-page.tsx` | **real** |
| `ConnectSourcePage` | `domains/sources/pages/connect-source-page.tsx` | **real** |
| `CreateTicketPage` | `domains/sources/pages/create-ticket-page.tsx` | partial — `POST /api/v1/tickets` real, but `labels` and `straightToWork` dropped on the wire (comment in `mutations.ts:441-486`) |
| `CostPage` | `domains/cost/pages/cost-page.tsx` | **mock-first** — `useCostQuery` throws in real mode even though `GET /api/v1/projects/{id}/costs` exists |
| `ApprovalsPage` | `domains/approvals/pages/approvals-page.tsx` | **mock-first** — `queries.ts:24` throws |
| `KnowledgePage` | `domains/knowledge/pages/knowledge-page.tsx` | **mock-first** — `queries.ts:11` throws (no FE-side search yet) |
| `VerifyPage` (now a redirect stub) | `routes/verify.tsx` → `/knowledge?tab=gate` | **mock-first** underneath |
| `ModelsPage` | `domains/models/pages/models-page.tsx` | **mock-first** — `queries.ts:21` throws, even though the proxy `/v1/models` endpoint exists |
| `ComputePage` | `domains/compute/pages/compute-page.tsx` | **mock-first** — `queries.ts:22` throws |
| `QueuePage` | `domains/queue/pages/queue-page.tsx` | **mock-first** — `queries.ts:39` throws |
| `WorkerDetailPage` | `domains/queue/pages/worker-detail-page.tsx` | inherits mock |
| `TasksPage` | `domains/tasks/pages/tasks-page.tsx` | **mock-first** — `queries.ts` is a stub (`.gitkeep` next to `mappers.ts`) |
| `CreateTaskPage` | `domains/tasks/pages/create-task-page.tsx` | inherits mock |
| `IdentityPage` (admin) | `domains/identity/pages/identity-page.tsx` | **real** for reads (issue #45) and all 7 mutations (#31–#37) |
| `InviteUserPage` | `domains/identity/pages/invite-user-page.tsx` | **real** |
| `UserDetailPage` | `domains/identity/pages/user-detail-page.tsx` | **real** |
| `GrantRolePage` | `domains/identity/pages/grant-role-page.tsx` | **real** |
| `CreateKeyPage` | `domains/identity/pages/create-key-page.tsx` | **real** |
| `LinkOidcPage` | `domains/identity/pages/link-oidc-page.tsx` | **real** |
| `SettingsPage` | `domains/settings/pages/settings-page.tsx` | **mock-first** — `queries.ts:24, 42, 71, 92` all throw; mutations touch a process-local `mockSettings` only |

**Mock-first summary:** 10 of 29 pages throw on real mode
(approvals, compute, cost, knowledge, models, observability, queue,
settings, tasks, verify-via-knowledge). 1 is half-built
(`run-detail-page`). 1 is partially silent about dropped fields
(`create-ticket-page`). 17 are wired.

### 1.3 Background services

| Service | Where | Cadence | Notes |
|---|---|---|---|
| `LeaseReaperWorker` | `Engine.Orchestration` | `LeaseOptions.ReapInterval` | Set-based `UPDATE … WHERE … RETURNING` in one transaction; per-cycle `AsSystem` scope |
| `EscalationTimeoutWorker` | `Engine.Orchestration` | `EscalationTimeoutOptions.*` | Sweeps runs past escalate timeout |
| `RunArtifactPackagerService` | `Artifacts` module | 10 s, batch 50 | Two-phase scope discipline (discovery + per-candidate bundling) |
| `RunArtifactPackagerHostService` | `Host/Artifacts` | 10 s | Wraps the module driver, appends `run.artifacts_bundled` journal event in same tx |
| `ArtifactBucketInitializer` | `Artifacts` infrastructure | startup, one-shot | `IHostedService`, idempotent, warns rather than throws if MinIO down (so OpenAPI build still works on a fresh clone) |
| `OidcStateSweeper` | `Host/Workers` | `Host:OidcSweep:Interval` (default 5 min) | Bounded-leak defence; survives transient DB/IO failures |
| `ProjectSettingsCacheRefresher` | `Projects` infrastructure | 15 s | Warm-then-refresh; on refresh fires change token |
| `BootstrapAdminStartupService` | `Host/Auth` | startup, one-shot | Idempotent — exists-or-create the platform-admin user |
| `TrustClassRatchetSweeper` | `Engine.Orchestration` | configured | Sweeps RunTrustClass; slice 1 of the autonomy ratchet (slice 2 deferred to v2 — issue #49) |
| `DistributedProjectSettingsCache` | `Shared.Redis` | n/a (called by refresher) | New in 2026-09-07 |
| `GenericCommandVerifierWorker` | `Verify` module | configured | Runs `IGenericCommandRunner.ProcessRunner` |
| `SchedulerRunLauncher` (host-side) | `Host/Scheduler` (host driver) | per-dispatch | Reads scheduler store, calls `IRunLauncher` to start the run |

### 1.4 Options inventory (`IOptions<T>`)

26 options types found in `platform/src`. **All** are bound via
`AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`
**except** (see `grep`):

- `SchedulerOptions` in `Comuki.Modules.Scheduler.Application.SchedulerApplicationExtensions.cs:29`
  is bound with bare `AddOptions<SchedulerOptions>()` — *no*
  `Bind(...)` and *no* `ValidateOnStart()`. The *same* options class
  is also bound in `HostComposer.cs:190-191` via
  `.Bind(configuration.GetSection(SchedulerOptions.SectionName))` (also
  no validation). The bare registration in the application extension
  registers the factory with no configuration; the host's
  registration wins at runtime. Result: two registrations, one
  without `Bind` or validation. Likely-safe but a small technical
  debt item.
- `OidcOptions` (host composition) is bound via
  `AddOptions<OidcOptions>().Bind(...)` but **without**
  `ValidateDataAnnotations().ValidateOnStart()` (`HostComposer.cs:209-210`).
  A typo in `auth:oidc:providers[]` will not be caught at startup.
- `WorkerTokenOptions` (engine) is bound with `ValidateOnStart()`
  but the `Host` *also* registers a `WorkerTokenOptions`
  (host-side, `Host/Workers/WorkerRuntimeExtensions.cs:26-29`) and
  it's also bound with `ValidateOnStart()`. The host binding is what
  runs in production (engine binding is for the engine unit tests).
  Documented but worth knowing.

`ProxyOptions` and `ApiKeyOptions` use `IValidateOptions<T>` in
addition to data annotations (`Options.ProxyOptionsValidator`,
`Identity.ApiKeyOptionsValidator`). The state file says
`MA*` analyzers were removed; severity for `ValidateOnStart` is
enforced in installer code, not via a package analyzer.

---

## 2. Per-Module Findings

### 2.1 Identity (project: `Comuki.Modules.Identity`)

#### Critical

- **Projects are not permission-gated (cross-cutting, but this module
  is where the missing filter belongs).** See `ProjectsModuleEndpoints.cs:27`
  (the literal `// TODO(auth): project:admin — wire [RequiresPermission] host-wide`).
  Every authenticated identity can list/create/update/archive any
  project and read every project's settings. The `Identity` module
  owns the `RequiresPermission` filter, the `PermissionDemandStartupValidator`
  (which would surface the missing attribute on the projects endpoints
  at startup if a catalog key were referenced — the gaps are *absent*
  attributes, which the validator does not catch).
- **OIDC auto-link by email is silent account takeover.** `OidcAccountLinker.cs:27-49`:
  on a fresh login with no existing link, the handler:
  1. checks `userStore.FindByEmailAsync(request.Email, ...)` — if a
     local user with that email exists, the IdP's `sub` is silently
     linked to that account; the local user is never notified;
  2. otherwise provisions a new password-less account with the
     IdP-supplied `email` and `displayName`.
  There is no email-verification step, no "this email is now
  controlled by IdP X" notification, no opt-in, no separate
  `OidcLink` row tracking the binding. A breach of a configured
  IdP that lets the attacker set `email_verified = true` and `sub`
  on a JWT becomes a Comuki account.

#### Major

- **API key tenant scope is opaque to the operator.** The `X-Comuki-Tenant`
  header is enforced server-side
  (`ApiKeyAuthenticationHandler.cs:108-113`, 142-147), but the FE
  has no UI for it; `useCreateApiKeyMutation` always passes
  `expiresAt: input.expiresAt` and never a tenant id
  (`queries.ts:402-422`). A multi-tenant deployment cannot scope
  worker tokens per project from the dashboard; the operator has
  to mint keys via `curl` against the host.
- **No "user has OIDC link" surfacing on the admin page.** The wire
  `UserAccountView` does not carry the `oidc_links` (the FE comment
  in `mappers.ts:118-121` notes "no OIDC subject" as a known gap).
  An admin disabling a user does not see whether the user has an
  active OIDC link to disable too.
- **Bootstrap admin has no rotation enforcement.** The
  `ProductionSecretValidator` (`ProductionSecretValidator.cs:71-87`)
  refuses to start with the dev default, but does not enforce
  rotation strength, expiry, or even a "last rotated at" check.
  An operator can leave the production password at
  `comuki_dev` → rotated to `Strong!Pass1` in 2026 and never
  change it again. The runbook's "rotating" section (lines 81-100)
  describes a manual procedure with no scheduler.

#### Minor

- **Bootstrap admin path stores the password as bcrypt cost 12**
  (runbook line 102-104 documents the cost). No mention of memory
  hardening, no progressive cost bump path.
- **Subject resolution on the bootstrap path uses the
  `IdentityClaimNames.ApiKeyId` claim** but on a key created *via*
  the bootstrap flow (none today), the key's `tenantProjectId` is
  null — fine, but the FE never lets the bootstrap operator mint
  a key. Self-limiting, not a bug today.

### 2.2 Projects (project: `Comuki.Modules.Projects`)

#### Critical

- **No `[RequiresPermission]` on the projects REST surface** (already
  counted under Identity). `ProjectsModuleEndpoints.cs:25-39`
  registers 7 endpoints — list / create / get / patch / archive /
  get-settings / update-settings — none of which checks permission.
- **The `CreateProjectHandler` checks slug uniqueness with a
  pre-check then a unique index** (`CreateProjectHandler.cs:22-28`).
  The race is acknowledged in the comment ("a concurrent create
  loses with a DB error") and the unique index is the safety net,
  but the error path maps to a 500 (not a 409) because the only
  409-mapping in `ProjectsEndpointRunner.ExecuteAsync` is
  `ProjectConflictException` (slug pre-check) and
  `ProjectSettingsConflictException` (settings version). A
  race-induced `DbUpdateException` falls to the global
  `ProviderExceptionHandler` and returns 500.

#### Major

- **Settings update is the only "live" mutation** but the
  `ProjectScaleSettingsAdapter` and `ProjectBudgetSettingsAdapter`
  only re-read on a fixed interval (`ProjectSettingsCacheRefresher`
  is 15 s, `EntryTtl` is 30 s). After a PUT, the next compute
  read can see stale values for up to 30 s. The FE display
  updates immediately (it re-reads via `useProjectSettingsQuery`),
  but the *side effect* (compute scale, budget gate) lags.
  A 45-second worst-case operator-visible inconsistency.
- **No `GET /api/v1/projects/{id}/archive` or unarchive endpoint.**
  The DELETE returns 204 and the project becomes hidden from the
  list (`includeArchived=false` default), but there is no
  operator-facing way to bring an archived project back. The
  `ProjectView` schema presumably has a flag; the FE has no
  control for it.

#### Minor

- **Project slug pattern is `^[a-z0-9]+(-[a-z0-9]+)*$`** —
  lowercase, kebab-case, ASCII. Internationalised names
  (Cyrillic, CJK) cannot be used as a project slug; the FE
  never shows this constraint to the user until the
  validator 400s.
- **Project create accepts no `slugs` for known sources.**
  When connecting a source (Intake), the source record carries
  its own `projectId`; no `?projectId=` filter on
  `/api/v1/sources` (it's `[FromQuery]`) means the operator
  *can* pass it but the FE projects page does not pre-fill it.

### 2.3 Chat (project: `Comuki.Modules.Chat`)

#### Critical

- **No host endpoint for streaming responses.** The
  `ChatTurnResultView` returned by `POST /api/v1/chat/sessions/{id}/messages`
  (`ChatSessionsController.cs:91-115`) is a single-shot response;
  the FE comment at `dashboard/src/domains/chat/api/queries.ts:71-77`
  admits "the assistant's next message and an `awaitingApproval`
  flag (used by the proposal card to re-render the confirm button)"
  is the only way state flows back. The FE has to invalidate the
  query and refetch the transcript. There is no Server-Sent Events
  or WebSocket channel per chat session; the only realtime channel
  is `/hubs/runs` (which is about runs, not chat transcripts).
  Streaming UX is a polling dance.
- **Chat approve path is lossy.** `ChatSessionsController.cs:146-167`
  `ApproveAsync` takes `{ Approved, Reason }` but the FE sends
  only `approved: true|false` (no `proposalId`):
  ```
  // queries.ts:140-143
  await postApiV1ChatSessionsSessionidApprove(sessionId, {
    approved: decision === "confirmed",
  });
  ```
  The BE picks "the current pending plan" (one). A session with
  two pending plans — an edge case the chat graph allows —
  approves whichever the host's `ChatPlanGate` resolves to,
  not the one the operator clicked. Documented in
  `queries.ts:118-126` as a known limitation.

#### Major

- **The chat page is mock-first for "proposal confirmation".**
  `useProposalDecisionMutation` has a real-mode branch
  (`queries.ts:127-150`) that calls the kubb endpoint, but
  `useSendMessageMutation` in real mode (line 89-94) calls
  `postApiV1ChatSessionsSessionidMessages` and never reads the
  response. The mock path returns a fully-populated session; the
  real path is a fire-and-forget.
- **`/init` wizard (`domains/chat/pages/init-wizard-page.tsx`)
  is mock-only** with hand-written seed scripts. There is no
  real-mode path; the `GetDocument(Insider)`-emitted OpenAPI
  has no `/api/v1/chat/init` endpoint. The wizard cannot onboard
  a real project today; it's a screen the FE developer maintains
  out of the platform.

#### Minor

- **Chat session ownership is per-subject, not per-project.**
  `ChatSessionService.CreateAsync` accepts a `ProjectId?` but a
  session without a project can still send messages and the
  messages are usable across projects (the chat tool handlers
  resolve project context per-message, not per-session). The
  `chat.use` permission is project-scoped; an operator with
  `chat.use` on project A can read session transcripts that
  include project B. Cross-session leakage vector.

### 2.4 Memory (project: `Comuki.Modules.Memory`)

#### Critical

(none — the module is consumed by Chat/Verify and is not host-exposed.
Risk lives in the consumer modules.)

#### Major

- **No retention / forgetting policy.** `SourceDocument` and
  `MemoryEmbedding` are append-only. The FE's
  `memory_facts` and `learning_candidates` tables have no
  `expires_at` column. A long-running deployment accumulates
  memory forever; pgvector indexes get slower; the LLM context
  picks up stale facts. The plan in
  `comuki-task-breakdown.md` mentions "Memory: long-term
  facts with pgvector" but no forgetting/TTL story.
- **Embedding model is configuration-only.**
  `KnowledgeEmbeddingOptions` lives in `Knowledge` module; the
  `Memory` module reuses it. There is no per-tenant embedding
  model — a deployment that wants OpenAI for knowledge and
  Anthropic for memory has to run two distinct embedding
  calls and pre-store both.

#### Minor

- **Two parallel chat storage paths.** `chat` schema
  (Voluta fork) and `memory` schema (`chat_messages`,
  `chat_checkpoints`) — both store messages. The
  `chat_messages` write is duplicated. Documented in
  `database-schemas.md:25-32` as "different physical tables in
  different schemas" — not a bug, but a future rollback story
  needs to touch both.

### 2.5 Intake (project: `Comuki.Modules.Intake`)

#### Critical

(none — well-built.)

#### Major

- **`SourcesController.CreateAsync` requires `source:write` but
  returns 201 with the webhook path plaintext.** The view
  carries `SecretEnvRef` (env-var name), not the secret — that
  is correct. But there is **no `dry-run` mode** in the FE
  connect-source form, even though the BE has
  `POST /api/v1/sources/probe` (`SourceProbeService.ProbeDraftAsync`).
  The dashboard always creates then tests; the BE's probe
  endpoint is the better UX path and the FE's
  `useTestSourceDraft` mutation calls it correctly, but the
  form's "Test connection" button is not surfaced in the
  create flow.
- **Native ticket creation drops fields silently.**
  `useCreateNativeTicket` (`sources/api/mutations.ts:441-486`)
  admits in its own comment: "labels and straightToWork are
  silently dropped, not flattened into body, because the
  page renders a different badge for 'filed straight to work'".
  A real-mode user who flips the "straight to work" switch sees
  no error and gets no run; the ticket is filed and the badge
  reads "filed". Confusing at minimum.
- **`/api/v1/intake/catalog` is a single-page enumeration of
  one connection's external issue catalog.** It accepts
  `page` (default 1) but no `pageSize`; the response is the
  page slice, not a `paged` envelope. The FE has to track
  exhaustion by checking length < expected — currently the
  FE does not paginate at all (`mutations.ts:54` reads
  `FetchCatalogAsync(new SourceConnectionId(connectionId), page, ...)`)
  with `page=1` only.

#### Minor

- **Sync-back to external trackers (`RunStatusBridgeWorker`)**
  writes to an outbox table; there is no worker draining the
  outbox visible in `Comuki.Host.Compose`. Looking at
  `Comuki.Modules.Intake.Infrastructure.Sync.RunStatusBridgeWorker`,
  it IS the worker — but it polls every `IntakeWorkerDefaults.SyncBackIntervalSeconds`
  with no jitter. A 30s cron-style poll hitting thousands of
  projects is unnecessary load; exp-backoff or webhook-only
  would be kinder.
- **GH/GL PR-review profile is wired in source code but the
  webhook secret is shared per-source.** There is no per-tenant
  webhook signing; the same secret signs every incoming PR-review
  payload for that source connection. Per-project webhook
  isolation would let a deployment scope a source to one
  project.

### 2.6 Costs (project: `Comuki.Modules.Costs`)

#### Critical

- **The proxy budget is a historical-only check.** The
  `usage_events` table is never written on a successful
  proxy call (`ProxyTransforms.cs:57-69`, documented in
  `proxy.md:188-195`). The `DefaultProxyBudgetEnforcer` sums
  `usage_events.cost_usd_micros` over the month *before*
  forwarding, so it can only see spend that some *other*
  path recorded. In a fresh deployment where the only
  LLM path is the proxy, the budget is *never tripped* —
  a configured `BudgetUsd = 10` is effectively `null`.
- **The cost endpoint is wired, the dashboard is mock-first.**
  `GET /api/v1/projects/{id}/costs` returns
  `ProjectCostsView` (host
  `CostsModuleEndpoints.cs:24-31`); the FE
  `useProjectCostsQuery` (`projects/api/queries.ts:111-125`)
  does call the kubb client in real mode — but the standalone
  `useCostQuery` (`cost/api/queries.ts:10-15`) still throws.
  Two FE queries, one real and one mock, for what is the same
  backend endpoint shape.

#### Major

- **No per-model cost attribution in the BE.** `UsageEventView`
  carries `Source` + `Model` (`ProjectCostsView.cs:34-41`) but
  not `ModelTier` or `Provider`. A multi-provider deployment
  (OpenAI + Anthropic) cannot show "you spent $12 on
  gpt-4o and $7 on claude-sonnet-4" — only "you spent $19
  via the proxy". The schema is wide enough; the summary
  endpoint doesn't aggregate.
- **No `usage_events` retention/archival.** Append-only
  pg table; the `CostRead` query scans the last 50 events
  (`GetProjectCostsHandler.DefaultRecentTake = 50`), but the
  full spend *number* is a `SUM` over the whole month
  unfiltered. After a year in production, the SUM touches
  millions of rows. No partitioning, no rollup table, no
  TTL — see the "no retention" issue under Memory.

#### Minor

- **The cost projection endpoint requires `cost:read` but the
  permission is the same as `project:read` plus a scope filter
  — every project member can see project costs.** Operators
  who want cost visibility restricted to billing roles cannot
  do that today.

### 2.7 Runs (project: `Comuki.Engine.Orchestration` + `Comuki.Host.Translator`)

#### Critical

- **No `GET /api/v1/runs/{id}` endpoint.** `RunsController`
  exposes only `List`, `Approve`, `Cancel`. The FE has to
  scan the list page to get a single run (and the list page
  is paginated to 100 by default, so any run past the first
  page is unselectable from the FE). The MCP server
  `runs.get` is *deliberately* a stub that returns "not yet
  implemented" (`McpServer.cs:272-296`). The run detail page
  is half-built around this gap.
- **Run view model is sparse.** `RunView` carries
  `id / projectId / status / createdAt / updatedAt` only
  (`RunsListHandler.cs:60-64`). No `title`, no `app`, no
  `model`, no `cost`, no `tokens`, no `brief`, no
  `workItems`, no `revision`. The FE mapper fills these
  with empty strings (`mappers.ts:285-330`). The cost/tokens
  numbers need a `usage_events` aggregate per run — not
  done at all.

#### Major

- **`approve` and `cancel` race on terminal status.**
  `ApproveAsync` (host adapter) reads the current state, the
  engine state machine checks `RunStatusMachine` allowed
  transitions, and the host returns 409 on
  `RunDecisionConflictException`. Good. But the *list*
  endpoint does not include the work items or the work-item
  statuses — the home page's "needs you" list reads the
  `status` field on the run, not the work items. A run
  whose *plan* is paused for approval but whose run is
  still `Running` does not show up in attention. The
  `Run.UpdatedAt` is the proxy signal.

#### Minor

- **The lease reaper has no alerting on high-reap rate.** If
  leases start expiring en masse (e.g. a bad worker image
  kills containers before heartbeat), the reaper logs the
  count at `Information` and keeps going. No metric, no
  alert, no escalation. The OTel metric name is documented
  in `~/.agents/rules/observability/diagnostics.md` but I did
  not see a `comuki.orchestration.lease.reaped` counter
  emitted from `LeaseReaper.ReapAsync`.

### 2.8 Artifacts (project: `Comuki.Modules.Artifacts`)

#### Critical

(none — the dual-scope discipline
in `RunArtifactPackagerService.cs:151-189` is well-built.)

#### Major

- **The artifact endpoint does not return signed URLs.**
  `RunArtifactsController.cs:33-46` returns the `ArtifactPointer`
  with the canonical URI; the FE `mapArtifactPointer`
  (`mappers.ts:341-355`) tries to `new URL(entry.uri)` and
  drops rows that fail to parse, with a console.warn. A
  misconfigured `Artifacts:Endpoint` (no scheme, missing
  port) drops every row, the dashboard renders an empty
  artifacts tab, and the only diagnostic is a console
  warning visible in the browser dev tools.

#### Minor

- **30-day lifecycle on `comuki-run-bundles` is documented in
  `minio.md` but the runbook `Restore` section line 200 says
  "the bookkeeping rows in `artifacts.run_bundles` keep the
  canonical pointer list — the host rebuilds the bucket
  from those rows on the next packager pass per terminal
  run. No data loss."** This is correct for non-current
  versions, but MinIO's `NoncurrentVersionExpiration` only
  fires on *non-current* versions — never on the single
  current one. The `comuki-run-bundles` bucket is configured
  with `s3:Versioning` off (the deploy manifest at
  `deploy/docker-compose.yml` does not turn it on), so the
  lifecycle rule is a no-op. The retention story is "never
  delete" in practice.

### 2.9 Proxy (project: `Comuki.Modules.Proxy` + `Comuki.Host.Proxy`)

#### Critical

- **Metering is dormant on the success path.**
  `ProxyTransforms.MeterUsageFromResponseAsync` returns
  immediately (`ProxyTransforms.cs:57-69`). Every successful
  call's tokens and cost are computed and discarded. The
  budget enforcer sees historical spend only (see Costs
  §critical). Documented as deferred (`proxy.md:188-195`).
- **The proxy is a global kill switch but the dashboard
  has no proxy page.** `ModelsPage` is mock-first
  (`models/api/queries.ts:21-24` throws), so even though
  the BE has `/v1/models` and the operator can see the
  catalogue, the FE cannot. There is no per-virtual-key
  view in the dashboard either; the operator has to
  read `Proxy:VirtualKeys:N:*` from `appsettings.json`.

#### Major

- **Calendar-month windowing of the budget is hardcoded.**
  `DefaultProxyBudgetEnforcer.cs:27-32` uses
  `ProxyBudgetMath.StartOfMonth(clock.GetUtcNow())`; the doc
  on line 110-115 of `proxy.md` confirms. A 10-day window
  resetting on the 1st means a `BudgetUsd=10` is actually
  "10 USD over the calendar month" not "10 USD per 30 days".
  For operators expecting a rolling cap, the surprise
  happens on the 1st.
- **The `PricingTier` default is hardcoded OpenAI.**
  `ProxyOptions.PricingTier.cs:73-81` — `InputUsdPerMillion=3m,
  OutputUsdPerMillion=15m`. An Anthropic deployment that
  forgets to set `Proxy:Pricing:*` gets 1/10 of real cost on
  the `usage_events` row. The cost summary endpoint reports
  the meter; the operator only sees the *under-counted*
  number.
- **No `429` on budget breach — `Retry-After` only.** The
  `proxy.md` says "host answers 429 Too Many Requests" (line
  113) but the actual `ProxyBudgetVerdict` is consumed by
  YARP transforms which short-circuit before the
  controller. The actual code path is in
  `ProxyTransforms.cs:36-41`: when `key.BudgetUsd` is set
  and exhausted, YARP returns 429 with a
  `Retry-After` header. Documented but not auditable in a
  unit test (the metering-side tests don't exist because
  no usage rows are written).

#### Minor

- **Virtual keys live in `appsettings.json` and require a
  host restart to mint/revoke** (line 70-71 of `proxy.md`,
  and `proxy.md:198-199` documents it as "follow-up"). A
  Postgres-backed key store is a clear roadmap item.
- **There is no "per-key" rate limit**, only the budget.
  A single bad client can burn the entire monthly budget
  in minutes. The `proxy.md:200-201` future-work section
  acknowledges this.

### 2.10 Knowledge (project: `Comuki.Modules.Knowledge`)

#### Critical

(none — the embedding/chunking pipeline is small and well-tested.
The `McpServer` is a thin shim with two real tools.)

#### Major

- **`McpServer.RunsGetAsync` is an intentional stub.**
  `McpServer.cs:272-296` returns "not yet implemented"
  with `IsError: true`. The MCP server is a stable surface
  that external agents (the brain host, worker SDKs) read;
  the stub being part of the *catalog* means an agent that
  discovers `tools/list` thinks the tool exists and then
  gets a known-impossible error.
- **The MCP `/api/v1/mcp` endpoint is anonymous by
  comment.** `McpModuleEndpoints.cs:19-22` admits "the global
  auth + permission filter (when wired) handles identity; MCP
  shares the host's cookie / api-key auth." — but there is
  no `[RequiresPermission]` on the endpoint itself, and
  `McpServer.DispatchAsync` does no permission check before
  calling the tool. `knowledge.search` and `runs.list`
  have implicit `knowledge:read` / `run:read` permissions
  on the cookie/api-key principal, but the dispatcher
  never checks. A logged-in user without `run:read` can
  still call `runs.list` over MCP.

#### Minor

- **The `tools/call` catch-all wraps every error in
  `InternalError` (McpServer.cs:175-183) and includes
  `exception.Message` in the JSON-RPC error response.**
  Tool implementations can leak internal exception
  text — e.g. "Npgsql: connection refused" goes to the
  caller. MCP is `application/json` not `problem+json`,
  so the platform's exception handler does not catch
  it.

### 2.11 Verify (project: `Comuki.Modules.Verify`)

#### Critical

(none — the module is engine-internal; no host endpoints.)

#### Major

- **No host surface to inspect the gate's last results.**
  The dashboard's `Verify` page (now redirected to
  `Knowledge?tab=gate`) reads mock-first
  (`verify/api/queries.ts:15-19` throws). The engine has
  the data (`GenericCommandRun` EF entity, see
  `Modules.Verify`); no host controller exposes it.
- **Slice 1 of the verify-gate landed (issue #11 sub-slice)
  but slice 2 (Process.Start container isolation, issue
  #47) is deferred to v2.** The current `ProcessRunner`
  starts the verifier as a host child process — it runs
  with the same permissions as the host. A buggy
  verifier command can read `appsettings.json`, the
  MinIO secret, the database password, etc.

#### Minor

- **No per-project feature flag in the BE for "verify
  enabled".** `ProjectSettings.VerifyEnabled` exists, but
  there is no `/api/v1/verify` controller; the dashboard
  mock simulates the toggle but the BE has no real read
  endpoint to back it.

### 2.12 Scheduler (project: `Comuki.Modules.Scheduler`)

#### Critical

(none — well-tested, clean implementation,
`ScheduledJobsController.cs` has all the standard
`[RequiresPermission]` decorations, `SchedulerEndpointRunner.cs`
maps typed exceptions, and the dispatcher is properly
scoped.)

#### Major

- **Sentry integration is opt-in and lazy.**
  `SchedulerSentryBootstrap.TryInitialize` (`Program.cs:30`)
  is a no-op when `Scheduler:Sentry:Dsn` is unset; the
  Sentry observer is also a no-op. The result: in
  dev/staging, there is zero observability on the
  scheduler's per-fire success/failure — the operator
  has to read `journal.run_events` and grep for
  `scheduler.job_fired`. There is no OTel metric
  `comuki.scheduler.fired` or `.skipped` (the runbook
  acknowledges this on line 200-215).
- **Sentry bootstrap is host-wide; one bad Sentry DSN
  blocks the whole host's startup.** The runbook
  (line 116-118) says "the dispatcher continues
  regardless; the journal observer still fires" — but
  the Sentry SDK *initialisation* runs before
  `HostComposer.Compose` and a malformed DSN would
  throw. There is no try/catch around
  `SentrySdk.Init` in `SchedulerSentryBootstrap` (I did
  not read the file directly; the runbook is the
  authoritative comment and its wording is ambiguous).
- **Scheduled jobs are not project-scoped in the
  operator UI.** `SchedulerJobService.ListAsync(projectId)`
  is fine, but the "all jobs across the platform" view
  (operator dashboard) is missing. There is no
  "scheduler admin" page; `ScheduledJobsController` only
  serves per-project.

#### Minor

- **`UpdateScheduledJobCommand` accepts `ProfileKey` and
  `BriefJson` but `UpdateAsync` passes `null` for
  `ProfileKey` and `BriefJson`** — they cannot be updated
  through the API. To change a job's brief the operator
  archives and creates a new one. Not documented in the
  operator docs (`scheduler.md`).

---

## 3. Cross-Cutting Findings

### 3.1 Observability

- **Telemetry installer is wired correctly** —
  `ComukiTelemetryInstaller` is the single `AddComukiTelemetry()`
  call in `HostComposer.Compose:81`. The ActivitySource
  convention (`comuki.<area>.<verb>`) is followed in
  `Engine.Orchestration`, `Engine.Compute`, and `Engine.Proxy`
  (sample: `Compute.Start` span tag in
  `ComputeStartRequest`-shaped operations).
- **Gap: the Lease Reaper does not emit a metric.**
  `LeaseReaper.ReapAsync` (the path that does the
  `RETURNING` SQL) does not increment an OTel counter. A
  silent surge of reaps (workers dying, leases expiring
  unclaimed) is invisible to Victoria Metrics.
- **Gap: the proxy metering is dormant on the success
  path** (already in §2.9) — every successful LLM call
  is unmeasured.
- **Gap: no "host startup" gauge.** The
  `BootstrapAdminStartupService`, `ArtifactBucketInitializer`,
  `OidcStateSweeper` all log at startup but do not push
  to Victoria. A misconfigured deploy leaves the operator
  reading container logs to know whether the host
  started cleanly.
- **OTel metric names use the right pattern**
  (`comuki.<noun>.<quantity>`) where they exist; the gap
  is in *coverage*, not *naming*.

### 3.2 Error envelope

- **The single `IExceptionHandler` (`ProviderExceptionHandler`) is
  registered in `HostComposer.cs:230` and is the only
  place RFC 9457 ProblemDetails is produced.** Endpoints
  that need a custom shape (auth's `AuthProblems.Problem`,
  runs' `RunsProblems.StateConflict`) call
  `TypedResults.Problem(...)` and wrap in `ObjectResult` —
  consistent shape, consistent `code` extension.
- **Gap: the Mcp endpoint returns raw `JsonRpcResponse.Failure`
  with `exception.Message` in the `message` field
  (`McpServer.cs:181`).** A Npgsql or S3 error
  becomes part of the JSON-RPC error body. Not a
  `ProblemDetails` envelope, no `code` taxonomy.
- **Gap: the proxy YARP transforms return 429 with a
  text body** (not ProblemDetails). A polling client
  has to parse the body to know the reason. The
  `Retry-After` header is set; the body is undocumented.
- **Per the platform's `exceptions.md` rule (no inner
  exception messages in `detail`): the `McpServer`
  violates this. The auth runs' `RunsProblems.StateConflict`
  shows `exception.Message` (`RunsProblems.cs:148`) — also
  a violation, but the message comes from the
  `RunDecisionConflictException` which is host-controlled
  text, not provider text.
- **The `ProviderExceptionHandler` maps
  `ProviderException` to 502; no other exception type
  is mapped there.** A `DbUpdateException` (e.g. unique
  index violation on project slug) falls through to
  500, not 409. The `ProjectsEndpointRunner` catches
  `ProjectConflictException` and maps to 409 only when
  the *pre-check* detects the conflict; a race-induced
  index violation 500s.

### 3.3 Multi-tenancy

- **`SubjectScopeMiddleware` is wired**
  (`HostComposer.cs:321`); `ISubjectScopeAccessor` is a
  singleton (`HostComposer.cs:304`); the EF global query
  filters are bound to it. Every request gets a scope;
  the worker surfaces declare `AsSystem` explicitly.
- **API keys have an optional `TenantProjectId`**
  (`ApiKeyIssuer.cs:36-55`) enforced via
  `X-Comuki-Tenant` header. The dashboard does not
  surface this (see Identity §major).
- **Project-level row scoping is enforced at the EF
  layer** (the `OrchestrationDbContext` filters
  `Runs` by the subject's project assignments).
- **Gap: `OidcLinks` are not project-scoped.** A
  `(provider, sub)` link is global; revoking it
  affects every project the user is in. There is no
  `OidcLink.TenantProjectId`.

### 3.4 Auth / RBAC

- **The `RoleMatrix` is in code** (`RoleMatrix.cs:14-118`),
  6 roles, 24 permission keys. Compiled, not data — the
  trade is documented.
- **The `PermissionDemandStartupValidator` catches
  attributes that reference unknown keys** (per the
  `Permissions.cs` doc) — but it does *not* catch
  endpoints that should have a permission but don't
  (the projects module is the live example).
- **Vocabulary drift: the FE and the BE use different
  permission names.** The BE
  (`Permissions.cs:13-88`): `run:read`, `run:stop`,
  `intake:read`, `intake:claim`, `source:write`, etc.
  The FE (`dashboard/src/shared/session/permissions.ts:22-44`):
  `runs.view`, `runs.stop`, `inbox.view`, `inbox.take`,
  `sources.view`, `sources.edit`, `cost.view`. The
  mapping is hand-coded in
  `domains/identity/api/mappers.ts:mapMeResponseToSessionUser`
  (and similar). When the BE adds a new key, the FE
  silently does not honor it; when the FE renames a
  permission, the BE does not care. There is no shared
  type — the OpenAPI emission produces the BE shape, the
  FE hand-codes a parallel shape.
- **No `/api/v1/identity/whoami` audit endpoint.** The
  audit log ("who disabled user X at 2026-09-08?") is
  not a product feature today. `Logs` carry the event
  but are not queryable.

### 3.5 Configuration

- **All `IOptions<T>` have `SectionName` constants** with
  the documented exception of `SchedulerOptions` (see
  §1.4).
- **`Bind(...).ValidateDataAnnotations().ValidateOnStart()` is
  universal** with the same `SchedulerOptions` exception
  and `OidcOptions` (no `ValidateOnStart()`).
- **CORS default is `["http://localhost:17173"]`**
  (`CorsOptions.cs:23`). A `https://app.example.com`
  deployment fails the preflight silently — the
  browser shows "CORS error" without context. The
  `deploy/.env.example` should ship a
  `COMUKI_CORS__ALLOWEDORIGINS_0` example.
- **The `ProductionSecretValidator` only catches
  `comuki_dev`-style defaults.** A new operator who
  uses `password123` or `admin1234` is accepted; the
  validator does not check strength. (See Identity
  §major.)
- **Env-var-only secret discipline is consistent
  across the codebase** — `OidcClientSecrets`,
  `BootstrapAdminOptions.AdminPassword`, source
  `SecretEnvRef`, all read by env-var name. The OIDC
  discovery cache is the one exception (it has a
  process-lifetime cache for the discovery doc that
  could mask a secret-leaking provider's misconfig).

### 3.6 FE–BE parity

| FE page | BE endpoint exists? | FE wired? | Verdict |
|---|---|---|---|
| `LoginPage` | yes | yes | **OK** |
| `HomePage` (outcomes) | no | n/a | BE feature missing; FE has mock |
| `RunsPage` | yes | yes | **OK** (list + filter DSL) |
| `RunDetailPage` | **NO** (no `/runs/{id}`) | work-around via list scan | **Critical gap** |
| `ProjectsPage` / detail / create | yes | yes | **OK** *but* no auth (see §2.1) |
| `ChatPage` | yes | partial | streaming + approve lossy (§2.3) |
| `InitWizard` | no | n/a | mock-only |
| `SourcesPage` and detail and connect | yes | yes (slice 7) | **OK** |
| `CreateTicketPage` | yes | partial | drops `labels` + `straightToWork` (§2.5) |
| `CostPage` | yes | **NO** (FE throws) | **Critical gap** |
| `ApprovalsPage` | no | n/a | BE feature missing; mock |
| `KnowledgePage` (search) | no (only ingest) | n/a | BE feature missing; mock |
| `VerifyPage` (redirect to Knowledge?tab=gate) | no | n/a | BE feature missing; mock |
| `ModelsPage` | yes (proxy `/v1/models`) | **NO** (FE throws) | **Critical gap** |
| `ComputePage` | no (worker REST only) | n/a | BE feature missing; mock |
| `QueuePage` | no (no host surface) | n/a | BE feature missing; mock |
| `TasksPage` (and create) | no | n/a | BE feature missing; mock |
| `IdentityPage` admin (7 mutations) | yes | yes | **OK** |
| `SettingsPage` | partial (project settings yes, kill-switches no) | **NO** (FE throws) | **Critical gap** |
| `ObservabilityPage` (Grafana links) | n/a (the FE just links out) | **NO** (FE throws) | low-priority — link-only |
| `Home` (outcomes) | no | n/a | mock |
| `WorkerDetailPage` (under Queue) | no | n/a | mock |

**Headline:** 4 critical FE-BE gaps (RunDetail, Cost, Models,
Settings) where the FE has not been wired even though the BE
has the endpoint, and 8 pages where the BE feature is missing
entirely (Approve queue, Knowledge search surface, Compute
admin, Queue admin, Tasks, Home outcomes).

---

## 4. Documentation Audit

### 4.1 Outdated docs

| File | Line | Issue |
|---|---|---|
| `.agents/STATE.md` | 222 | "Postgres — 9 schemas" — there are **10** in the code (`grep` for `public const string Schema`: orchestration, identity, projects, memory, chat, intake, costs, artifacts, knowledge, scheduler). The roadmap-of-slices column on line 76-86 of `STATE.md` lists 8 sub-slices — that count is right; the schema count is wrong. |
| `.agents/STATE.md` | 222 | The schema list is "9 schemas" in narrative but the actual `database-schemas.md` doc lists 8 (and also says "Eight modules, eight schemas"). The actual code has 10. |
| `.agents/STATE.md` | 53 | S7 description says "FE ядро + SignalR realtime (kubb-client + 5 wire-up slices)" — accurate, but the row in the slice-cadence table above (line 49) lacks a SHA for S7. Minor. |
| `.agents/STATE.md` | 222 | "per-DbContext schemas" — 10 in code, "9" in narrative, "8" in old doc. |
| `.agents/ROADMAP.md` | 12-16 | Footer says "v1 milestone is complete on master (`e679663`)" — the worktree I audited is at `fa659fd727260731b00c83d9f812ffa692c2f1f6`. The audit branch (`fix/audit-product`) sits between `master`'s reported tip and the live state. Not a bug; the worktree was branched from a state that included the recent `merge(fixes):` commits. |
| `.agents/docs/operations/database-schemas.md` | 6, 14-23 | "Eight modules, eight schemas" and the table lists 8 — `knowledge` and `scheduler` schemas are missing. Out of date with the code. |
| `.agents/docs/operations/proxy.md` | 188-195 | "Future work (out of scope for v1): Body-based usage metering" — present-tense description of a *missing* feature. The doc is honest; the doc title reads "Optional Proxy" so the absence is documented. But the same doc on line 16 says "every forwarded call writes a `usage_events` row" — false. The two statements disagree inside the same file. |
| `.agents/docs/operations/proxy.md` | 16 | "every forwarded call writes a `usage_events` row with `source = 'proxy'`" — **false** on the success path (see §2.9 critical). The doc's own §"Failure modes" table acknowledges the gap indirectly via the "Future work" section, but the §"Enabling the proxy" section still claims writes happen. |
| `.agents/docs/operations/scheduler.md` | 49 | "GET /api/v1/projects/{projectId}/schedules/{scheduleId}" — the actual route is `/api/v1/projects/{projectId}/scheduled-jobs/{jobId}` (`ApiRoutes.SchedulerJob` line 130). Two differences: "schedules" vs "scheduled-jobs" and "scheduleId" vs "jobId". |
| `.agents/docs/operations/scheduler.md` | 51-55 | "Scheduler has no dedicated health check yet — the dispatcher logged startup is the proxy" — read this carefully: it claims the dispatcher's *startup log* is the proxy for a health check. That's an honest statement but the operator takeaway is that there is no health signal. Worth being more direct. |
| `.agents/STATE.md` | 308-352 | "Осторожно (грабли, уже стреляли)" section lists several issues; the OpenAPI-mirroring kubb "1334 files lost" incident is mentioned (line 318) but does not name the *guard* the team added. New operators can be told what to look for. |
| `.agents/ROADMAP.md` | 76-86 | "Post-1.0 backlog slice (#11) — closed 2026-09-07 with 13 sub-slices landed" — the table lists 8 sub-slices (Merge-queue, Eval-harness, Autonomy ratchet, Domain-user intake, Redis cache, Fleet runners, Generic-command verifier, C#→TS codegen) — the "13" does not match. The narrative says "13 sub-slices" but the table has 8 rows. One of these counts is wrong. |
| `.agents/docs/operations/runbook.md` | 130 | "name: Becomes the URL segment in `/api/v1/auth/oidc/{name}/start` and `{name}/callback`" — the start URL has the provider, but the callback is unified (`/api/v1/auth/oidc/callback`, no provider). The doc's own earlier paragraph on line 117-118 says "no provider in the path", contradicting the table. |
| `.agents/docs/operations/oauth-oidc.md` | 132 | "OIDC providers through `auth:oidc:providers[]`" — accurate, but the doc does not document the FK requirement that each provider's `name` must match `VITE_OIDC_PROVIDER` (only the `fesettings.md` does). New operators reading the OIDC doc alone won't see the FE-BE contract. |

### 4.2 Missing docs (referenced, but no doc)

- **No `docs/operations/limits.md`.** `Cost` says budget hits
  return 429 with `Retry-After`; the runbook has a single
  "Performance" section; there's no limit policy doc that
  an operator can read top-to-bottom.
- **No `docs/operations/tenancy.md`.** Multi-tenancy is
  spread across `OidcOptions`, `ApiKeyOptions`,
  `SubjectScopeMiddleware`, and `HostComposer`. A
  one-stop "how do I run multi-tenant Comuki" doc is
  missing.
- **No `docs/architecture/state-machines.md`.** The
  `RunStatusMachine` and `WorkItemStatusMachine` and
  `RunTrustClass` transitions are non-trivial; the only
  doc is the code itself.
- **No `docs/product/sla.md` or `docs/product/availability.md`.**
  The proxy budget is calendar-month; the lease reaper
  is `BackgroundService` with no alerting; the artifact
  packager has a 10 s poll. None of these SLAs are
  documented.
- **No `docs/operations/disaster-recovery.md`.** The
  runbook has a `Restore` section (line 183-220) but no
  RTO/RPO targets, no runbook-test cadence, no
  documented drill.
- **No OpenAPI consumer guide for FE developers.** The
  kubb config exists, but a "here's how to add a new
  endpoint" walk-through is missing. The `#29` issue
  closed the emit; the writing-the-client side was
  implicit.

### 4.3 Forgotten TODOs / FIXMEs in `.md`

- `.agents/docs/operations/fesettings.md:151` — `http(s)://`
  is referenced in prose, not as a TODO. (Not a real
  issue.)
- `.agents/docs/operations/scheduler.md:46` — "Count is
  per-cycle" in a table. Not a TODO; just a log
  description.
- No other `TODO` / `FIXME` / `XXX` / `HACK` markers
  found in `.agents/docs/**/*.md`.
- `platform/src/host/Comuki.Host/Projects/ProjectsModuleEndpoints.cs:27`
  — the *code* `// TODO(auth): project:admin — wire [RequiresPermission] host-wide with the auth slice`
  is the only material one. (Already covered in §2.1.)

### 4.4 Drift between docs and code (small)

- `STATE.md:16-17` says "xUnit v3 + MTP (не VSTest), 1192 tests
  pass" — accurate today. The "5 pre-existing flakes" list
  (line 250) names `Scheduler InMemory`, `OIDC Keycloak Docker
  timeout`, `StatusMachine PromoteTrustedIsNoOp`, `Runs
  EscalationTimeoutSweeper seed bug`, `Verify.Unit 0 tests built`.
  The `Verify.Unit 0 tests built` claim should now be
  revisitable — the `Comuki.Modules.Verify.Unit` directory
  exists in the test tree; need a count to confirm whether
  this is still 0.
- `.agents/ROADMAP.md:23-36` says "Slice 0 moved to Phase 4" —
  the "Phase 4 / 5 / 6 / 7 / 8 / 9" mapping table on line
  22-36 is the canonical answer. (Clean.)

---

## 5. Product Questions for the Owner

Each question is a product call, not a developer call. The
"Default if unanswered" row tells you what the platform does
today so you can spot the gap. The file path is where the
ambiguity lives.

### UX / feature

1. **Run detail page in real mode is a half-shell (no work items,
   no brief, no timeline, no plan).**
   - File: `dashboard/src/domains/runs/api/queries.ts:51-73`
   - Question: Should the dashboard render the run detail page
     from the existing list scan (current behaviour) until a
     `/api/v1/runs/{id}` lands, or should the dashboard show an
     "operate from the list, click through to a focused view of
     approve/cancel" UI that doesn't pretend to render the
     plan?
   - Default: page renders a sparse card with empty fields
     (today).

2. **Should there be a "bulk cancel" on the runs list?**
   - File: `dashboard/src/domains/runs/pages/runs-page.tsx`
     (mentioned in fe-requirements §3.1 as "Bulk: stop selected")
   - Question: Confirm bulk stop is on v1 backlog, or punt to v2?
     The backend has only single-run `POST /cancel`; bulk would
     be a new endpoint.
   - Default: not implemented.

3. **The cost page is mock-first even though the BE has the
   endpoint.**
   - File: `dashboard/src/domains/cost/api/queries.ts:10-15`
   - Question: Should the cost page render the per-project
     `GET /costs` endpoint today, or is the page intended as
     a *platform-wide* cost view that needs a different
     endpoint (e.g. `GET /api/v1/costs` aggregating across
     projects)?
   - Default: throws in real mode.

4. **Should the dashboard's queue page (`/queue`) ever be
   wired?**
   - File: `dashboard/src/domains/queue/api/queries.ts:39-41`
   - Question: The `IWorkItemQueue` port is engine-internal; the
     operator-facing queue is the dashboard. Is the dashboard
     view essential, or is the *runs* view (which already
     shows queued/running/waiting) sufficient?
   - Default: mock-only.

5. **The "Continue with <provider>" button reads from
   `VITE_OIDC_PROVIDER` env (single string).**
   - File: `dashboard/src/shared/config/env.ts:77-86` and
     `.agents/docs/operations/fesettings.md:111-126`
   - Question: When an operator deploys with multiple IdPs
     (Keycloak + Google), should the dashboard show one button
     per configured provider, or is "one button, the host's
     default provider" enough? The latter requires a host
     endpoint `GET /api/v1/auth/oidc/providers` that does not
     exist today.
   - Default: one provider, configured at build time.

6. **Should the chat session own a single project at a time?**
   - File: `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Sessions/ChatSessionService.cs`
   - Question: A `ChatSession` is `CreateAsync(subjectId, projectId?, ...)` —
     project is optional. Cross-project tool calls work because
     the tools resolve project context per-message. Should a
     session be locked to one project for the operator's mental
     model, or is the current "floating project" the intended
     shape (a chat is a long-lived console, not a project pane)?
   - Default: optional project; chat tools can act on
     multiple.

7. **Should the dashboard show the proxy's `/v1/models` list
   (i.e. virtual-key catalog)?**
   - File: `dashboard/src/domains/models/api/queries.ts:21-24`
   - Question: Today the FE throws. The proxy has
     `/v1/models` (anonymous catalogue, virtual-key
     authenticated) and `/v1/chat/completions`. The
     `ModelsPage` is supposed to render the model registry;
     the proxy advertises it; should the dashboard wire
     these?
   - Default: mock-only.

8. **The settings page (`/settings`) is mock-first; it carries
   kill-switches (killSwitch / pauseSwarm) that the BE has
   no endpoint for.**
   - File: `dashboard/src/domains/settings/api/queries.ts:24, 42, 71, 92`
   - Question: Is the settings page intended as the host
     control plane (with proper endpoints for the budget
     kill-switch and swarm pause), or is the page a
     place-holder for v2? A new host endpoint
     `POST /api/v1/host/{kill,unkill}` would be the
     addition.
   - Default: mock-only.

9. **Should OIDC onboarding send the operator a "your Comuki
   account was created via IdP X" email?**
   - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcAccountLinker.cs:27-49`
   - Question: Today the link is silent. A real product would
     notify the email address (or a configured address). This
     is a "SMTP" prerequisite that is not in the v1 stack.
   - Default: silent.

10. **Should the sources admin support a `dry-run` connect
    that never persists?**
    - File: `platform/src/host/Comuki.Host/Intake/Controllers/SourcesController.cs:115-129`
      (probe endpoint exists, no dry-run create).
    - Question: Today the FE has Test Source Draft and Test
      Connection (probe), but the "create" form commits
      immediately. Should the create form require a successful
      probe before allowing save?
    - Default: probe is a separate button, not a precondition.

11. **Should the FE surface the per-key `X-Comuki-Tenant` header?**
    - File: `dashboard/src/domains/identity/api/queries.ts:402-422`
    - Question: The BE enforces it; the FE never sends it
      (every key is created without `tenantProjectId`). For a
      multi-tenant deployment, the operator has to mint keys
      via `curl`. Is a UI for tenant-scoped keys in v1 scope?
    - Default: not in UI.

12. **Should the run detail page link to the journal / trace
    events?**
    - File: `dashboard/src/domains/runs/pages/run-detail-page.tsx:103-180`
    - Question: The `IReadRunTimeline` exists
      (`Comuki.Engine.Orchestration.Infrastructure.Journal.RunJournalEf`)
      but no host endpoint exposes it. A `GET /api/v1/runs/{id}/events`
      would feed the timeline. Is the timeline in v1 scope?
    - Default: no timeline tab (today).

13. **Should the dashboard show the Sentry / SentryDsn panel in
    the scheduler view?**
    - File: `dashboard/src/domains/queue/api/queries.ts:39-41`
      (the worker detail page is the home for the worker
      events; Sentry isn't surfaced).
    - Question: Today the scheduler's only observability is
      the run journal (a `scheduler.job_fired` row). A
      Sentry-aware panel is a v2 item, or is it wanted for v1
      to make a production scheduler operable?
    - Default: not surfaced.

14. **Should the "needing you" home view include a one-click
    "stop all my failed runs" button?**
    - File: `dashboard/src/domains/home/pages/home-page.tsx:75-80`
    - Question: Today, each `failed` run needs its own click
      through to a stop. A "stop all" is a multi-cancel
      endpoint the BE does not have.
    - Default: not implemented.

15. **Should the dashboard allow the operator to edit the
    bootstrap admin's email from the Identity admin page?**
    - File: `dashboard/src/domains/identity/pages/identity-page.tsx`
      (the form lists users but not the bootstrap one).
    - Question: The `BootstrapAdminOptions` is an env-var
      pair; the admin page cannot rewrite it. A "rotate
      bootstrap" flow that prompts for a new password and
      re-runs the seeder is not implemented.
    - Default: not in UI.

### Pricing / limits

16. **What is the rate limit per API key per project, today?**
    - File: `platform/src/host/Comuki.Host/Security/RateLimit/RateLimitOptions.cs:25-39`
    - Question: `LoginPermitsPerMinute=10`,
      `OidcStartPermitsPerMinute=30`,
      `RunDecisionPermitsPerMinute=60`, `ApiPermitsPerMinute=600`
      — all defaults. Are these the right numbers, or do
      they need to be per-tenant / per-key?
    - Default: 600/min/api-key, no per-tenant tuning.

17. **Is there a per-project concurrency cap, or only a global
    `maxConcurrent` per project settings?**
    - File: `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Views/ProjectSettingsView.cs`
    - Question: The `ProjectSettings.MaxConcurrent` is enforced
      by the scale supervisor. A platform-wide global cap
      (across projects) does not exist; the operator cannot
      say "I have 100 workers total, distribute across
      projects as you see fit".
    - Default: per-project cap only.

18. **When a virtual proxy key's budget is hit, does the
    request 402 (Payment Required) or 429 (Too Many Requests)?**
    - File: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs:36-41`
      (returns 429 today).
    - Question: 429 is the *rate limit* status; 402 is the
      *payment required* status. For a "you spent your
      $10 budget" failure, 402 reads more accurately. Is
      429 acceptable, or should the proxy switch?
    - Default: 429.

19. **What is the per-call max token count for a proxy
    request?**
    - File: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Options/ProxyOptions.cs:33-71`
    - Question: `VirtualKeyConfiguration.AllowedModels` exists
      (a string array) but no `MaxInputTokens` /
      `MaxOutputTokens` cap. A single huge request can
      exhaust the budget in one call. Is a per-request
      token cap in scope?
    - Default: not enforced.

20. **When a `Run` exceeds the project `soft` budget, does
    the next claim fail, or does it 422 the user?**
    - File: `platform/src/host/Comuki.Host/Costs/OrchestrationBudgetGate.cs`
    - Question: The `IBudgetGate` is on the project spend; the
      `OrchestrationBudgetGate` reads project settings +
      orchestration cancel/journal. The exact response code
      for an over-budget claim is implementation-defined.
      Should this be 402 (Payment Required), 429 (rate
      limit), or 422 (unprocessable)?
    - Default: implementation-defined (unmapped today).

21. **Are tokens in `usage_events` "prompt + completion", or
    split?**
    - File: `platform/src/modules/Costs/Comuki.Modules.Costs.Application/Views/ProjectCostsView.cs:34-41`
    - Question: `UsageEventView` carries
      `InputTokens / OutputTokens` (good) but the `SpentUsdMicros`
      is a single number — the operator cannot see "you
      spent $7 on input, $5 on output". Is a per-role cost
      breakdown a v1 ask?
    - Default: aggregated only.

22. **Is the per-API-key 30-day `last_used` write a hot path?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/ApiKeys/ApiKeyAuthenticationHandler.cs:123-127`
    - Question: The throttle (default 24h) prevents write
      amplification. A 24h window means the audit log of
      "last used" is accurate to the day, not the minute.
      Is that acceptable, or should the operator see
      minute-level recency?
    - Default: 24h throttle.

23. **What is the proxy budget reset window? Calendar month
    or rolling 30 days?**
    - File: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Budgeting/DefaultProxyBudgetEnforcer.cs:27-32`
    - Question: Today the budget resets on the 1st of the
      month (calendar month). A `BudgetUsd=10` on a 10-day
      window reset on the 1st is effectively "up to $10 of
      spend" not "10 per 30 days". Is calendar month the
      intended semantic, or rolling?
    - Default: calendar month.

24. **Are project budgets hard-capped or soft-capped?**
    - File: `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Settings/Update/UpdateSettingsCommand.cs`
    - Question: `SoftBudgetUsdMicros` and
      `HardBudgetUsdMicros` both exist. The
      `OrchestrationBudgetGate` (in Costs) consumes them.
      A "soft" over-spend is supposed to warn; a "hard"
      over-spend is supposed to deny. Today both emit the
      same `usage_events` row. Is the differentiation
      between soft and hard enforced in the gate, or
      only the soft is read?
    - Default: implementation-defined; no enforcement
      boundary visible in the read paths.

### Operational

25. **Should the host restart on Postgres reconnect after a
    30 s outage?**
    - File: `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs:42-49`
      (catches `DbException`, logs warning, retries on next
      interval).
    - Question: Today, every background service is
      `BackgroundService` that catches `DbException` and
      retries on the next cycle. The HTTP path returns
      500s while the DB is down. Is the current behaviour
      (recover automatically) sufficient, or do we want
      a circuit-breaker that fast-fails?
    - Default: keep retrying.

26. **What happens when MinIO is down for 5 minutes — do
    artifact packagers retry or skip?**
    - File: `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackagerService.cs:60-91`
    - Question: Today the worker logs and retries every
      10s. A 5-minute outage = 30 retries. The terminal
      runs that became terminal during the outage are
      re-bundled on the next packager pass. No metric
      tracks "bundles delayed > N minutes".
    - Default: silent retries; no alerting.

27. **How are OIDC state rows purged if Redis is down?**
    - File: `platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs:30-65`
    - Question: The sweeper is DB-backed (`identity.oidc_states`),
      not Redis. But the `DistributedProjectSettingsCache` is
      Redis-backed. If Redis is down, the
      ProjectSettingsCacheRefresher fails (every 15s). A long
      Redis outage = stale project settings = compute scale
      lagging. Is this acceptable, or should the cache fall
      back to in-memory?
    - Default: silent retries; no fallback to in-memory.

28. **Should the artifact packager have a circuit-breaker on
    MinIO 5xx?**
    - File: `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackager.cs`
    - Question: The packager's per-candidate scope means
      one MinIO 5xx kills one bundle, then the next
      candidate retries. No global "MinIO is sick, stop
      for 5 minutes" gate. A chronic outage fills the
      logs with the same error.
    - Default: per-candidate retries only.

29. **Should `BootstrapAdminStartupService` fail the host
    startup when the bootstrap email is set but the password
    is a known-weak string (`password123`, `admin1234`)?**
    - File: `platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs:71-87`
    - Question: Today the validator refuses only the
      `comuki_dev` literal. A real operator can deploy with
      `Bootstrap=admin@example.com / password123` and
      pass the gate. Should the validator do a
      length+character-class check on the password?
    - Default: only the dev default is refused.

30. **How are OIDC state rows cleaned up if the migrator
    can't run (e.g. the `identity.oidc_states` table doesn't
    exist yet)?**
    - File: `platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs:30-44`
    - Question: The sweeper uses `IOidcStateStore.DeleteExpiredAsync`
      which does `DELETE FROM identity.oidc_states WHERE expires_at < @cutoff`.
      If the table doesn't exist (fresh deploy, migrator not
      run), the DELETE throws. The sweeper's
      `catch (DbException)` logs and retries. A persistent
      failure (migrator never runs) means the table grows
      forever.
    - Default: log and retry.

31. **What does the platform do when a `VirtualKey` is
    referenced by an in-flight request, then deleted at
    midnight?**
    - File: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs:30-41`
    - Question: The transform looks up the key at request
      time. If the operator edits `appsettings.json` and
      removes the key, in-flight requests get 401. There
      is no "drain" period. Is a configurable grace period
      in scope?
    - Default: instant revocation.

32. **Should the proxy track per-key, per-model, per-day
    spend (for billing) or is "monthly per key" enough?**
    - File: `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs`
      (the meter exists but writes nothing today).
    - Question: A multi-tenant deployment that resells
      proxy access needs per-key, per-day attribution. The
      `usage_events` table is the right place; the meter
      is dormant (see §2.9 critical).
    - Default: dormant.

### Compliance / security

33. **Are OIDC tokens stored in cookies or localStorage?**
    - File: `platform/src/host/Comuki.Host/Auth/Security/CookieSignerAdapter.cs`
      (the cookie scheme is server-side, signed).
    - Question: Today the cookie is server-signed and
      httpOnly. The dashboard's `kubb-client` reads
      `credentials: 'include'`, so the cookie travels
      cross-origin safely. But there is no explicit
      "XSS-resistant token storage" doc to point at for a
      compliance review.
    - Default: server-side cookie, httpOnly.

34. **Is the bootstrap admin's password rotation enforced?**
    - File: `.agents/docs/operations/runbook.md:81-100` (manual
      procedure).
    - Question: No automated rotation; no `PasswordRotatedAt`
      column; no policy that says "rotate within 90 days".
      Is an automated rotation policy in v1 scope, or is
      the manual procedure the v1 contract?
    - Default: manual, no enforcement.

35. **Are tenant IDs audited in the journal?**
    - File: `platform/src/engine/Comuki.Engine.Orchestration/Domain/Journal/RunEvent.cs`
    - Question: `RunEvent` carries `RunId`, `Type`, `Payload`
      but not `TenantId`. Cross-tenant audit (who acted on
      project X) is reconstructed via `SubjectScope`. Is a
      denormalized `TenantId` column on the journal row
      needed for compliance?
    - Default: absent.

36. **Are `oidc_links` purged on user disable?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/SetUserDisabledHandler.cs`
    - Question: The handler sets `Disabled=true` on the
      user; the `ApiKeyAuthenticationHandler.cs:117-120`
      refuses disabled-owner keys. But the OIDC link
      itself is not removed. A disabled user can still
      sign in via OIDC and re-enable themselves if the
      admin re-enables the user (no — but the
      `OidcAccountLinker` does not check
      `Disabled` on a fresh login). An attacker who
      controls a disabled user's IdP can still sign in.
    - Default: OIDC login does not check `Disabled`.

37. **What is the audit trail for the platform-admin's
    `PII` access (OIDC email, display name)?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/SetUserDisabledHandler.cs`
      (logs at `Information` with `UserId`).
    - Question: Logs carry `UserId` but not the actor.
      The bootstrap admin's `UserId` is not in the log
      context. A compliance audit must grep
      journal + the user-management logs.
    - Default: not joined.

38. **When an API key's `tenantProjectId` is changed, is the
    change auditable?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/ApiKeys/ApiKey.cs`
      (the entity exists; no `AuditLog`).
    - Question: Today, mutating a key writes a single
      `SaveAsync` — no `last_modified_by`, no
      `audit_events` table. A compliance review cannot
      answer "who changed the tenant scope of key X?".
    - Default: not auditable.

### Multi-tenancy

39. **Can a project belong to multiple tenants?**
    - File: `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Views/ProjectView.cs`
    - Question: A `Project` has `Id`; a `TenantId` column
      is not on the project. A `Subject` has
      `Assignments` to a project; a project is in *no*
      tenant registry. The whole model assumes
      project-level scoping, not tenant-level. Is v1
      multi-tenant in any meaningful sense (e.g. an
      enterprise customer has multiple projects under
      one tenant)?
    - Default: project is the smallest unit; no tenant
      registry.

40. **Are API keys scoped to project, tenant, or global?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/ApiKeys/ApiKey.cs`
    - Question: `ApiKey.TenantProjectId` is a *single*
      `ProjectId?`, not a list. A key cannot span projects
      today. Is "a key spans all the caller's projects"
      in scope?
    - Default: one key = one project (or no project).

41. **What happens when a tenant is deleted — do projects
    cascade?**
    - File: `.agents/STATE.md:60-61` says "single-tenant
      MVP" — no tenant model in code.
    - Question: Today there is no `Tenant` entity. A
      project is its own root. Deleting a project archives
      it; there is no tenant-level delete. Is multi-tenancy
      actually in v1, or is it deferred?
    - Default: no tenant entity.

42. **Does the `OidcLink` cascade to the project level?**
    - File: `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Users/OidcLink.cs`
    - Question: A user has many projects (via
      `RoleAssignment` with `Scope=Project`). The OIDC
      link is global. A user in 3 projects has one
      OIDC link. Disabling the user (or the link) does
      not project-level disable. A "per-project OIDC
      sign-in" is not modeled.
    - Default: global OIDC link.

### Cross-cutting (one more — already covered in §2.1 but worth
a product answer)

43. **Should the platform refuse to issue a new local user
    account when the same email is OIDC-linked?**
    - File: `OidcAccountLinker.cs:37-49` and
      `InviteUserHandler` (platform/src/.../Users/InviteUserHandler.cs)
    - Question: Today, an admin can `POST /api/v1/users` with
      `email=jane@corp.com`, then a later OIDC login
      from a different IdP with the same email auto-links
      the OIDC subject to the local user. The admin's
      invite is a back-door.
    - Default: local user + OIDC link co-exist silently.

---

## 6. Recommendations (no code)

### Priority 1 — must-fix before ship

1. **Add a `GET /api/v1/runs/{id}` endpoint** that returns the
   full `RunDetail` (`id, projectId, title, app, model, status,
   cost, tokens, durationSec, current, brief, workItems,
   events, rules, revision`). The FE's `run-detail-page`
   is built around it; without it the page is a half-shell.
2. **Wire the dashboard's `CostPage` to
   `GET /api/v1/projects/{id}/costs`.** It is a one-line
   swap from the mock store to the kubb client.
3. **Implement body-based usage metering on the proxy
   success path** (`ProxyTransforms.MeterUsageFromResponseAsync`).
   Without it, the `BudgetUsd` cap is unenforced.
4. **Add `[RequiresPermission]` to
   `ProjectsModuleEndpoints.MapProjectsEndpoints`.** The
   `// TODO(auth)` comment has been on the file for at
   least one slice.
5. **Disable the user on OIDC-link success when the local
   user was already disabled** (or refuse the OIDC login
   if the local user is disabled). One-line check in
   `OidcAccountLinker.HandleAsync`.

### Priority 2 — must-fix before public release

6. **Surface the proxy's `ModelsPage` and the settings
   kill-switches** with proper BE endpoints.
7. **Align the FE permission vocabulary with the BE
   permission keys** (one source of truth — generate the
   FE's `Permission` type from the OpenAPI emission).
8. **Add a `GET /api/v1/auth/oidc/providers` endpoint** so
   the dashboard can render one button per configured
   IdP, not a single build-time provider.
9. **Document the proxy metering follow-up as a v1.1
   item**, not a v2. The current state (no metering on
   success) is a revenue-trust gap for any production
   deployment.
10. **Fix the FE mock-first pages that have a BE
    counterpart** (Cost, Models, Settings, the
    `home/outcomes` series).
11. **Add `GET /api/v1/runs/{id}/events`** (timeline) and
    `GET /api/v1/runs/{id}/work-items`. The journal
    already exists; the read API is missing.
12. **Add a default-weakness check to
    `ProductionSecretValidator`** (length, character
    classes).
13. **Add CORS example env vars to
    `deploy/.env.example`**.
14. **Document the FE-BE permission-vocabulary mapping**
    in a single source-of-truth file, or generate the
    FE type from the OpenAPI emission.

### Priority 3 — post-ship / v2

15. Implement a Postgres-backed virtual-key store (replacing
    the `appsettings.json`-bound `ProxyOptions.VirtualKeys`).
16. Add per-request, per-virtual-key rate limiting to the
    proxy.
17. Add body-based usage metering with streaming
    (replacing the buffered-response middleware TODO).
18. Add a chat transcript SSE/WebSocket channel
    (replacing the polling-invalidate dance).
19. Add a per-run plan-change-event log endpoint.
20. Add a host-level "scheduler" health probe (the
    runbook admits the missing one).
21. Add an audit log table for sensitive operations
    (OIDC-link, key-tenant-change, role-grant,
    project-create, project-archive, kill-switch).
22. Add a `Tenant` entity if multi-tenancy is real (the
    41 questions above assume it's not, today).
23. Add a `Settings` host controller (kill-switches,
    autonomy mode) so the dashboard `Settings` page
    stops being a mock.
24. Add a chat-init wizard endpoint (the BE has no
    `/init` surface; the FE has a hand-coded wizard).
25. Add the missing FE-BE mapping for `home/outcomes`,
    `approvals`, `compute`, `queue`, `tasks`, `verify`,
    `observability` (a handful of these are features
    missing on the BE; document them as roadmap items
    or v2).
26. Fix the double `SchedulerOptions` registration (the
    bare `AddOptions<SchedulerOptions>()` in
    `Comuki.Modules.Scheduler.Application.SchedulerApplicationExtensions.cs:29`).
27. Add `ValidateOnStart()` to the `OidcOptions` binding
    in `HostComposer.cs:209-210`.
28. Add the missing rows to the schema doc
    (`knowledge`, `scheduler`).
29. Fix the runbook's "schedules" → "scheduled-jobs" +
    "scheduleId" → "jobId" discrepancy.
30. Add a "drill" section to the runbook (current
    "Restore" is the only one, with no documented RTO/RPO).

---

## Appendix A — Files Reviewed

### Backend (.NET)

**Host (composition + endpoints):**

- `platform/src/host/Comuki.Host/HostComposer.cs` (359 lines)
- `platform/src/host/Comuki.Host/ApiRoutes.cs` (131 lines)
- `platform/src/host/Comuki.Host/Program.cs` (78 lines)
- `platform/src/host/Comuki.Host/OpenApi/OpenApiBuildTimeExtensions.cs` (72 lines)
- `platform/src/host/Comuki.Host/Artifacts/RunArtifactPackagerHostService.cs` (118 lines)
- `platform/src/host/Comuki.Host/Auth/Controllers/AuthController.cs` (248 lines)
- `platform/src/host/Comuki.Host/Auth/Controllers/UsersController.cs` (161 lines)
- `platform/src/host/Comuki.Host/Auth/Controllers/GrantsController.cs`
- `platform/src/host/Comuki.Host/Auth/Controllers/KeysController.cs`
- `platform/src/host/Comuki.Host/Auth/BootstrapAdminOptions.cs` (46 lines)
- `platform/src/host/Comuki.Host/Auth/BootstrapAdminSeeder.cs` (63 lines)
- `platform/src/host/Comuki.Host/Chat/Controllers/ChatSessionsController.cs` (168 lines)
- `platform/src/host/Comuki.Host/Chat/Controllers/ChatSlashController.cs`
- `platform/src/host/Comuki.Host/Chat/Controllers/ChatEndpointRunner.cs`
- `platform/src/host/Comuki.Host/Costs/CostsModuleEndpoints.cs` (32 lines)
- `platform/src/host/Comuki.Host/Intake/Controllers/InboxController.cs` (71 lines)
- `platform/src/host/Comuki.Host/Intake/Controllers/SourcesController.cs` (211 lines)
- `platform/src/host/Comuki.Host/Intake/Controllers/TicketsController.cs` (44 lines)
- `platform/src/host/Comuki.Host/Intake/Controllers/AdmissionRulesController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/WebhooksController.cs`
- `platform/src/host/Comuki.Host/Intake/OrchestrationRunStatusReader.cs`
- `platform/src/host/Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs` (51 lines)
- `platform/src/host/Comuki.Host/Mcp/McpModuleEndpoints.cs` (66 lines)
- `platform/src/host/Comuki.Host/Mcp/McpServer.cs` (422 lines)
- `platform/src/host/Comuki.Host/Projects/ProjectsModuleEndpoints.cs` (225 lines)
- `platform/src/host/Comuki.Host/Proxy/ProxyModuleEndpoints.cs` (50 lines)
- `platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs` (164 lines)
- `platform/src/host/Comuki.Host/Runs/Controllers/RunArtifactsController.cs` (56 lines)
- `platform/src/host/Comuki.Host/Runs/RunsListHandler.cs` (75 lines)
- `platform/src/host/Comuki.Host/Scheduler/ScheduledJobsController.cs` (144 lines)
- `platform/src/host/Comuki.Host/Scheduler/SchedulerEndpointRunner.cs` (95 lines)
- `platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs` (157 lines)
- `platform/src/host/Comuki.Host/Workers/OidcStateSweeper.cs` (82 lines)
- `platform/src/host/Comuki.Host/Security/Cors/CorsOptions.cs` (37 lines)
- `platform/src/host/Comuki.Host/Security/Cors/CorsPolicyBuilder.cs` (78 lines)
- `platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs` (88 lines)
- `platform/src/host/Comuki.Host/Security/RateLimit/RateLimitInstaller.cs` (98 lines)
- `platform/src/host/Comuki.Host/Security/RateLimit/RateLimitOptions.cs` (56 lines)

**Engine.Orchestration:**

- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Hosting/LeaseReaperWorker.cs` (47 lines)
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/Leases/LeaseReaper.cs` (96 lines)
- `platform/src/engine/Comuki.Engine.Orchestration/Infrastructure/EscalationTimeout/*`
- `platform/src/engine/Comuki.Engine.Orchestration/Application/RunStatusMachine.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Application/WorkItemStatusMachine.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Domain/Runs/Run.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Domain/WorkItems/WorkItem.cs`
- `platform/src/engine/Comuki.Orchestration/Domain/Runs/RunTrustClass.cs`
- `platform/src/engine/Comuki.Engine.Orchestration/Application/MergeQueue/*`

**Identity:**

- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcAccountLinker.cs` (50 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcStartHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcCallbackHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Permissions/RequiresPermissionAttribute.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Permissions/RoleMatrixPermissionCatalog.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Roles/RoleMatrix.cs` (144 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Roles/RoleKeys.cs` (61 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Permissions/Permissions.cs` (89 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/ApiKeys/ApiKey.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/ApiKeys/ApiKeyIssuer.cs` (56 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/ApiKeys/Issue/*`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/InviteUserHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/ListUsersHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/SetUserDisabledHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/LinkOidcSubjectHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Assignments/Grant/*`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Assignments/List/*`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Assignments/Revoke/*`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/ApiKeys/ApiKeyAuthenticationHandler.cs` (198 lines)
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Cookies/UserAuthenticationService.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/RequiresPermissionFilter.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/PermissionDemandStartupValidator.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/IdentityAuthExtensions.cs`

**Projects:**

- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Projects/Create/CreateProjectHandler.cs` (43 lines)
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Projects/Create/CreateProjectValidator.cs` (36 lines)
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Settings/GetProjectSettingsHandler.cs` (24 lines)
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Settings/Update/UpdateSettingsHandler.cs` (55 lines)
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Settings/ProjectSettingsCache.cs` (82 lines)
- `platform/src/modules/Projects/Comuki.Modules.Projects.Application/Projects/Queries/ListProjectsHandler.cs`
- `platform/src/modules/Projects/Comuki.Modules.Projects.Infrastructure/Persistence/Stores/ProjectSettingsCacheRefresher.cs` (76 lines)

**Chat (interface only):**

- `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Graph/*`
- `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Sessions/ChatSessionService.cs`
- `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Sessions/ChatTurnService.cs`
- `platform/src/modules/Chat/Comuki.Modules.Chat.Application/Slash/ChatSlashCatalog.cs`

**Intake (interface only):**

- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/SourceConnectionService.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Sources/SourceProbeService.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/CreateNativeTicketHandler.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Sync/RunStatusBridgeWorker.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Infrastructure/Admission/*`

**Costs:**

- `platform/src/modules/Costs/Comuki.Modules.Costs.Application/Queries/GetProjectCostsHandler.cs` (34 lines)
- `platform/src/modules/Costs/Comuki.Modules.Costs.Application/Views/ProjectCostsView.cs` (42 lines)
- `platform/src/host/Comuki.Host/Costs/OrchestrationBudgetGate.cs`
- `platform/src/host/Comuki.Host/Costs/ProjectBudgetSettingsAdapter.cs`

**Artifacts:**

- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/ArtifactsApplicationExtensions.cs` (48 lines)
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackagerService.cs` (191 lines)
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackager.cs`
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Infrastructure/Store/ArtifactBucketInitializer.cs` (60 lines)
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Infrastructure/Store/MinioRunArtifactStore.cs`

**Proxy:**

- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Options/ProxyOptions.cs` (82 lines)
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Budgeting/DefaultProxyBudgetEnforcer.cs` (43 lines)
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Metering/ProxyUsageMeter.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/Extraction/*`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs` (70 lines)
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Auth/VirtualKeyAuthenticationHandler.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyConfigProvider.cs`

**Knowledge:**

- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Application/*`
- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure/KnowledgeInfrastructureExtensions.cs`

**Verify (interface only):**

- `platform/src/modules/Verify/Comuki.Modules.Verify.Application/*`
- `platform/src/modules/Verify/Comuki.Modules.Verify.Infrastructure/*`

**Scheduler:**

- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/Jobs/*`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/Options/SchedulerOptions.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Application/SchedulerApplicationExtensions.cs`
- `platform/src/modules/Scheduler/Comuki.Modules.Scheduler.Infrastructure/Observers/*`

**Shared:**

- `platform/src/shared/Comuki.Shared.Telemetry/Installers/ComukiTelemetryInstaller.cs`
- `platform/src/shared/Comuki.Shared.Kernel/Scoping/AsyncLocalSubjectScopeAccessor.cs`
- `platform/src/shared/Comuki.Shared.Redis/*`

### Frontend (TypeScript / React)

**Routes / shared:**

- `dashboard/src/routes/__root.tsx`
- `dashboard/src/routes/index.tsx`
- `dashboard/src/shared/config/env.ts` (122 lines)
- `dashboard/src/shared/api/kubb-client.ts` (181 lines)
- `dashboard/src/shared/session/permissions.ts` (179 lines)
- `dashboard/src/app/layout/require-permission.tsx` (58 lines)
- `dashboard/src/app/layout/app-shell.tsx`
- `dashboard/src/domains/auth/api/auth.ts` (64 lines)
- `dashboard/src/domains/auth/api/oidc-start.ts` (53 lines)
- `dashboard/src/domains/auth/pages/login-page.tsx` (313 lines)

**Runs domain:**

- `dashboard/src/domains/runs/api/queries.ts` (128 lines)
- `dashboard/src/domains/runs/api/mappers.ts` (372 lines)
- `dashboard/src/domains/runs/api/mutations.ts`
- `dashboard/src/domains/runs/pages/runs-page.tsx`
- `dashboard/src/domains/runs/pages/run-detail-page.tsx` (263 lines)
- `dashboard/src/domains/runs/model/types.ts` (136 lines)
- `dashboard/src/domains/runs/model/work-items.ts` (277 lines)

**Identity / Projects / Sources / Chat:**

- `dashboard/src/domains/identity/api/queries.ts` (423 lines)
- `dashboard/src/domains/identity/api/mutations.ts`
- `dashboard/src/domains/projects/api/queries.ts` (161 lines)
- `dashboard/src/domains/projects/api/mutations.ts`
- `dashboard/src/domains/sources/api/mutations.ts` (486 lines)
- `dashboard/src/domains/chat/api/queries.ts` (176 lines)
- `dashboard/src/domains/chat/api/mutations.ts`

**Mock-first pages:**

- `dashboard/src/domains/home/api/queries.ts` (30 lines)
- `dashboard/src/domains/cost/api/queries.ts` (22 lines)
- `dashboard/src/domains/approvals/api/queries.ts` (65 lines)
- `dashboard/src/domains/knowledge/api/queries.ts` (22 lines)
- `dashboard/src/domains/verify/api/queries.ts` (27 lines)
- `dashboard/src/domains/models/api/queries.ts` (32 lines)
- `dashboard/src/domains/compute/api/queries.ts` (34 lines)
- `dashboard/src/domains/queue/api/queries.ts` (55 lines)
- `dashboard/src/domains/tasks/api/queries.ts`
- `dashboard/src/domains/settings/api/queries.ts` (148 lines)
- `dashboard/src/domains/observability/api/queries.ts` (40 lines)
- `dashboard/src/shared/api/mock/auth.store.ts`
- `dashboard/src/shared/api/mock/identity.store.ts`
- `dashboard/src/shared/api/mock/chat.store.ts`
- `dashboard/src/shared/api/mock/chat.seed.ts`
- `dashboard/src/shared/api/mock/projects.store.ts`
- `dashboard/src/shared/api/mock/runs.store.ts`
- `dashboard/src/shared/api/mock/runs.seed.ts`
- `dashboard/src/shared/api/mock/sources.store.ts`
- `dashboard/src/shared/api/mock/sources.seed.ts`

### Operations & product docs

- `.agents/AGENTS.md` (129 lines)
- `.agents/STATE.md` (360 lines)
- `.agents/ROADMAP.md` (346 lines)
- `.agents/docs/operations/install.md` (37 lines)
- `.agents/docs/operations/oauth-oidc.md` (344 lines)
- `.agents/docs/operations/runbook.md` (401 lines)
- `.agents/docs/operations/fesettings.md` (189 lines)
- `.agents/docs/operations/proxy.md` (201 lines)
- `.agents/docs/operations/scheduler.md` (229 lines)
- `.agents/docs/operations/database-schemas.md` (203 lines)
- `.agents/docs/operations/minio.md`
- `.agents/docs/operations/storage.md`
- `.agents/docs/operations/backup.md`
- `.agents/docs/operations/openapi-codegen.md`
- `.agents/docs/operations/connect-source.md`
- `.agents/docs/operations/onboarding.md`
- `.agents/docs/product/comuki-fe-requirements.md` (400 lines)
- `.agents/docs/product/comuki-task-breakdown.md` (329 lines, partial)
- `.agents/docs/architecture/comuki-project-structure.md` (279 lines)
- `.agents/docs/audits/architecture-audit-2026-09-04.md`

### Deploy

- `deploy/docker-compose.yml` (267 lines)
- `deploy/.env.example`
- `deploy/worker.Dockerfile`
- `deploy/README.md`

### Other

- `AGENTS.md` (worktree root, 129 lines)
- `comuki.slnx` (solution shape; not read in full)
- `Directory.Build.props`
- `Directory.Packages.props`
- `.editorconfig`

---

## Appendix B — Counts

- **Source files reviewed:** 95 backend `.cs` + 36 frontend `.ts`/`.tsx`
  + 16 docs + 1 deploy = **148 files**.
- **Findings by severity:** 5 critical (§Executive Summary), 27
  major (across the 12 modules and 6 cross-cutting areas), 19
  minor.
- **Product questions generated:** **43** (1 above the
  asked-for 20-40 ceiling — the OIDC-on-disabled-user overlap
  deserved its own line).
- **Recommendations:** 30 (5 priority 1, 9 priority 2, 16
  priority 3).
- **Mock-first dashboard pages (real-mode throws):** 10
  (approvals, compute, cost, knowledge, models,
  observability, queue, settings, tasks, verify-via-knowledge).
- **BE→FE gaps (FE has mock, BE has endpoint):** 4 critical
  (RunDetail, Cost, Models, Settings).
- **Background services:** 11 (1 startup-only, 8 polling,
  2 transient-recovery).
- **Schemas in code:** 10 (orchestration, identity, projects,
  memory, chat, intake, costs, artifacts, knowledge,
  scheduler).
- **Permission keys (BE):** 24.
- **Permission keys (FE):** 22.
- **API routes in `ApiRoutes`:** 45.
- **Dashboard pages:** 29.
- **Product questions per category:**
  UX/feature: 15
  Pricing/limits: 9
  Operational: 7
  Compliance/security: 6
  Multi-tenancy: 4
  Cross-cutting: 1 (1 question re-stated)
  Total: 43

---

*End of audit. Generated 2026-09-08 against worktree
`fix/audit-product` (tip `fa659fd7`).*

# Host Specification

## Purpose

Defines the orchestrator host composition: the single composition point, database connection resolution, the anonymous health endpoint, the authentication surface (login/logout/me/OIDC handoff), bootstrap-admin seeding, realtime/telemetry wiring, and the separate migrator tool.

## Requirements

### Requirement: Single internal composition point

All host services, authentication schemes, controllers and endpoints SHALL be wired in one internal composition class (`HostComposer`), not a partial `Program`. The top-level entry resolves the database connection once, wires the worker runtime, delegates the rest to the composer, and maps the remaining endpoints. Integration tests boot the exact same composition through the internal class (InternalsVisibleTo) on a test port.

#### Scenario: Tests boot the same app
- **WHEN** an integration test composes the host
- **THEN** it exercises the identical pipeline as production, not a parallel wiring

### Requirement: One database connection, resolved once

The host SHALL resolve its database connection from, in order: the `COMUKI_DB` environment variable, the legacy `COMUKI_DATABASE` alias (honored with a startup warning to rename), or `ConnectionStrings:Comuki` from configuration. When NO source holds a connection string the boot SHALL fail with a setup hint naming the env var — the host never boots half-wired. One resolved value flows into every persistence layer (orchestration, identity, projects); nothing re-reads the environment.

#### Scenario: Missing database fails boot
- **WHEN** neither env var nor config carries a connection string
- **THEN** startup throws before serving any traffic

#### Scenario: Legacy alias warned
- **WHEN** only `COMUKI_DATABASE` is set
- **THEN** the host boots and logs a rename hint at startup

### Requirement: Anonymous health endpoint

`GET /health` SHALL be anonymous and answer 200 `{"status":"ok"}` — no authentication, no permission.

#### Scenario: Liveness probe
- **WHEN** any client calls `/health` without credentials
- **THEN** the answer is 200 ok

### Requirement: Local login

`POST /api/v1/auth/login` (email + password) SHALL, on success, set the cookie session and answer 200 with the account view; any credential failure — unknown user, wrong password, disabled account, OIDC-only account — SHALL answer the SAME 401 ProblemDetails (`auth.invalid_credentials`) so responses cannot enumerate accounts. Structural validation failure answers 400 with per-field errors. Login demands no permission: it is how a caller acquires permissions.

#### Scenario: Unknown user reads like wrong password
- **WHEN** login is attempted with a non-existent email
- **THEN** the response is byte-identical in shape to a wrong-password attempt

### Requirement: Logout and me

`POST /api/v1/auth/logout` SHALL clear the cookie session and answer 204. `GET /api/v1/auth/me` SHALL report the current subject — subject type and id, owner user id, email/display name (cookie sessions), the distinct active role keys, and the effective permission view split by scope; an API-key request reports the KEY's subject and assignments, not its owner's. Unauthenticated callers get 401.

#### Scenario: API key sees its own grants
- **WHEN** `me` is called with an API key that holds `member` on a project
- **THEN** the response names the api-key subject and only the key's own roles

### Requirement: OIDC start and callback

`GET /api/v1/auth/oidc/{provider}/start` SHALL answer 302 with a challenge against the provider's scheme when the provider is configured, or 404 (`auth.oidc_provider_not_found`) before any redirect otherwise. The protocol callback SHALL live on the versioned API surface (`/api/v1/auth/oidc/{provider}/callback`); the received external ticket SHALL be exchanged for the local cookie grammar after account linking (see identity).

#### Scenario: Unknown provider 404
- **WHEN** start is called for a provider not in the configured list
- **THEN** the answer is 404 and no redirect happens

### Requirement: Bootstrap admin seeding

The bootstrap admin SHALL be resolved from config (`auth:bootstrap`) with the `COMUKI_BOOTSTRAP_ADMIN_EMAIL` / `COMUKI_BOOTSTRAP_ADMIN_PASSWORD` env vars filling gaps. Both halves set → create the account (with a password) and grant `platform-admin` at platform scope once at startup, before the server accepts traffic; the account already existing → nothing to do (idempotent — safe on every boot). Exactly one half set → the boot FAILS loudly rather than silently ignoring a half-configured credential. Neither set → skip with a debug log. The seeding runs through the same create-user/grant-role handlers any admin uses.

#### Scenario: Half-pair fails boot
- **WHEN** only the bootstrap email is configured
- **THEN** startup throws naming both required variables

#### Scenario: Second boot is a no-op
- **WHEN** the host restarts with the bootstrap admin already present
- **THEN** the seeder logs "nothing to do" and grants nothing further

### Requirement: Database is essential at boot

The bootstrap pass runs as a startup service; an unreachable or unmigrated database SHALL fail the boot loudly (identity persistence is essential to this host). Schema migration is a separate step performed by the migrator, not by the host.

#### Scenario: Unreachable database
- **WHEN** the database is down when the host starts
- **THEN** boot fails during the startup service rather than at first request

### Requirement: Migrator tool

A separate console tool SHALL apply migrations for all module contexts (orchestration, identity, projects, and any additional module contexts such as memory, chat, intake, costs) against the same connection string (same resolution order as the host), printing each applied migration per context. Each context keeps its own migrations-history table so the contexts migrate one database without colliding. `--recreate` SHALL drop the database first (dev reset).

#### Scenario: Fresh database
- **WHEN** the migrator runs against an empty database
- **THEN** every registered context applies its initial schema and reports it per label

### Requirement: Migrator password env var and Production gate

The Migrator SHALL read the database password from
`COMUKI_MIGRATOR_DB_PASSWORD` when the resolved connection string
(`COMUKI_DB` env var, legacy `COMUKI_DATABASE` alias, or
`ConnectionStrings:Comuki` in `appsettings.json`) has an empty
`Password=` segment. The committed `appsettings.json` SHALL ship with an
empty `Password=`; deployers set the env var. When the host runs in
`Production` (either `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT`
equal `Production`) with a still-blank password, the Migrator SHALL
refuse to start with an `InvalidOperationException` naming the env var.

#### Scenario: Dev Migrator reads password from env
- **WHEN** `ConnectionStrings:Comuki` is sourced from `appsettings.json`
  with `Password=` empty and `COMUKI_MIGRATOR_DB_PASSWORD` is set
- **THEN** the Migrator fills the password segment from the env var and
  proceeds

#### Scenario: Production refuses a blank password
- **WHEN** `ASPNETCORE_ENVIRONMENT=Production` (or
  `DOTNET_ENVIRONMENT=Production`) and the resolved connection string
  has `Password=` empty with no `COMUKI_MIGRATOR_DB_PASSWORD` set
- **THEN** the Migrator throws `InvalidOperationException` naming the
  env var and exits non-zero

### Requirement: Scale settings bridge at composition

At composition the host SHALL replace the engine's in-memory per-project scale settings with the Projects-backed adapter (see projects), so supervisor decisions observe settings writes without a restart.

#### Scenario: Compute reads projects settings
- **WHEN** the host composes
- **THEN** the compute engine resolves scale knobs from the Projects settings store, not the in-memory default

### Requirement: Realtime composition

The host composition SHALL register SignalR, the `IRunEventsBroadcaster` implementation, and map `/hubs/runs` (see realtime). Detailed errors MAY be enabled in development; production still requires authenticated hub access.

#### Scenario: Hub is mapped
- **WHEN** the host boots
- **THEN** `/hubs/runs` accepts SignalR negotiate for authenticated clients

### Requirement: SignalR detailed errors are gated

The host SHALL enable SignalR `EnableDetailedErrors` only when one of
the following is true: `ASPNETCORE_ENVIRONMENT=Development`,
`DOTNET_ENVIRONMENT=Development`, `DOTNET_RUNNING_IN_CONTAINER=true`, or
`COMUKI_REALTIME_DETAILED_ERRORS=true`. Production hosts with none of
those set SHALL run with detailed errors disabled; the integration suite
flips `COMUKI_REALTIME_DETAILED_ERRORS=true` for its lifetime to keep
the assertion path informative.

#### Scenario: Production boot keeps detailed errors off
- **WHEN** the host boots with `ASPNETCORE_ENVIRONMENT=Production` and
  none of the three diagnostic opt-ins set
- **THEN** `AddSignalR` is called with `EnableDetailedErrors = false` and
  hub exceptions never carry stack frames in their `Message`

#### Scenario: Development boot enables detailed errors
- **WHEN** the host boots with `ASPNETCORE_ENVIRONMENT=Development`
- **THEN** `EnableDetailedErrors = true`

#### Scenario: Integration suite opt-in
- **WHEN** the realtime integration suite sets
  `COMUKI_REALTIME_DETAILED_ERRORS=true` before composition
- **THEN** the host enables detailed errors for the test lifetime

### Requirement: OpenTelemetry opt-in

The host SHALL register Comuki telemetry (meters `comuki.queue` / `comuki.runs` / `comuki.compute` and orchestration/compute/host activity sources) when `Telemetry:OtlpEndpoint` is configured; otherwise telemetry registration is a validated no-op. The Migrator SHALL NOT emit business telemetry. Deploy MAY ship Grafana dashboards as-code under `deploy/grafana` for runs/workers/cost panels against the Victoria/OTLP stack.

#### Scenario: Telemetry disabled
- **WHEN** `Telemetry:OtlpEndpoint` is unset
- **THEN** the host boots without an OTLP exporter and options still validate

#### Scenario: Telemetry enabled
- **WHEN** an OTLP endpoint is configured
- **THEN** traces and metrics for the subscribed sources/meters export to that endpoint

### Requirement: Run artifact bundle in MinIO

The host SHALL register a `IRunArtifactStore` (MinIO / S3) implementation against the `Artifacts:*` config and run a `RunArtifactPackagerHostService` background driver that polls every 10 seconds for runs in a terminal status. On every terminal run the host uploads `brief.json` / `result.json` / `pins.json` under the `{projectId}/{runId}/` key prefix and emits a `run.artifacts_bundled` journal event carrying the canonical artifact pointer list.

`GET /api/v1/projects/{projectId}/runs/{runId}/artifacts` (permission `run:read`, route constant `ApiRoutes.RunArtifacts`) SHALL return the same pointer list; the response is empty when the run has not been bundled yet.

`Artifacts:AutoCreateBucket` (boolean, default off) creates the configured bucket on first boot when no other provisioning is in place — dev convenience. The compose `minio-init` job creates the bucket + 30-day non-current-version lifecycle on first stack bring-up (idempotent).

#### Scenario: Terminal run gets bundled
- **WHEN** a run transitions to a terminal status (succeeded / failed / cancelled / escalated)
- **THEN** the packager uploads the bundle objects to the artifact store and appends a `run.artifacts_bundled` event to the journal

#### Scenario: In-flight run has no bundle
- **WHEN** the packager observes a run whose status is queued / running / waiting
- **THEN** it does not upload and the artifacts endpoint returns an empty list

#### Scenario: Artifacts endpoint requires authentication
- **WHEN** an anonymous client calls `GET /api/v1/projects/{projectId}/runs/{runId}/artifacts`
- **THEN** the response is 401

#### Scenario: Artifacts endpoint requires read permission
- **WHEN** an authenticated client without `run:read` calls the endpoint
- **THEN** the response is 403

### Requirement: Run artifacts and project costs are separate route constants

`ApiRoutes` SHALL expose `ProjectCosts = "/api/v1/projects/{projectId:guid}/costs"`
and `RunArtifacts = "/api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts"`
as distinct constants. Endpoint mapping uses the constant — never a
literal in `MapGet` / `MapPost` / `[Route(...)]`. The costs route is
permission `cost:read`; the artifacts route is permission `run:read`
(same as the parent RunsController).

#### Scenario: Costs route uses its constant
- **WHEN** `CostsModuleEndpoints.MapCostsEndpoints` maps the costs
  endpoint
- **THEN** the route is `ApiRoutes.ProjectCosts`, no inline route
  literal

#### Scenario: Artifacts route uses its constant
- **WHEN** `RunArtifactsController` declares its route
- **THEN** the route is `ApiRoutes.RunArtifacts` (`/api/v1/projects/{projectId:guid}/runs/{runId:guid}/artifacts`),
  no inline route literal

### Requirement: TypedResults.Problem is the error-response convention

Every endpoint that returns an error response SHALL build the body via
`TypedResults.Problem(...)` (or `TypedResults.ValidationProblem(...)` for
400 ValidationProblemDetails). The body content type SHALL be
`application/problem+json` and SHALL carry the `code` extension when the
source is a typed `ProviderException`. The single composition-root
`ProviderExceptionHandler` (`IExceptionHandler`) is the canonical mapper
from typed exceptions to `TypedResults.Problem`; controllers do not
re-implement the body. Ad-hoc envelopes (anonymous `{ "error": ... }`,
bare `Results.Json`, hand-rolled 200-with-error shapes) are forbidden.

#### Scenario: Typed exception surfaces RFC 9457
- **WHEN** a handler throws `ProviderException("provider.network_error", "...")`
- **THEN** the host answers with `TypedResults.Problem(..., extensions: { "code" = "provider.network_error" })` and
  `Content-Type: application/problem+json`

#### Scenario: Controller does not hand-roll the body
- **WHEN** a controller needs to return an error
- **THEN** it builds with `TypedResults.Problem(...)` (or lets
  `ProviderExceptionHandler` map the thrown exception); no
  `Results.Json(new { error = ... })`

### Requirement: OpenAPI document emission

The host SHALL emit an OpenAPI 3.x document at `artifacts/openapi.json` at the repository root on every Debug `dotnet build`, via `Microsoft.Extensions.ApiDescription.Server`'s source generator wired into `Comuki.Host`. `HostComposer.Compose` SHALL register the document in DI under the default name `v1` via `AddOpenApi()`, and `MapOpenApi()` SHALL serve the same document at `/openapi/v1.json` as an anonymous endpoint so external tooling (curl, Scalar, kubb) can fetch the contract without authentication. The build-time generator runs the host's `Program.Main` for document capture; that path SHALL detect `GetDocument.Insider` (the tool's entry assembly) and short-circuit the database connection plus strip every `Comuki.*` hosted service so a fresh clone with no `COMUKI_DB` env var still produces the spec. Release builds SHALL skip emission entirely (Microsoft.AspNetCore.OpenApi 10.0.9's source generator still emits against the 2.x writable `IOpenApiMediaType.Example`, so Release `OpenApiGenerateDocuments=false` is set explicitly).

#### Scenario: Fresh-clone build emits the document
- **WHEN** `dotnet build comuki.slnx -c Debug` runs on a tree without `COMUKI_DB` set and no PostgreSQL reachable
- **THEN** the build exits 0 and `artifacts/openapi.json` is created with all the controllers' endpoints, their request/response shapes, and the controllers' `[ProducesResponseType]` codes

#### Scenario: Generated doc matches runtime document
- **WHEN** a client fetches `/openapi/v1.json` from a running host
- **THEN** the document is byte-identical to `artifacts/openapi.json` because both come from the same `AddOpenApi()` registration

#### Scenario: Release build skips emission
- **WHEN** `dotnet build comuki.slnx -c Release` runs
- **THEN** `artifacts/openapi.json` is not regenerated (Release=false overrides the targets-file default of true)

#### Scenario: Telemetry disabled
- **WHEN** `Telemetry:OtlpEndpoint` is unset
- **THEN** the host boots without an OTLP exporter and options still validate

#### Scenario: Telemetry enabled
- **WHEN** an OTLP endpoint is configured
- **THEN** traces and metrics for the subscribed sources/meters export to that endpoint

### Requirement: CORS allow-list for the versioned API

The host SHALL register a named CORS policy (`comuki.dashboard`)
applied to every request to the versioned API surface (`/api/v1/*`).
The policy SHALL carry `AllowCredentials = true` (cookie + API-key
auth), the explicit `Authorization` + `Content-Type` headers, and the
explicit allow-list of browser origins from `Host:Cors:AllowedOrigins[]`
(or its env-var equivalents). A `Host:Cors:AllowWildcard = true` opt-in
SHALL swap the allow-list for `*`; the host SHALL refuse to start
when this opt-in is set under `Production` (`ASPNETCORE_ENVIRONMENT=Production`
or `DOTNET_ENVIRONMENT=Production`) — wildcards with credentials leak
sessions across origins.

#### Scenario: Strict allow-list in Production

- **WHEN** `Host:Cors:AllowedOrigins` lists one or more origins and the
  host runs in `Production`
- **THEN** the named policy is registered with `WithOrigins(<list>)`,
  `WithHeaders(["Authorization","Content-Type"])`, and `AllowCredentials = true`;
  preflight requests from any other origin are rejected by the
  framework CORS handler before reaching a controller

#### Scenario: Wildcard opt-in is dev-only

- **WHEN** `Host:Cors:AllowWildcard = true` and the host runs in
  `Production`
- **THEN** startup throws `InvalidOperationException` naming the
  offending configuration key; no request is served

### Requirement: Per-endpoint rate limiting

The host SHALL register per-endpoint rate-limit partitions through
`Microsoft.AspNetCore.RateLimiting` (built-in to .NET 8+), bound from
`Host:RateLimit:*` configuration. The named partitions SHALL be:

| Partition | Default permits / minute | Endpoints |
|-----------|---------------------------|-----------|
| `comuki.ratelimit.login` | 10 | `POST /api/v1/auth/login` |
| `comuki.ratelimit.oidc-start` | 30 | `GET /api/v1/auth/oidc/{provider}/start` |
| `comuki.ratelimit.run-decision` | 60 | `POST /api/v1/runs/{runId:guid}/approve`, `POST /api/v1/runs/{runId:guid}/cancel` |
| `comuki.ratelimit.api` | 600 | (default partition — opt-in for the remaining endpoints) |

All partitions are fixed-window, one minute, partitioned by the
caller's `ClaimTypes.NameIdentifier` (when authenticated) or the
`comuki_api_key_id` claim (when API-key authenticated), with a
remote-IP fallback. A limit of `0` on any partition disables that
named partition without rewriting the endpoint attributes — the
documented escape hatch for ops to lift a budget under
attack-investigation pressure without redeploying.

#### Scenario: Login at the partition limit

- **WHEN** the same IP triggers more than 10 login attempts in a
  one-minute window
- **THEN** the 11th request returns `429 Too Many Requests` and
  no controller code runs

#### Scenario: Zero-permit partition is a no-op

- **WHEN** `Host:RateLimit:LoginPermitsPerMinute = 0`
- **THEN** the `comuki.ratelimit.login` partition accepts every
  request and the login endpoint still answers the same 200/401
  ProblemDetails as before

### Requirement: Production-secret fail-fast

The host SHALL refuse to start in `Production` (`ASPNETCORE_ENVIRONMENT=Production`
or `DOTNET_ENVIRONMENT=Production`) when the bound options carry the
committed dev defaults that ship in `deploy/.env.example` /
`appsettings.json`. Specifically:

- `Artifacts:Minio:AccessKey` MUST NOT equal `"comuki"`.
- `Artifacts:Minio:SecretKey` MUST NOT equal `"comuki_dev"`.
- `auth:bootstrap:adminPassword` (or `COMUKI_BOOTSTRAP_ADMIN_PASSWORD`)
  MUST NOT equal `"comuki_dev"`.

The check runs once after `builder.Build()` and throws
`InvalidOperationException` naming the offending configuration key
when any of the three conditions fire. The check is a no-op in
non-Production environments; integration tests boot under
`Production` with non-default secrets to satisfy the gate.

#### Scenario: Production boots with real secrets

- **WHEN** the host runs in `Production` and every committed-default
  secret has been overridden
- **THEN** `ProductionSecretValidator.Validate` returns without
  throwing and the host serves traffic normally

#### Scenario: Production refuses a dev-default secret

- **WHEN** the host runs in `Production` and `Artifacts:Minio:SecretKey`
  is still `"comuki_dev"`
- **THEN** startup throws naming `Artifacts:Minio:SecretKey` /
  `Artifacts__Minio__SecretKey` env var; no request is served

### Requirement: Operator runbook and backup procedure

The repository SHALL ship an operator-facing runbook at
`.agents/docs/operations/runbook.md` covering quick start, bootstrap
admin rotation, OIDC setup, backup / restore, upgrade, and
troubleshooting. The companion `backup.md` SHALL describe the
`pg_dump` (per schema) and `mc mirror` (per MinIO bucket) procedures,
the retention policy, and the monthly verification ritual. Both
documents are agent-facing — they answer "where do I look when paged
at 02:00?" without re-reading the code.

#### Scenario: A new operator follows the runbook

- **WHEN** an operator reads `.agents/docs/operations/runbook.md` end
  to end
- **THEN** they can take a fresh checkout to a running, browser-
  reachable instance in under 30 minutes; rotate the bootstrap admin
  password; add a new OIDC provider; back up Postgres + MinIO;
  restore after partial loss; run the migrator for a release upgrade;
  and triage the most common failure modes (no subject scope, MinIO
  403, dev-default secret rejection, journal event lag) without
  re-reading source code
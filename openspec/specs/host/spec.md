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

### Requirement: OpenTelemetry opt-in

The host SHALL register Comuki telemetry (meters `comuki.queue` / `comuki.runs` / `comuki.compute` and orchestration/compute/host activity sources) when `Telemetry:OtlpEndpoint` is configured; otherwise telemetry registration is a validated no-op. The Migrator SHALL NOT emit business telemetry. Deploy MAY ship Grafana dashboards as-code under `deploy/grafana` for runs/workers/cost panels against the Victoria/OTLP stack.

#### Scenario: Telemetry disabled
- **WHEN** `Telemetry:OtlpEndpoint` is unset
- **THEN** the host boots without an OTLP exporter and options still validate

#### Scenario: Telemetry enabled
- **WHEN** an OTLP endpoint is configured
- **THEN** traces and metrics for the subscribed sources/meters export to that endpoint

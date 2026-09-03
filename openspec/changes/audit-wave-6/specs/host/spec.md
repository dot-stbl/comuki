## ADDED Requirements

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

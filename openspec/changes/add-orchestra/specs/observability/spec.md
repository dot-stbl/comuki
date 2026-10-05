# Observability Specification

## Purpose

Defines how the swarm's own telemetry becomes readable to itself: the `MEL → OTLP` log export leg, the typed VictoriaLogs and VictoriaMetrics clients, the four MCP read tools, the trace-correlation plumbing, and the configuration surface. This is the substrate the Critic-sweep (Phase 4) and the dashboard operator reads for real-time visibility.

Today `ComukiShared.LoggingExtensions` writes to the console and `ComukiTelemetryInstaller.cs` exports tracing + metrics only; VictoriaLogs is up and empty under `deploy/docker-compose.yml`. This capability makes the logs readable through the same MCP catalog the rest of the platform exposes.

## ADDED Requirements

### Requirement: MEL exports logs through OTLP

`ComukiTelemetryInstaller` SHALL register `builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter())` on the existing OTel resource so MEL structured logs flow through the same OTLP endpoint as traces and metrics. The console leg remains the default for Community deployments that have no `OTEL_EXPORTER_OTLP_ENDPOINT` set; the export leg activates only when the endpoint is configured.

#### Scenario: Logs reach VictoriaLogs when the endpoint is set

- **WHEN** the host boots with `OTEL_EXPORTER_OTLP_ENDPOINT=http://victoria:4318`
- **THEN** every MEL log entry is exported to VictoriaLogs within one scrape interval, with the existing structured-logging placeholders preserved

#### Scenario: Console-only without the endpoint

- **WHEN** the host boots without `OTEL_EXPORTER_OTLP_ENDPOINT`
- **THEN** logs continue to write to the console and no exporter is started

### Requirement: Typed Victoria clients live in a module

`Comuki.Modules.Observability` (Domain / Application / Infrastructure) SHALL expose `IVictoriaLogsQueryClient` and `IVictoriaMetricsQueryClient` ports. The clients return typed DTOs, not `JsonElement`; the DTO shape is the typed result the MCP tools and the dashboard observability page render. One concrete implementation each behind the `IVictoriaEndpointResolver` (config + health probe); no protobuf, no OpenTelemetry SDK misuse.

#### Scenario: Logs query returns typed rows

- **WHEN** a caller invokes `IVictoriaLogsQueryClient.SearchAsync(new LogsQuery { Query = "...", Limit = 100 }, ct)`
- **THEN** the result is `IReadOnlyList<LogRow>` with `{ Timestamp, Level, MessageTemplate, ScopeJson, TraceId?, SpanId? }` and never a raw `JsonElement`

#### Scenario: Metrics query returns typed samples

- **WHEN** a caller invokes `IVictoriaMetricsQueryClient.QueryAsync(new MetricsQuery { PromQl = "...", From = ..., To = ... }, ct)`
- **THEN** the result is `IReadOnlyList<MetricSeries>` with `{ Labels, Samples: IReadOnlyList<MetricSample> }`

#### Scenario: Endpoint unreachable

- **WHEN** the configured Victoria endpoint is unreachable for the full timeout
- **THEN** the client throws `VictoriaUnavailableException`; the MCP tool translates to a typed ProblemDetails with `code = observability.victoria_unavailable`

### Requirement: Four MCP read tools land in the existing catalog

`Comuki.Host/Mcp/McpToolCatalog.cs` SHALL register four new tools, each with its own `McpToolPermissionMap` entry and each demanding `observability:read` (a new permission in `Permissions.cs` / `RoleMatrix`):

- `observability.logs.search` — accepts `{ query: string, from?: iso8601, to?: iso8601, limit?: int }`; returns typed log rows.
- `observability.metrics.query` — accepts `{ promql: string, from?: iso8601, to?: iso8601 }`; returns typed metric series.
- `observability.logs.context` — accepts `{ traceId: string }`; returns the matching log rows plus the matching trace spans.
- `observability.metrics.series` — accepts `{ labelSelector: string }`; returns the matching label-set keys.

The four tools reuse the existing `McpWorkerToolGate` infrastructure; no new gate path.

#### Scenario: Authorised MCP read

- **WHEN** a ChatAgent operator with `observability:read` invokes `observability.logs.search` with a LogsQL query
- **THEN** the MCP response carries typed log rows and the request is journaled under the ChatAgent's actor

#### Scenario: Unauthorised MCP read

- **WHEN** the same caller without `observability:read` invokes the tool
- **THEN** the response is `403` with `code = observability.permission_denied`

### Requirement: Trace correlation threads through the typed clients

`IVictoriaLogsQueryClient.SearchAsync` and `IVictoriaMetricsQueryClient.QueryAsync` SHALL read `Activity.Current?.TraceId` and thread it as the default filter when the caller does not specify `TraceId`. The OTel `ActivitySource` registered in `ComukiTelemetryInstaller` emits trace ids in W3C format; the typed client formats them in the same format on the wire. No new telemetry exporter.

#### Scenario: Trace correlation by default

- **WHEN** an MCP tool call happens inside an OTel activity and the caller does not specify a `TraceId`
- **THEN** the underlying VictoriaLogs query includes the `trace_id` filter and returns only the matching log rows

#### Scenario: Explicit trace id override

- **WHEN** the caller explicitly passes `traceId`
- **THEN** the typed client uses the explicit value and ignores `Activity.Current?.TraceId`

### Requirement: Retention and scrape interval are configurable

`config.toml` SHALL expose `Observability:Victoria:RetentionPeriod` (no code-level default — the deployed Victoria stack ships with `--retentionPeriod=1` per the platform baseline; an explicit retention setting is a deployment-level concern, not a code-level one) and `Observability:Victoria:ScrapeInterval` (default 15s, range 5s–5m). Both bind via `ValidateDataAnnotations().ValidateOnStart()`. The Victoria stack under `deploy/docker-compose.yml` reads the retention from the same TOML path; the change ships the values, not a separate compose file.

#### Scenario: Custom retention period

- **WHEN** the host boots with `Observability:Victoria:RetentionPeriod = "30d"`
- **THEN** the typed client emits a `retention_period=30d` flag on its bootstrap handshake and the Victoria stack applies it

#### Scenario: Out-of-range scrape interval

- **WHEN** the host boots with `Observability:Victoria:ScrapeInterval = "1s"` (below the minimum)
- **THEN** `ValidateOnStart()` fails the boot with a typed error naming the offending field

#### Scenario: Retention not set

- **WHEN** the host boots without `Observability:Victoria:RetentionPeriod` set
- **THEN** the Victoria container ships with the platform baseline (`--retentionPeriod=1`); an explicit retention value is a deployment-level concern and the code does not assume one

## ADAPTER Notes

`ComukiTelemetryInstaller.cs` is the single composition entry for OTel. This capability adds the log-export leg on the existing resource — no new resource, no new SDK. The typed clients are scoped to `Comuki.Modules.Observability` and the existing module-installer pattern; the host composes the module through `AddObservabilityModule()`.

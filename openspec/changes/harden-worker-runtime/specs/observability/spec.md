## MODIFIED Requirements

### Requirement: Worker telemetry sidecar on the existing OTel resource

The Translator SHALL emit worker-side metrics on the same OTel resource as the host (`ComukiTelemetryInstaller`, master after `add-orchestra` phase 2). One OTLP exporter, one resource — no second pipeline. The worker-side `ActivitySource = "Comuki.Translator.Runtime"` and `Meter = "comuki.worker"` are added on the **existing** `OpenTelemetryBuilder` chained to the host-side resource. The Translator's MEL log SHALL flow through the same `AddOtlpExporter` call (`builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter())` on the existing OTel resource).

The nine metrics published by the Translator (per `worker-runtime` capability delta "Worker telemetry emits nine counters through the host OTLP endpoint"):

| Metric | Type | Tags |
|---|---|---|
| `comuki.worker.last_event_age_ms` | gauge | `worker_id` |
| `comuki.worker.events_total` | counter | `worker_id`, `type` |
| `comuki.worker.parse_errors_total` | counter | `worker_id`, `kind` |
| `comuki.worker.stdin_commands_total` | counter | `worker_id`, `cmd` |
| `comuki.worker.heartbeat_outcome` | counter | `worker_id`, `status` |
| `comuki.worker.events_per_sec` | histogram | `worker_id` |
| `comuki.worker.skew_detected_total` | counter | `expected_version` |
| `comuki.worker.unknown_command_total` | counter | `cmd` |
| `comuki.worker.events_dropped_total` | counter | `kind` |

Tags follow `observability/diagnostics.md` §4 (dot.case, bounded cardinality, no PII).

#### Scenario: One exporter, one resource

- **WHEN** the host boots with `OTEL_EXPORTER_OTLP_ENDPOINT=http://victoria:4318`
- **THEN** both host-side and worker-side metrics are exported through the same exporter; `service.name` is the host's resource (no second Worker resource), `comuki.worker.*` and `comuki.*` metrics carry the same `service.name` and only differ by metric name

#### Scenario: Translator MEL log flows through the same exporter

- **WHEN** the Translator emits a MEL log line
- **THEN** the line reaches VictoriaLogs through the OTLP exporter on the same resource as host logs (the host-side MEL→OTLP exporter added in `add-orchestra` phase 2 is the same exporter)

#### Scenario: Tag cardinality is bounded

- **WHEN** a worker-side metric emits with a tag
- **THEN** the tag value comes from the closed set (`worker_id`, `type`, `kind`, `cmd`, `status`, `expected_version`); no unbounded tags per `observability/diagnostics.md` §4

## ADAPTER Notes

The host-side `ComukiTelemetryInstaller` (master after `add-orchestra` phase 2 — MEL→OTLP log export leg) is extended on the **existing** `OpenTelemetryBuilder` to add a worker-side `Meter = "comuki.worker"` and `ActivitySource = "Comuki.Translator.Runtime"`. No new resource, no new exporter, no new SDK — only new meters/sources on the existing resource. The metric names are `comuki.worker.*` per `observability/diagnostics.md` §3 (`{app}.{noun}.{quantity}` lowercase dot.case).

The four MCP read tools (`observability.logs.search`, `observability.metrics.query`, `observability.logs.context`, `observability.metrics.series`) added in `add-orchestra` §2.3 already read both host-side and worker-side metrics through the typed Victoria clients — no MCP delta is required here. Querying `observability.metrics.series{labelSelector = "metric_name=~comuki\\.worker.*"}` returns the worker-side metrics; the typed DTO contract is unchanged.
# Observability — ActivitySource / Meter / OTLP

> The Comuki service's own operational telemetry. **Not** the
> telemetry the dashboard displays from a backend — that's a separate
> concern (the FE consumes traces from another service; this doc
> covers what the **Comuki host / engine / modules** emit themselves).
>
> Source of truth: `platform/src/shared/Comuki.Shared.Telemetry/`
> (`ComukiInstrumentation.cs`, `ComukiTelemetry.cs`, and the
> `Installers.ComukiTelemetryInstaller` wired from `HostComposer`).

## 1. Wiring

The host calls `AddComukiTelemetry(builder.Configuration)` first in
`HostComposer.Compose` (see [`host-internals.md`](./host-internals.md)
§1, phase 0). The installer:

1. Registers ASP.NET Core, HttpClient and EF Core instrumentation
   (auto-spans for every HTTP request, every outbound HttpClient call,
   every EF Core query).
2. Sets the `OTEL_*` env vars from configuration when
   `Telemetry:OtlpEndpoint` is set; the OTLP exporter ships spans +
   metrics to VictoriaMetrics OTLP ingest (`:8431` by default).
3. Subscribes the three `Meter` instances declared in
   `ComukiTelemetry.cs` so the runtime keeps their instruments alive.
4. Adds `Runtime` + `Process` instrumentation (GC pauses, CPU,
   memory, open FDs) — free with the auto-instrumentation set.

The exporter is **OTLP only**. There is no separate Prometheus exporter
in v1; VictoriaMetrics scrapes via OTLP or you put a sidecar in front
of `/metrics` on the host.

## 2. Activity sources

One `ActivitySource` per emitting assembly. The source name is the
**assembly name** (not a generic "Comuki") — that's how Victoria
filters traces by component.

| Source | Emitted by |
|--------|-----------|
| `Comuki.Engine.Orchestration` | Claim / apply-plan / lease-reaper paths in the engine. |
| `Comuki.Engine.Compute` | Worker start / stop, scale-supervisor cycle. |
| `Comuki.Host` | HTTP request handler spans (auto), realtime hub, OpenAPI, proxy. |

Span names (`ComukiInstrumentation.*`):

- `comuki.queue.claim` — the `ClaimAsync` path on the work queue
  (the pull-model primitive). Tagged `outcome: hit | empty`.
- `comuki.runs.apply_plan` — the brain's plan → work items
  translation. Tagged `project`.
- `comuki.compute.worker.start` — `Compute.Start` (`DockerComputeProvider`,
  `KubernetesComputeProvider`). Tagged `provider`, `profile`.
- `comuki.compute.worker.stop` — symmetric to start.

The four `comuki.*` span names are stable; new spans get new constants
in `ComukiInstrumentation`. Adding a one-off span inline is a
review-reject — register the name first.

## 3. Meters

Three `Meter` instances, one per domain. The instrument names are also
in `ComukiInstrumentation`.

### `comuki.queue`

| Instrument | Type | Unit | Tags |
|------------|------|------|------|
| `comuki.queue.claimed` | `Counter<long>` | — | `outcome` |
| `comuki.queue.claim.duration` | `Histogram<double>` | `ms` | `outcome` |

Use: claim rate, queue-empty rate, claim latency distribution. The
"claim hit ratio" (hits / total claims) is the headline health number
for the worker pool.

### `comuki.runs`

| Instrument | Type | Unit | Tags |
|------------|------|------|------|
| `comuki.runs.started` | `Counter<long>` | — | — |
| `comuki.runs.work_items.queued` | `Counter<long>` | — | `profile` |

Use: throughput (runs/sec) and granularity of DAG work. Pair with
the `runs.run_events` row count for fan-out depth.

### `comuki.compute`

| Instrument | Type | Unit | Tags |
|------------|------|------|------|
| `comuki.compute.workers.started` | `Counter<long>` | — | `provider` |
| `comuki.compute.workers.stopped` | `Counter<long>` | — | `provider`, `reason` |
| `comuki.compute.worker.start.duration` | `Histogram<double>` | `ms` | `provider` |

Use: provider-by-provider worker churn (Docker vs k8s), start
latency (cold-image pull vs warm container), and stop reason
breakdown (`reason: idle_ttl | lease_expired | user_stop |
budget_exceeded | …`).

## 4. Span tags — bounded cardinality only

Tag keys live in `ComukiInstrumentation.cs` and **never** carry ids
or free text. Concretely:

| Tag | Allowed values |
|-----|----------------|
| `outcome` | `hit` · `empty` (queue claim) |
| `provider` | `docker` · `kubernetes` |
| `profile` | The catalog profile key — `sentry`, `explore-readonly`, `implement`, … (closed set per `control-plane/profiles/`). |
| `reason` | `idle_ttl` · `lease_expired` · `user_stop` · `budget_exceeded` · `failure` · `cancelled` |

**Anti-patterns** (review-reject):

- `run.id`, `project.id`, `user.id` — unbounded cardinality; the
  collector will OOM as soon as a tenant starts a few thousand runs.
- `error.message` / `error.stack` — use the OTel `Exception` /
  `RecordException` API; the SDK formats it once and the collector
  keeps cardinality bounded.
- A `path` tag with raw URL — same issue; if you need request-path
  data, drop it into the trace as an attribute (low-cardinality
  filter at the collector side).

## 5. Metric naming

Per `~/.agents/rules/observability/diagnostics.md`:

- `comuki.<domain>.<noun>.<verb>` — every segment separated by a
  single dot, all lowercase.
- Unit goes on the **instrument** declaration (`unit: "ms"`),
  never baked into the name (`comuki.queue.claim.duration_ms` is
  banned — the unit is in the histogram's `Unit` property).
- Prometheus-style suffixes (`_total`, `_bytes`, `_seconds`) only
  on Prometheus-exposed counters, never on OTel instruments through
  OTLP.

## 6. End-to-end pipeline

```
Comuki.Engine.Orchestration
   │  StartActivity("comuki.queue.claim", tags: { outcome: "hit" })
   │  RecordDuration("comuki.queue.claim.duration", ms)
   ▼
ComukiTelemetry (singleton Meter / ActivitySource)
   │  OTLP exporter (Telemetry:OtlpEndpoint → VictoriaMetrics :8431)
   ▼
VictoriaMetrics
   │  vmagent / VictoriaLogs (logs via MEL → OTel → Victoria)
   ▼
deploy/grafana/dashboards/comuki-{runs,workers,cost}.json
   │  panels: claim hit ratio, p95 claim latency, runs/sec,
   │  provider-by-provider worker churn, etc.
   ▼
Operator on-call (Grafana → Slack/PagerDuty)
```

The `deploy/grafana/` directory ships as-code dashboards
(`comuki-runs.json`, `comuki-workers.json`, `comuki-cost.json`)
provisioned by the compose deploy.

## 7. Logs

Logs come through `Microsoft.Extensions.Logging` → OTel `ILogger`
bridge → VictoriaLogs OTLP. Structured logging per
`~/.agents/rules/csharp/logging.md`:

- No string interpolation: `"Claim {ClaimId} succeeded in {Ms}ms"`,
  not `$"Claim {id} succeeded in {ms}ms"`.
- Placeholders are PascalCase (`{ClaimId}`), values flow through
  `ILogger`'s parameter dictionary.
- `Exception` is the second argument to `LogError`, never interpolated
  into the message.

## 8. Where the auto-instrumentation ends

| Surface | Auto? | Why / why not |
|---------|------|---------------|
| Inbound HTTP requests | Yes | ASP.NET Core source-gen. |
| Outbound `HttpClient` | Yes | `SocketsHttpHandler` source-gen. |
| EF Core queries | Yes | EF Core source-gen. |
| gRPC client / server | Yes | `Grpc.Net.Client` / `Grpc.AspNetCore.Server` instrumentation packages. |
| Background services | Partial | The hosted service itself is wrapped, but the polling body is invisible — add a manual span if you want to see one cycle. |
| Domain logic | No | Manual spans only. Don't pollute domain code with telemetry — go through a host-side adapter. |
| Realtime / SignalR | Partial | The hub handshake is wrapped; the broadcast interceptor is not. Add a span at the journal flush if you need per-event visibility. |

## 9. Alert policy (operator-side, not platform-owned)

The platform emits metrics; the **operator** wires the alert rules.
Suggested baselines (in `deploy/grafana/` once shipped):

- `comuki.queue.claim.duration` p95 > 500 ms — Postgres hot.
- `comuki.runs.started` rate drops to 0 for 5+ min — scheduler stuck
  or worker pool exhausted.
- `comuki.compute.workers.stopped{reason="lease_expired"}` rate
  spikes — heartbeat regression or worker-side slowness.
- `comuki.runs.work_items.queued` rate / `comuki.runs.started` rate
  ratio > 5x for 10+ min — planner is over-fanning.

The platform doesn't auto-page; it surfaces.

## 10. Source pointers

- `platform/src/shared/Comuki.Shared.Telemetry/ComukiInstrumentation.cs`
  — every stable name (meters, activity sources, span names, tag
  keys, outcome values). Add to this file when introducing a new
  instrument; never inline.
- `platform/src/shared/Comuki.Shared.Telemetry/ComukiTelemetry.cs`
  — the singleton `Meter` / `ActivitySource` / instrument instances.
- `platform/src/shared/Comuki.Shared.Telemetry/Installers/ComukiTelemetryInstaller.cs`
  — the OTel SDK wiring (`AddOpenTelemetry().WithMetrics(...)`, etc.).
- `platform/src/host/Comuki.Host/HostComposer.cs` §0 — the
  `AddComukiTelemetry(builder.Configuration)` call.
- `~/.agents/rules/observability/diagnostics.md` — the global
  observability rules.
- `deploy/grafana/dashboards/` — the as-code dashboards that
  consume these metrics.

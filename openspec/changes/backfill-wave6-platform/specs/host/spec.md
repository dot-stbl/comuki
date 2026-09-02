## ADDED Requirements

### Requirement: OpenTelemetry opt-in
The host SHALL register Comuki telemetry (meters `comuki.queue` /
`comuki.runs` / `comuki.compute` and orchestration/compute/host activity
sources) when `Telemetry:OtlpEndpoint` is configured; otherwise telemetry
registration is a validated no-op. The Migrator SHALL NOT emit business
telemetry. Deploy MAY ship Grafana dashboards as-code under `deploy/grafana`
for runs/workers/cost panels against the Victoria/OTLP stack.

#### Scenario: Telemetry disabled
- **WHEN** `Telemetry:OtlpEndpoint` is unset
- **THEN** the host boots without an OTLP exporter and options still validate

#### Scenario: Telemetry enabled
- **WHEN** an OTLP endpoint is configured
- **THEN** traces and metrics for the subscribed sources/meters export to that
  endpoint

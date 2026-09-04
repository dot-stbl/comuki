# Costs Specification

## Purpose

Defines usage-event recording, soft/hard project budgets in USD micros, the
hard-stop gate that cancels attributed runs, and the project costs read API.

## Requirements

### Requirement: Usage events
The platform SHALL append usage events to `usage_events` with project id,
optional run id, source, model, input/output tokens, cost in USD micros
(1 USD = 1_000_000), and occurred_at. Indexes SHALL support project+occurred
and run_id lookups. Events are append-only.

#### Scenario: Record usage
- **WHEN** a caller records a usage event for a project
- **THEN** a row is persisted and the project spend sum includes its cost

#### Scenario: Sum proxy-source usage for one project in a window
- **WHEN** a caller sums proxy-source cost for a project since a given instant
- **THEN** the returned micros equal the sum of `cost_usd_micros` over rows where `source = 'proxy'` and `occurred_at >= since`

### Requirement: Proxy source
The proxy module (issue #8 / S9 T9.6) SHALL meter metered LLM calls
through `usage_events` with `source = 'proxy'` and the `project_id` the
virtual key is bound to. Cost rows is derived from the configured
per-model price tier (USD per million tokens) and recorded in
`cost_usd_micros`.

#### Scenario: Proxy records usage on metered response
- **WHEN** the proxy successfully forwards a `/v1/chat/completions` or `/v1/messages` call
- **THEN** a `usage_events` row is appended with `source = 'proxy'` and the project attribution the virtual key carries

### Requirement: Soft and hard budgets
Project budget caps SHALL be read through `IProjectBudgetSettings` (backed by
project settings). Null soft/hard limits mean unlimited. Soft exceedance SHALL
be advisory (structured warning log). Hard exceedance SHALL invoke the host
budget gate when a run id is attributed.

#### Scenario: Soft exceedance
- **WHEN** project spend reaches or exceeds the soft limit
- **THEN** a warning is logged and the run is not cancelled by the soft path

#### Scenario: Hard exceedance with run
- **WHEN** spend reaches or exceeds the hard limit and the event carries a run id
- **THEN** the budget gate hard-stops that run

### Requirement: Budget gate hard-stop
The host budget gate SHALL cancel the attributed run and journal
`budget.exceeded` with spent/hard limit micros and project id. The operation
SHALL be idempotent for repeated exceedance recordings on the same run.

#### Scenario: Journal budget exceeded
- **WHEN** the gate hard-stops a run
- **THEN** a `budget.exceeded` journal entry is appended with the spend figures

### Requirement: Project costs API
`GET /api/v1/projects/{projectId}/costs` SHALL return spent micros, soft/hard
limits, soft/hard exceeded flags, and a recent usage feed. Money fields use USD
micros. (Permission `cost:read` is the intended demand; wiring on the
minimal-API route may still be pending — document actual host behavior.)

#### Scenario: Costs summary
- **WHEN** costs are requested for a project with usage
- **THEN** the response includes spend, caps, exceeded flags, and recent events

### Requirement: Persistence layout
Costs SHALL use module-private migrations history `__comuki_costs` for
`usage_events`.

#### Scenario: Migrator applies costs schema
- **WHEN** the migrator runs
- **THEN** `usage_events` exists without colliding with other module histories

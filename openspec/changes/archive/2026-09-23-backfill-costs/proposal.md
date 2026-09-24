## Why

Costs/budgets (S9) landed: `usage_events`, soft/hard USD-micros caps on
project settings, hard-stop gate, project costs API. Specs omit the capability
and projects settings still omit budget fields.

## What Changes

- Add capability `costs`.
- Extend `projects` settings shape with soft/hard budget micros.
- Note `budget.exceeded` journal type on `runs`.

## Capabilities

### New Capabilities
- `costs`: usage recording, budgets, project costs read API

### Modified Capabilities
- `projects`: settings include soft/hard budget USD micros
- `runs`: journal type `budget.exceeded`

## Impact

Docs only. Code in `Modules.Costs`, `Host/Costs`, Projects settings columns.

## Non-goals

- Hardening `cost:read` permission attribute on the minimal-API costs route
  (TODO still in host)
- Per-model pricing tables

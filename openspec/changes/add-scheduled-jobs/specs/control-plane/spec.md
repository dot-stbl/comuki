## ADDED Requirements

### Requirement: ops-sentry profile

The default control-plane pack SHALL include profile `ops-sentry`. It
SHALL be read-only: allowed tools are inspection only (Read, Grep, Glob,
Bash restricted to non-mutating diagnostics). Advisory model SHALL be
`light` (cheap working model). The prompt SHALL instruct the worker to
answer the brief's checks with evidence, emit an explicit verdict
(`ok` / `fail` / `error`) and never change infrastructure or git state.

A scheduled job MAY pin any catalogued profile; `ops-sentry` is the
recommended default, not a restriction. The catalog API SHALL list
`ops-sentry` like any other profile (metadata only, no prompt body).

#### Scenario: Catalog lists ops-sentry

- **WHEN** a client lists profiles from the default pack
- **THEN** an entry with key `ops-sentry` is present with a non-empty
  description and a non-empty allowed-tools list

#### Scenario: Sentry must not write

- **WHEN** an ops-sentry worker is given a brief that would require
  editing files or applying cluster changes
- **THEN** those tools are not in the allow-list

## ADDED Requirements

### Requirement: Scheduled job dispatcher hosted service

The host SHALL register a scheduled-job dispatcher as a hosted service,
wired only in `HostComposer`. The dispatcher SHALL run as a named system
consumer (`AsSystem("schedule-dispatcher")`) so project query filters do
not hide due rows. Interval default 15 seconds, overridable via
`Host:Schedule:{Enabled,Interval}`. `Enabled=false` SHALL make
`ExecuteAsync` return immediately (OpenAPI build-time strip still
removes the hosted service). A failed pass SHALL be logged and retried
on the next tick — it SHALL NOT stop the host.

Composition SHALL bind a host-side run launcher for scheduled jobs
(mirroring intake): the ScheduledJobs module SHALL NOT reference the
orchestration engine.

#### Scenario: Dispatcher disabled at boot

- **WHEN** `Host:Schedule:Enabled` is false
- **THEN** the dispatcher does not poll and no cron runs are launched

#### Scenario: Dispatcher failure is contained

- **WHEN** one poll throws
- **THEN** the host stays up and the next interval runs another pass

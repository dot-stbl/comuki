# Realtime Specification

## Purpose

Defines the SignalR realtime surface for run timelines and project attention:
authenticated hub joins with RBAC checks, journal-driven broadcast, and
attention-worthy transition mapping.

## Requirements

### Requirement: Runs hub route and auth
The host SHALL map SignalR hub `RunsHub` at `/hubs/runs`. The hub SHALL require
an authenticated connection (`[Authorize]`); anonymous handshakes fail. Hub
methods do not use the MVC permission filter — each join resolves the RBAC
subject from the connection principal and evaluates permissions itself.

#### Scenario: Anonymous handshake
- **WHEN** a client connects to `/hubs/runs` without credentials
- **THEN** the handshake fails authorization

### Requirement: Join run timeline
`JoinRunAsync(runId)` SHALL require `run:read` on the run's project. An unknown
run SHALL raise a hub error `run.not_found`. On success the connection joins
group `run:{id}`. `LeaveRunAsync` SHALL always be allowed.

#### Scenario: Join without permission
- **WHEN** an authenticated subject without `run:read` on the run's project
  calls JoinRunAsync
- **THEN** the hub raises `permission.denied` and the group is not joined

#### Scenario: Unknown run
- **WHEN** JoinRunAsync is called for a run id that does not exist (or is
  out of subject scope)
- **THEN** the hub raises `run.not_found`

### Requirement: Join project attention
`JoinProjectAsync(projectId)` SHALL require `project:read` on that project and
join group `project:{id}:attention`. No project existence check is required —
joining an unknown project at worst subscribes to a silent group.
`LeaveProjectAsync` SHALL always be allowed.

#### Scenario: Attention join
- **WHEN** a subject with `project:read` joins a project
- **THEN** the connection is added to `project:{id}:attention`

### Requirement: Journal broadcast
After journal entries are committed, a broadcaster SHALL send each entry to its
`run:{id}` group as client method `RunEvent`. Attention-worthy entries SHALL
additionally send client method `Attention` to the owning project's attention
group. Unresolvable run→project lookups skip the attention signal without
failing the run-group broadcast.

#### Scenario: Timeline event
- **WHEN** a `work_item.status_changed` journal row is committed
- **THEN** connected members of `run:{id}` receive `RunEvent`

### Requirement: Attention-worthy transitions
Attention SHALL be derived from journal types `work_item.status_changed`,
`work_item.lease_expired`, and `run.status_changed`. Work-item `Running` →
attention kind `running`; `Failed` → `failed`. Requeue to `Queued` SHALL NOT
emit attention. Run-level escalated / awaiting-approval mappings MAY be
included for forward compatibility.

#### Scenario: Requeue is not attention
- **WHEN** a lease expiry requeues an item to `Queued`
- **THEN** no Attention message is sent for that transition

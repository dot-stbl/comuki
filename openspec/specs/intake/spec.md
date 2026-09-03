# Intake Specification

## Purpose

Defines tracker ingress into Comuki: anonymous signed webhooks, source
connections, admission rules (watch vs inbox), the human inbox/claim loop,
native ticket creation, and sync-back jobs that push status to trackers.

## Requirements

### Requirement: Anonymous webhook ingress
`POST /api/hooks/{provider}/{key}` SHALL be anonymous. The per-connection
routing key SHALL resolve the source connection before signature verification;
a verified tracker signature IS authentication. Unknown provider/connection
answers 404; bad signature answers 401. Deliberately ignored deliveries
(replay, skip, filtered, duplicate) SHALL answer 200 with an outcome label so
trackers do not retry intentional drops.

#### Scenario: Bad signature
- **WHEN** a delivery fails signature verification
- **THEN** the answer is 401 ProblemDetails and no ticket is admitted

#### Scenario: Duplicate delivery
- **WHEN** a delivery id was already recorded
- **THEN** the answer is 200 with outcome `duplicate`

### Requirement: Supported providers
Intake SHALL support kebab-case provider keys `github`, `gitlab`, `jira`, and
`yandex-tracker`, each with payload mapping, signature verification, and
sync-back client. Unsupported provider names answer 404.

#### Scenario: Unknown provider
- **WHEN** a webhook posts to `/api/hooks/bitbucket/{key}`
- **THEN** the answer is 404

### Requirement: Source connections
Source connections SHALL persist under `source_connections` with settings
,
env-ref secrets (never inline secrets), and a generated webhook routing key.
REST under `/api/v1/sources` SHALL demand `intake:read` for reads and
`source:write` for create/update/delete.

#### Scenario: Create source
- **WHEN** an operator with `source:write` creates a connection
- **THEN** the response is 201 including the webhook routing key once

### Requirement: Pull-request / merge-request ingress (issue #27)
The GitHub and GitLab webhook mappers SHALL admit pull-request /
merge-request events alongside issue events. Admitted events are
`opened`, `ready_for_review`, and `reopened`; `synchronize` / `closed`
/ etc. stay skipped for v1. Admitted PRs SHALL be stamped as
`PullRequest`-kind tickets on the same `IncomingTicket` shape (the
external id keeps `owner/repo#N`; the URL is the PR html_url).

#### Scenario: GitHub pull_request.opened admission
- **WHEN** a signed `pull_request` webhook with action `opened`
  arrives
- **THEN** the mapper admits a `PullRequest`-kind ticket and the
  webhook pipeline behaves like an issue admission (delivery lock,
  signature verify, normalize, admission, ticket insert, run launch)

#### Scenario: GitLab merge_request open admission
- **WHEN** a signed `merge_request` webhook with action `open`
  arrives
- **THEN** the mapper admits a `PullRequest`-kind ticket

#### Scenario: Skipped PR action
- **WHEN** a `synchronize` GitHub or GitLab merge-request webhook
  arrives
- **THEN** the answer is 200 with outcome `skipped` (not a review
  trigger in v1)

### Requirement: Catalog fetch can opt into pull requests
Source connections SHALL admit pull requests into the inbox catalog
only when the connection opts in via settings — never mixed into the
issues inbox by default. GitHub reads `includePullRequests` from the
settings jsonb (false by default); GitLab reads `includeMergeRequests`
(false by default) and additionally calls the merge-request catalog
endpoint.

#### Scenario: Default GitHub catalog stays issue-only
- **WHEN** a GitHub connection's settings omit `includePullRequests`
- **THEN** the catalog fetch returns only issues (PRs filtered out)

#### Scenario: GitHub connection opts into PR catalog
- **WHEN** a GitHub connection's settings declare `includePullRequests: true`
- **THEN** the catalog fetch returns both issues and PRs, with PR
  entries stamped `PullRequest`-kind

### Requirement: Profile routing (issue #27)
Admitted tickets SHALL land on a profile chosen by
`IIntakeProfileRouter`. The router SHALL honor an explicit
`profileKey` from the connection's settings jsonb when present; other-
wise default to `pr-review` for `PullRequest`-kind tickets and to
`Intake:Worker:IssueDefaultProfileKey` (default `general`) for
`Issue`-kind tickets. Admitted PR tickets SHALL NOT claim on the
`implement` profile.

#### Scenario: Inbound PR webhook claims on pr-review
- **WHEN** a watch-mode rule admits a `PullRequest`-kind ticket
- **THEN** the queued work item carries `ProfileKey = "pr-review"`

#### Scenario: Per-connection profileKey override wins
- **WHEN** a connection's settings declare `"profileKey": "explore-readonly"`
- **THEN** the router returns `explore-readonly` regardless of ticket kind

### Requirement: Sync-back for PRs is a single issue-comment only
On a terminal run transition the GitHub / GitLab sync port SHALL post
exactly one status comment (carrying the run's verdict) to the
tracker's comment thread. For pull-requests and merge-requests the
sync port SHALL NOT close the tracker ticket on success — a Comuki
review is a comment, not a merge decision. Inline review comments are
out of scope for v1.

#### Scenario: GitHub PR sync-back on Succeeded
- **WHEN** a `PullRequest`-kind GitHub ticket's run reaches
  `Succeeded`
- **THEN** the GitHub sync port posts the run-link comment and does
  not call the issue-state patch

#### Scenario: GitLab MR sync-back on Succeeded
- **WHEN** a `PullRequest`-kind GitLab ticket's run reaches
  `Succeeded`
- **THEN** the GitLab sync port posts the run-link note and does not
  call the issue-state update

### Requirement: Admission rules
Per-project admission rules SHALL live in `admission_rules` with mode
(watch → auto-admit vs inbox → pending) and a filter expression. Rules CRUD
under `/api/v1/admission-rules` SHALL demand `intake:read` / `source:write`.

#### Scenario: Inbox mode holds ticket
- **WHEN** a matching rule is inbox-mode and a webhook arrives
- **THEN** the ticket is stored pending and not launched as a run

### Requirement: Inbox and claim
`GET /api/v1/inbox` and `GET /api/v1/inbox/catalog` SHALL list pending tickets
(`intake:read`). `POST /api/v1/inbox/claim` SHALL require `intake:claim` and
launch a run through the host run-runlauncher port for the claimed ticket.

#### Scenario: Claim launches run
- **WHEN** an operator claims a pending ticket
- **THEN** a run is created for the project and the ticket records the active
  run id

### Requirement: Native tickets
`POST /api/v1/tickets` SHALL create a native (non-tracker) ticket and demand
`run:create`, returning 201 with the ticket view.

#### Scenario: Native ticket without webhook
- **WHEN** a caller creates a native ticket with project and title
- **THEN** the ticket is stored without a source connection and can enter
  admission/claim flows

### Requirement: Sync-back outbox
Status transitions destined for trackers SHALL enqueue `sync_jobs` rows. A
background bridge SHALL push transitions through the provider sync port and
update job status. Failures remain retryable in the outbox.

#### Scenario: Sync job recorded
- **WHEN** an admitted ticket's run reaches a terminal status that maps to a
  tracker transition
- **THEN** a sync job row exists for the provider push

### Requirement: Persistence layout
Intake tables (`intake_tickets`, `intake_deliveries`, `source_connections`,
`admission_rules`, `sync_jobs`) SHALL use module-private migrations history
`__comuki_intake`. Deliveries SHALL insert-first for idempotency.

#### Scenario: Replay uses delivery lock
- **WHEN** the same delivery id is inserted twice
- **THEN** the unique delivery constraint makes the second insert a no-op path
  answering 200 `duplicate`

### Requirement: Dashboard filter for the PR-review profile
The operational dashboard (Inbox, Runs) SHALL filter tickets and runs by
worker profile — `profile == "pr-review"` is the visible surface for
inbound PR review activity. No new top-level dashboard app, no
`Modules.PullRequests` backend module, no `/api/v1/prs` route —
the existing run-detail screen is sufficient.
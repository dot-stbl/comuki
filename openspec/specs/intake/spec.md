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

### Requirement: Inbound ticket kind discriminator
Admitted tickets SHALL carry an `InboundTicketKind` discriminator with
exactly two members: `Issue` (the default) and `PullRequest` (the inbound
review surface for GitHub pull requests and GitLab merge requests). The
discriminator SHALL be stamped at normalize time by the provider mapper
and SHALL be the input the profile router reads to choose the worker
profile. The persisted column SHALL be named `kind` and SHALL be required
on every admitted ticket; pre-existing rows SHALL be backfilled to
`Issue`.

#### Scenario: GitHub pull request stamps PullRequest
- **WHEN** a GitHub `pull_request.opened` webhook admits a ticket
- **THEN** `kind = PullRequest` is stamped at normalize time and the
  ticket row carries the discriminator

#### Scenario: Plain issue stays Issue
- **WHEN** a GitHub `issues.opened` or any other tracker issue event
  admits a ticket
- **THEN** `kind = Issue` and the value never changes for the ticket's
  lifetime

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
`IIntakeProfileRouter`. The intake module SHALL expose
`IIntakeProfileRouter` as a port that maps an admitted ticket to a
worker profile key; the host composes the implementation. The default
implementation SHALL honor an explicit `profileKey` from the
connection's settings jsonb when present, SHALL fall back to
`pr-review` for `PullRequest`-kind tickets, and SHALL fall back to
`Intake:Worker:IssueDefaultProfileKey` (default `general`) for
`Issue`-kind tickets. The router SHALL never throw — broken JSON,
missing fields, and non-string values silently use the fallback.
Admitted PR tickets SHALL NOT claim on the `implement` profile. The
intake module SHALL NOT depend on the engine — the host composes the
implementation.

#### Scenario: Inbound PR webhook claims on pr-review
- **WHEN** a watch-mode rule admits a `PullRequest`-kind ticket
- **THEN** the queued work item carries `ProfileKey = "pr-review"`

#### Scenario: Per-connection profileKey override wins
- **WHEN** a connection's settings declare `"profileKey": "explore-readonly"`
- **THEN** the router returns `explore-readonly` regardless of ticket kind

#### Scenario: Issue kind uses the configured default
- **WHEN** an admitted `Issue`-kind ticket has no per-connection
  override
- **THEN** the router returns `Intake:Worker:IssueDefaultProfileKey`
  (default `general`)

#### Scenario: Broken settings json falls back silently
- **WHEN** the connection's settings jsonb is malformed or the
  `profileKey` field is a non-string
- **THEN** the router returns the kind-based default and does not throw

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

#### Scenario: Inbox filter scoped to pr-review
- **WHEN** an operator opens the dashboard Inbox with
  `profile = "pr-review"`
- **THEN** the inbox lists only tickets whose work-item profile is
  `pr-review` and runs are filterable on the same axis

### Requirement: Source connection schema (issues #38, #39)

A `source_connections` row SHALL carry the provider name, the project
it feeds, a free-form provider-specific `settings_json` (NEVER
secrets — only env-var names and non-sensitive options), the env-var
name `secret_env_ref` that holds the webhook verification secret, and
a generated unguessable `webhook_key` (the routing segment of the
`/api/hooks/{provider}/{key}` URL). The webhook key is generated on
create and never rotates — it stays burned for the connection's
lifetime so a tracker-side misconfiguration can be reproduced and
diagnosed. REST under `/api/v1/sources` SHALL demand `intake:read`
for reads and `source:write` for create / update / delete. The
update body is partial — `null` fields stay.

#### Scenario: Create source

- **WHEN** an operator with `source:write` creates a connection
- **THEN** the response is 201 including the webhook routing key once
- **AND** the settings json stores the operator's payload verbatim —
  the host never interprets or rewrites it

#### Scenario: Update source (PATCH semantics)

- **WHEN** an operator with `source:write` patches a connection with a
  partial body
- **THEN** the response is 200 with the updated view; every `null`
  field stays, every non-null field replaces the stored value

### Requirement: Admission rules as sibling rows (issue #40)

Admission rules SHALL live in `admission_rules` as a sibling
collection, NOT as a nested field on `source_connections`. The rule's
project id is the routing key — one project, one rule (or many,
keyed by `(project_id, mode)`). REST under `/api/v1/admission-rules`
SHALL demand `intake:read` for reads and `source:write` for writes.
The host SHALL also expose a sibling-of-the-source write path
`PUT /api/v1/sources/{sourceId}/rules/{ruleId}` so the dashboard's
nested watch form can route its writes through the same endpoint
shape as the read path; the operation is identical to the flat
`PUT /api/v1/admission-rules/{ruleId}`.

#### Scenario: Update rule under source

- **WHEN** an operator PUTs to
  `/api/v1/sources/{sourceId}/rules/{ruleId}` with a partial body
- **THEN** the response is 200 with the updated `AdmissionRuleView`
- **AND** the source id is accepted for routing symmetry with the
  dashboard's nested watch form but the lookup is by rule id alone

### Requirement: Source probe — draft and stored (issues #41, #42)

The host SHALL expose two probe endpoints to answer "can the host
reach this provider with the supplied credential?":

- `POST /api/v1/sources/probe` takes a draft body
  (`{ provider, settingsJson, secretEnvRef }`) and resolves the
  credential from the env-var reference at call time. No row is
  persisted.
- `POST /api/v1/sources/{id}/probe` takes an empty body, looks up
  the stored connection, and probes with its stored credential.

Both SHALL demand `source:write`. The answer SHALL always be 200
with `{ reachable, latencyMs, suggestedId?, message }` — a rejected
credential is a result, not a 5xx. The host SHALL respect a 5-second
timeout; on timeout `reachable=false` with the timeout sentence.

#### Scenario: Probe a draft with a missing secret

- **WHEN** an operator posts to `/api/v1/sources/probe` with
  `provider: github`, a settings json, and an env-var name that does
  not exist
- **THEN** the response is 200 with `reachable=false` and a sentence
  that surfaces the credential-resolution miss

#### Scenario: Probe a stored connection

- **WHEN** an operator posts to `/api/v1/sources/{id}/probe`
- **THEN** the response is 200 with `reachable`, `latencyMs`, and a
  provider-specific status sentence based on the stored settings
  and credential
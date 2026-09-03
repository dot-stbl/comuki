## ADDED Requirements

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

### Requirement: Intake profile router seam
The intake module SHALL expose an `IIntakeProfileRouter` port that maps
an admitted ticket to a worker profile key. The default implementation
SHALL honor an explicit per-connection override from the connection's
settings jsonb (`profileKey` field) when present, SHALL fall back to
`pr-review` for `PullRequest`-kind tickets, and SHALL fall back to
`Intake:Worker:IssueDefaultProfileKey` (default `general`) for `Issue`-
kind tickets. The intake module SHALL NOT depend on the engine — the host
composes the implementation. The router SHALL never throw; broken JSON,
missing fields, and non-string values silently use the fallback.

#### Scenario: PR kind routes to pr-review
- **WHEN** an admitted `PullRequest`-kind ticket has no per-connection
  override
- **THEN** `IIntakeProfileRouter.ResolveProfileKey` returns `pr-review`

#### Scenario: Per-connection override wins
- **WHEN** a connection's settings jsonb declares
  `"profileKey": "explore-readonly"`
- **THEN** the router returns `explore-readonly` regardless of ticket
  kind

#### Scenario: Issue kind uses the configured default
- **WHEN** an admitted `Issue`-kind ticket has no per-connection
  override
- **THEN** the router returns `Intake:Worker:IssueDefaultProfileKey`
  (default `general`)

#### Scenario: Broken settings json falls back silently
- **WHEN** the connection's settings jsonb is malformed or the
  `profileKey` field is a non-string
- **THEN** the router returns the kind-based default and does not throw

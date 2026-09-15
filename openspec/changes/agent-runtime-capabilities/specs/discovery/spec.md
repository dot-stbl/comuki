## Purpose

Discovery is how the agent learns what a project looks like —
environments, integrations, stack, conventions, secret requirements —
without the operator typing it in. It runs the existing
`explore-readonly` worker profile with a brief, captures the report
into project-scoped memory, and exposes the result as a chat slash
command and an MCP tool.

## ADDED Requirements

### Requirement: Discovery MCP tool

The host SHALL expose `discovery.scan` as an MCP tool. The tool SHALL
accept `{ projectId, brief, mode }` where `mode` is one of `quick`
(default — languages, top-level layout, install commands),
`secrets` (`.env.example` / IaC patterns), `integrations` (declared
providers), `full` (everything). The tool SHALL return a
`DiscoveryRunId` synchronously and SHALL stream progress over the
existing MCP notification channel.

#### Scenario: Scan returns a run id

- **WHEN** an MCP client calls `discovery.scan` with a project id
- **THEN** the response carries a `discoveryRunId` referencing a
  worker run started under the `explore-readonly` profile

### Requirement: Discovery brief

The tool SHALL construct a `brief` argument for the worker profile
that names the project, asks the questions the mode selects, and
limits the worker to read-only filesystem access. The brief SHALL
instruct the worker to emit a single Markdown report at
`/run/output/discovery.md` with a `## Findings` section per topic
and a `## SecretRequirements` block listing detected secret names.

#### Scenario: Brief is bounded

- **WHEN** a scan starts in `quick` mode
- **THEN** the worker's brief lists exactly the topics
  `Languages`, `Layout`, `Install`, `Conventions` and nothing else

### Requirement: Discovery writes to memory

When the worker run completes, the host SHALL parse the report's
`## Findings` sections and write one `MemoryFact` per finding with
`scope = project`, `subjectId = projectId`, `kind = standing`,
`source = run`. The `## SecretRequirements` block SHALL write one
`MemoryFact` per detected secret name with `topicKey =
discovery.secret.<name>` so re-scans supersede cleanly.

#### Scenario: Re-scan supersedes prior findings

- **WHEN** a project is re-scanned and the report lists the same
  secret requirement
- **THEN** the prior `MemoryFact` row's `SupersededAt` is set and the
  new row becomes visible to search

### Requirement: Discovery chat slash command

`/discover [mode] [projectId]` SHALL be registered in the existing
chat slash catalog. The slash SHALL translate to a chat turn that
calls the `discovery.scan` MCP tool, surface the run id, and offer a
follow-up `/show discovery` action to render the captured report.

#### Scenario: Slash requires a project

- **WHEN** `/discover` is invoked without a project id and the chat
  has no current project context
- **THEN** the slash returns a clarification prompt

### Requirement: Discovery run lifecycle

A discovery run SHALL be a normal `WorkerRun` with profile
`explore-readonly`; the host SHALL NOT introduce a separate pool or
quota. Discovery runs SHALL be visible in the runs dashboard with
`reason = discovery.scan` and SHALL count against the project's
`MaxConcurrent` setting.

#### Scenario: Discovery shares the worker pool

- **WHEN** a discovery run starts while the project already has
  `MaxConcurrent` workers running
- **THEN** the run is queued; it does not bypass the cap

### Requirement: Discovery audit

Every discovery run SHALL write a `discovery.scan` audit event in
`RunEvent` with the actor subject, the project, and the chosen mode.
No resolved secrets SHALL appear in the event payload.

#### Scenario: Audit has no plaintext

- **WHEN** a discovery run completes and writes its audit event
- **THEN** the event payload contains the mode and the captured
  finding count; no secret values are present
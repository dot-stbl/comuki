## ADDED Requirements

### Requirement: /memory slash command

The chat slash catalog SHALL register `/memory` (alias: `/mem`).
With no argument, `/memory` returns a one-line-per-fact summary of
the visible scope's standing facts plus a `forget` affordance per
row. With `forget <id>` it SHALL call `memory.forget` MCP tool and
return the new visibility. With `clear <topicKey>` it SHALL forget
all facts sharing that topic key in the current scope.

#### Scenario: /memory lists project facts

- **WHEN** the user invokes `/memory` in a chat bound to project
  `<p>`
- **THEN** the response lists the visible project-scoped facts and
  none from other projects

### Requirement: /discover slash command

The chat slash catalog SHALL register `/discover [mode]`
(alias: `/scan`). With no `mode` argument, default mode is `quick`.
The slash SHALL emit a `discovery.scan` MCP call, surface the
returned run id, and link to the discovery run in the runs
dashboard. When the chat has no current project binding, the slash
prompts for clarification.

#### Scenario: /discover queues a worker run

- **WHEN** the user invokes `/discover secrets` in a chat bound to
  project `<p>`
- **THEN** the slash creates a discovery worker run with profile
  `explore-readonly`, mode `secrets`, project `<p>`, and returns
  the run id

### Requirement: secrets MCP tools

The host MCP server SHALL expose `secrets.list` and `secrets.get`.
`secrets.list` returns the caller's visible secrets (by subject +
RBAC) with id, name, category, scope, version, rotated at, and a
one-line `summary` (the secret's purpose tag if any). Neither tool
returns a resolved value. Both tools require `secret:read`.

#### Scenario: secrets.list respects RBAC

- **WHEN** a user with `Member` on project `<p>` calls `secrets.list`
- **THEN** the response contains project-scoped secrets for `<p>`
  plus the user's own personal secrets; platform-scoped secrets are
  absent unless the user holds `secret:read` for `platform`

### Requirement: discovery MCP tool

The host MCP server SHALL expose `discovery.scan` with arguments
`{ projectId, mode, brief? }`. Calling it SHALL create a discovery
worker run and return its run id. The tool requires `discovery:run`
on the target project.

#### Scenario: discovery.scan without access

- **WHEN** a user without `discovery:run` on project `<p>` calls
  `discovery.scan` for `<p>`
- **THEN** the tool returns a `permission.denied` error and no
  worker is started
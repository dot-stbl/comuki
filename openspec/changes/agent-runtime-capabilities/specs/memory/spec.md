## ADDED Requirements

### Requirement: BrainRequest carries scope

`BrainRequest` SHALL add two optional fields: `ScopeKind`
(`user` | `project` | `global`, nullable) and `SubjectId`
(Guid string, nullable). When present, the brain runtime SHALL call
`IMemoryStore.SearchAsync(scope, subjectId)` before the model loop
runs and prepend the results to the digest. When absent, the brain
falls back to the existing global-only default; this preserves
existing callers.

#### Scenario: Project-scope digest

- **WHEN** chat invokes a brain call with
  `ScopeKind = project, SubjectId = <guid>`
- **THEN** the digest fed to the model includes project-scoped
  `MemoryFact` rows and excludes facts from other projects or
  global-only secrets

### Requirement: MemoryDigest accepts a scope

`MemoryDigest.Build(task, scope, subjectId?)` SHALL accept an
optional `ScopeKind` / `SubjectId` and SHALL return the union of
top-relevant facts under that scope plus the freshest standing
global facts not already included, deduplicated by id. The
existing global-only call SHALL keep working and SHALL continue to
return the same content it does today for the same input.

#### Scenario: Scope filter is opt-in

- **WHEN** `MemoryDigest.Build` is called without a scope
- **THEN** the digest matches the v1 behaviour exactly

### Requirement: Discovery findings land as facts

The `discovery.scan` MCP tool SHALL write each finding of the
worker report as a `MemoryFact` with `scope = project`,
`subjectId = projectId`, `kind = standing`, `source = run`. The
`topicKey` for findings is `discovery.finding.<slug>`; for secret
requirements it is `discovery.secret.<name>`. Re-runs supersede
prior facts via the existing `topic_key` rule.

#### Scenario: Finding is project-scoped

- **WHEN** a discovery run for project `<p>` finds
  `Install = "pnpm install"`
- **THEN** the fact is written with `scope = project, subjectId = p,
  topicKey = discovery.finding.install, source = run`; it is
  visible to brain calls for project `<p>` and invisible to calls
  for other projects

### Requirement: Memory UI surfaces project facts

The dashboard's `/memory` page SHALL display facts grouped by scope.
Project-scoped facts SHALL appear only when a project context is
selected; the page SHALL render the `topicKey` and `source` as
inline metadata. A `forget` action SHALL be available to callers
with `memory:write` on the matching scope.

#### Scenario: Forget a discovery finding

- **WHEN** a user with `memory:write` clicks forget on a
  `discovery.finding.install` fact
- **THEN** the row's `SupersededAt` is set; the brain no longer
  sees it on subsequent calls for that project
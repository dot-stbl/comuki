## ADDED Requirements

### Requirement: Ambient subject scope (object axis)
Authorization's object axis SHALL be an ambient `SubjectScope` on the async
flow: unrestricted (platform-scope / system) or confined to an explicit set of
project ids. Request middleware SHALL establish the scope from the
authenticated subject's assignments before MVC/minimal endpoints run.
Background workers and the worker runtime SHALL declare
`ISubjectScopeAccessor.AsSystem("<consumer>")` inside a `using` for the
duration of their work. Reading `Current` with no established scope SHALL
throw — there is no default empty or unrestricted fallback for filters.

#### Scenario: Missing scope fails loud
- **WHEN** a DI-built DbContext with a scope accessor runs a query with no
  scope established
- **THEN** accessing the ambient scope throws rather than returning zero or
  all rows

#### Scenario: System consumer
- **WHEN** the lease reaper runs inside `AsSystem("lease-reaper")`
- **THEN** query filters see every project for that flow and restore the
  previous scope on dispose

### Requirement: Out-of-scope rows are absent
Persistence contexts that carry project-scoped aggregates (orchestration runs
today; projects similarly) SHALL apply EF global query filters so out-of-scope
rows are invisible. Downstream APIs SHALL surface misses as 404, never as 403
"deny" for the object axis. Permission filters remain responsible only for the
action axis (`RequiresPermission`).

#### Scenario: Scoped list omits foreign projects
- **WHEN** a subject confined to project A lists runs
- **THEN** runs of project B are absent from the result set

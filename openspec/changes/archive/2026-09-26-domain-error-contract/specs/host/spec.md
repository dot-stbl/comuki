# Domain Error Contract — Delta

## ADDED Requirements

### Requirement: Domain error contract

Every error response SHALL follow one contract, regardless of which module
raised the error. For each typed error class the response SHALL carry, and
SHALL keep stable across releases:

- the HTTP status of that class — 404 for the module not-found family,
  409 for the conflict family (uniqueness, optimistic-concurrency version,
  state-machine decision), 400 for request validation, 402 for budget
  exhaustion, 403/404/502/504 for the upstream provider family, 422 for a
  semantic domain rule with no more specific class, 500 for an unhandled
  exception;
- a `type` field holding the stable URN `urn:comuki:error:{code}` built
  from the error's machine `code` (`about:blank` only for unhandled
  exceptions);
- a short, stable, human `title`;
- a `code` extension carrying the machine dot.case identifier clients
  branch on;
- the per-error domain extensions the class already ships (for example
  `projectId` on project not-found, `projectId` and `currentVersion` on
  settings version conflict) with unchanged names, JSON types and
  semantics.

A domain error class SHALL carry its `code` as a fact of the class: the
value is fixed per error type and identical for every occurrence; callers
cannot supply or alter it. An error thrown with no dedicated mapping
SHALL still answer 422 with its own `code` and URN — never a bare 500 —
and the gap SHALL be observable in host logs so missing mappings are
found.

Validation failures SHALL answer 400 with the ValidationProblemDetails
per-field `errors` dictionary (field name → array of messages, grouped
by field, ordinal order), unchanged in shape from the per-module
mappers this contract replaces.

#### Scenario: Module not-found keeps its shape

- **WHEN** any projects endpoint is called with an unknown project id
- **THEN** the answer is 404 with `type:
  "urn:comuki:error:project.not_found"`, `title: "Project not found"`,
  the `code` extension `project.not_found` and the `projectId` extension
  carrying the requested id as a string

#### Scenario: Settings version conflict keeps its retry payload

- **WHEN** a settings PUT presents a stale version
- **THEN** the answer is 409 with `code` `project.settings_conflict`,
  the `projectId` extension, and the `currentVersion` extension carrying
  the version actually stored, so the client can re-read and retry

#### Scenario: Validation shape is unchanged

- **WHEN** a request fails FluentValidation with two failures on one
  field and one on another
- **THEN** the answer is 400 whose `errors` dictionary has one key per
  field, each mapping to the array of that field's failure messages

#### Scenario: Unmapped domain error fails safe and visible

- **WHEN** a handler throws a domain error that has no dedicated mapping
- **THEN** the answer is 422 with the error's own `code` and
  `urn:comuki:error:{code}` type, and the host logs a warning naming the
  error type

#### Scenario: Unhandled exception stays opaque

- **WHEN** an exception with no domain or provider mapping escapes
- **THEN** the answer is 500 with `about:blank` type and no stack trace,
  secret or PII in the body

## MODIFIED Requirements

### Requirement: TypedResults.Problem is the error-response convention

Every endpoint that returns an error response SHALL build the body via
`TypedResults.Problem(...)` (or `TypedResults.ValidationProblem(...)` for
400 ValidationProblemDetails). The body content type SHALL be
`application/problem+json` and SHALL carry the `code` extension when the
source is a typed exception. The single composition-root
`ProviderExceptionHandler` (`IExceptionHandler`) is the canonical — and
only — mapper from typed exceptions to `TypedResults.Problem`: it
resolves one custom handler per domain error type and falls back to the
kernel defaults. Endpoints and controllers SHALL NOT catch typed
exceptions to map them; per-module runner/helper mapping is retired, and
no new endpoint-local catch-map blocks are introduced. Ad-hoc envelopes
(anonymous `{ "error": ... }`, bare `Results.Json`, hand-rolled
200-with-error shapes) are forbidden.

#### Scenario: Typed exception surfaces RFC 9457

- **WHEN** a handler throws `ProviderException("provider.network_error", "...")`
- **THEN** the host answers with `TypedResults.Problem(..., extensions: { "code" = "provider.network_error" })` and
  `Content-Type: application/problem+json`

#### Scenario: Controller does not hand-roll the body

- **WHEN** a controller needs to return an error
- **THEN** it builds with `TypedResults.Problem(...)` (or lets
  `ProviderExceptionHandler` map the thrown exception); no
  `Results.Json(new { error = ... })`

#### Scenario: Module endpoint does not catch typed exceptions

- **WHEN** a module endpoint's handler throws a typed domain error
- **THEN** the endpoint body contains no catch block for it; the
  composition-root handler produces the response, and the wire shape is
  the one the module shipped before its runner was retired

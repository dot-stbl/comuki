## Why

Every module surface maps its typed exceptions to ProblemDetails in its own
catch block — `ProjectsEndpointRunner`, `IntakeEndpointRunner`,
`SchedulerEndpointRunner`, `ChatEndpointRunner`, `WorkerResults`,
`RunsController`, `LearningCandidatesController`, `KnowledgeModuleEndpoints`,
`ProxyKeyAdminEndpoints` — while the global `ProviderExceptionHandler`
already exists and already maps the kernel hierarchy
(`ProviderException` family, `BudgetExceededException`, `DomainException` →
422). The result is ten copies of the same catch-map-log plumbing, codes and
titles as string literals duplicated between throw site and catch site, and
a doc comment on the global handler that blesses the per-module runners as
transitional "until PR #20". That retirement is this change. It builds on
the in-flight exception `Code` work (D-BE) and takes it to the end state:
one domain-error contract, custom handlers per domain error type, endpoints
that stop try/catching.

## What Changes

- Module domain exceptions (`ProjectNotFoundException`,
  `ProjectConflictException`, `ProjectSettingsConflictException`, and their
  siblings in Intake / Scheduler / Chat / Runs / Workers / Learning /
  Knowledge / Proxy) derive from the kernel `DomainException` and carry
  their stable dot.case `Code` as a class fact — never as a
  caller-supplied ctor string.
- A typed problem-handler registry in the host: one `IProblemHandler` per
  domain error type, collected by DI, resolved by exception type (exact
  type first, then base chain). Adding a domain error adds a handler
  class; no existing file is edited (registry, not switch sprawl).
- `ProviderExceptionHandler` becomes the single mapper: registry lookup →
  RFC 9457 ProblemDetails with stable `type` (`urn:comuki:error:{code}`),
  `title`, `status`, the `code` extension from the exception, and
  per-domain extensions (`projectId`, `currentVersion`) preserved
  byte-for-byte. The kernel arms (Provider family, Budget,
  `DomainException` default 422) become built-in registry entries.
- FluentValidation `ValidationException` gets one core handler producing
  the same 400 ValidationProblemDetails per-field `errors` shape the five
  per-runner copies produce today.
- Per-module endpoint runners retire: endpoints call handlers directly and
  stop try/catching; `*EndpointRunner` / `*Problems` helper files are
  deleted. Migration is module-by-module, each step wire-compatible
  (status / code / title / extensions / validation shape unchanged; the
  only additive wire delta is a `type` URN on module errors whose runners
  did not set one yet).
- No FE changes, no kubb regen, no new endpoints, no new permissions.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `host`: the "TypedResults.Problem is the error-response convention"
  requirement widens — the composition-root handler plus the typed
  registry is the only exception→ProblemDetails mapper, and endpoints
  SHALL NOT try/catch typed exceptions. A new "Domain error contract"
  requirement pins the observable response shape per error class
  (status, `type` URN, `title`, `code`, domain extensions, validation
  shape).

## Impact

- Backend: `Comuki.Shared.Kernel` (no new types — `DomainException` is
  the base), every module Application layer holding plain-`Exception`
  domain exceptions (re-parenting), `Comuki.Host/Errors/**` (registry,
  handlers, `ProviderExceptionHandler` rewrite), `Comuki.Host` per-module
  endpoint files (runner removal), `HostComposer` (handler
  registrations).
- Tests: new `tests/unit/Comuki.Host.Unit.Errors` (registry resolution,
  per-handler wire shape); existing per-module endpoint tests keep
  asserting the same 4xx shapes.
- FE: none (contract preserved; `problem.ts` keeps reading
  `detail`/`title`/`code`).

## Non-goals

- Changing any status code, `code` value, title, extension or validation
  error shape that ships today — the FE forms (`touched || attempted`
  per-field errors) and the settings-conflict retry flow
  (`project.settings_conflict` + `currentVersion`) depend on them.
- Re-mapping value-based validation returns (Auth controllers'
  result-object flows) — only exception→ProblemDetails mapping
  consolidates.
- Touching `RequiresPermissionFilter`'s 403 (it is a filter concern, not
  an exception mapping).
- Error payloads for the worker gRPC/Translator surface (different wire,
  different change).

## Context

See proposal.md for why. Constraints that shape the how:

- The kernel already has the hierarchy: `Comuki.Shared.Kernel.Exceptions`
  holds `DomainException(code, message)` with a `Code` property, the
  `ProviderException` family (each carrying `Code`), and
  `BudgetExceededException`. The global `ProviderExceptionHandler`
  (`Comuki.Host/Errors/`, wired in `HostComposer.cs:444` behind
  `AddProblemDetails()`) maps them via one file-static switch
  (`ExceptionMapping.Map`) to RFC 9457 rows — `type` is already
  `urn:comuki:error:{code}` there. Its doc comment names per-module
  runners as transitional "until PR #20".
- Worker D-BE (in flight, baseline for this change) adds a `Code`
  property + `type:` URNs to the Projects exceptions and makes
  `ProjectsEndpointRunner` read `exception.Code`. This change takes that
  landed state and finishes the arc; the deltas from D-BE's state are
  named in D1/D8.
- The consolidation surface (grep `TypedResults.Problem` under
  `platform/src/host`): per-module runners `ProjectsEndpointRunner`,
  `IntakeEndpointRunner` (+ `IntakeProblems`), `SchedulerEndpointRunner`,
  `ChatEndpointRunner`; inline mappings in `WorkersReadEndpoints`,
  `WorkerResults`, `WorkerEndpoints`, `UploadArtifactEndpoint`,
  `RunsController`, `AuthController`, `LearningCandidatesController`,
  `KnowledgeModuleEndpoints`, `ProxyKeyAdminEndpoints`. Most are
  message-only rows; a few carry payload extensions (`projectId`,
  `currentVersion`); five carry copy-pasted `ValidationException`
  grouping.
- Domain exceptions live in two places today: module Application layers
  (`ProjectNotFoundException`…) and the host itself
  (`RunDecisionConflictException`, `ChatApprovePendingException`).
  `SecretRefUnsetException` lives in `Comuki.Shared.Kernel.Secrets` and
  maps to 400.
- FE contract dependencies: `dashboard/src/shared/api/problem.ts` reads
  `detail`/`title`; forms gate field errors on `touched || attempted`
  against the 400 per-field `errors` dictionary; the settings retry flow
  keys on `project.settings_conflict` + `currentVersion`
  (`dashboard/src/domains/projects/model/types.ts:65`). These shapes are
  load-bearing and byte-stable through this change.
- Canon: `~/.agents/rules/csharp/variation-points.md` (branch from data →
  typed registry), `error-mapping.md` (providers throw typed exceptions
  with stable `Code`; one handler maps to ProblemDetails),
  `problem-details.md` (RFC 9457, `TypedResults.Problem`),
  `exceptions.md` §3 (one hierarchy, stable `Code`, no PII).

## Goals / Non-Goals

**Goals:**

- One mechanism turning a thrown typed exception into its ProblemDetails
  row; endpoints contain no error plumbing.
- Per-domain-error-type handlers: adding an error type adds a handler
  class and one registration line; no existing mapping file is edited.
- Wire compatibility: every module's 4xx shapes (status, code, title,
  extensions, validation dictionary) are byte-identical before and after
  its migration step. Only additive delta: `type` URN on errors whose
  runner didn't set one.
- Migration sliced so every step independently builds, tests green, and
  reverts cleanly.

**Non-Goals** (design-level, beyond proposal non-goals):

- No new kernel exception types beyond what exists; `DomainException` is
  not redesigned (no abstract `Code` override chain — see D1).
- No OpenAPI metadata changes (minimal APIs carry no per-endpoint
  `ProducesResponseType` today; kubb client untouched).
- No error-code renaming, no re-tuning of which class gets which status.
- gRPC / Translator worker-facing errors stay as they are.

## Decisions

### D1. `DomainException` is the base; `Code` is a class fact, not a ctor parameter

Module and host domain exceptions re-parent from plain `Exception` to
the kernel `DomainException`. Each subclass fixes its code as a constant
declared beside the exception and passes it to the base ctor — the
exception's own ctor signature never accepts a code, so no call site can
invent one:

```csharp
public sealed class ProjectNotFoundException(ProjectId projectId)
    : DomainException(Code, $"project '{projectId}' not found")
{
    private const string Code = "project.not_found";
    public ProjectId ProjectId { get; } = projectId;
}
```

D-BE's baseline (a `Code` property read by the runner) is preserved —
`DomainException.Code` satisfies it; the delta is only the base class
and where the constant lives. **Rejected:** an abstract override-style
`Code` (forced virtual property) — buys nothing over the const-into-base
form and fights the primary-constructor idiom. **Rejected:** a per-module
intermediate base (`abstract ProjectException`) — a name with no
behavior; payloads differ per leaf, so leaves derive from
`DomainException` directly. Cross-cutting exceptions that are not domain
errors keep their own hierarchies (`ProviderException` family;
`SecretRefUnsetException`; FluentValidation's `ValidationException`) —
the registry maps by exact type regardless of hierarchy.

### D2. Dispatch: a typed registry collected by DI — not a switch, not a handler chain

The thrown exception type is data: the exception pipeline cannot know it
at compile time, so per `variation-points.md` the dispatch is a typed
registry, not a growing switch and not caller-known branches.

```csharp
// Comuki.Host/Errors/ProblemAnswer.cs — the neutral wire row
public sealed record ProblemAnswer(
    int StatusCode,
    string Type,
    string Title,
    string Detail,
    string Code,
    IReadOnlyDictionary<string, object?> Extensions,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
// ValidationErrors non-null ⇒ the handler emits ValidationProblemDetails (400)

// Comuki.Host/Errors/IProblemHandler.cs
public interface IProblemHandler
{
    Type ExceptionType { get; }
    ProblemAnswer Answer(Exception exception);
}
```

`ProviderExceptionHandler.TryHandleAsync` shrinks to: resolve → log →
`TypedResults.Problem`/`ValidationProblem` → execute. **Why not**
per-module `IExceptionHandler` implementations chained in the host
(ASP.NET Core supports several via repeated `AddExceptionHandler`):
ordering becomes implicit and fragile (most-derived must win across N
handlers), each copy re-does the HttpContext plumbing, and the "one
place decides what an exception means on the wire" property dissolves.
**Why not** extending the file-static `ExceptionMapping` switch: every
module error would edit one central file — switch sprawl across modules,
exactly the shape the owner rejected.

### D3. One handler per domain error type; a generic base for the common shape

One `internal sealed class <Error>ProblemHandler : ProblemHandler<TException>`
per domain error type, grouped `Errors/Handlers/<Module>/` beside the
host's existing module folders. A small abstract generic base carries the
boilerplate:

```csharp
public abstract class ProblemHandler<TException>(int statusCode, string title)
    : IProblemHandler where TException : Exception
{
    public Type ExceptionType => typeof(TException);
    public ProblemAnswer Answer(Exception exception) => Answer((TException)exception);
    protected abstract ProblemAnswer Answer(TException exception);
}
```

Message-only handlers are 3-line subclasses; payload handlers
(`ProjectNotFoundProblemHandler`) add the typed extensions. Per canon
`class-layout-and-tooling.md` the protected abstract hook on an abstract
base is the sanctioned exemption; handlers with no logic beyond the base
may share a file with their siblings (paired internal classes), which
also keeps folders within the ≤3-files heuristic for modules with few
error types. Handlers are registered by one
`Add<Module>ProblemHandlers()` extension per module surface, called from
`HostComposer` next to `AddExceptionHandler` — the composition root
stays explicit.

### D4. Type URN scheme: `urn:comuki:error:{code}`, `about:blank` only for the unhandled 500

Same scheme D-BE lands for Projects and `ExceptionMapping` already uses
— one rule, no per-module vocabulary. The URN is always derived from the
row's `Code`, never hand-written per handler, so `type` and `code`
cannot disagree.

### D5. Registry resolution: exact type → base chain → kernel defaults, with a logged gap

`ProblemHandlerRegistry` (built once at startup from the DI-collected
`IEnumerable<IProblemHandler>`, keyed by exact `ExceptionType`) resolves
by walking the exception's base-type chain on miss. Always-registered
defaults: `ProviderTimeout` 504, `ProviderNotFound` 404,
`ProviderForbidden` 403, `ProviderException` 502, `BudgetExceeded` 402,
`DomainException` 422 (using the instance's `Code`), `ValidationException`
400, `Exception` 500/`about:blank`. Exact module rows always beat
defaults because exact type is consulted first — no ordering tricks, the
"most-derived wins" property falls out of the lookup order. A
`DomainException` subclass that reaches the 422 default without an exact
row logs a warning naming the type: fail-open on the wire (the code and
URN still identify it precisely), visible in ops (spec scenario "Unmapped
domain error fails safe and visible").

### D6. `ValidationException` becomes one core handler

The per-field grouping (PropertyName → ordinal-grouped `string[]`) is
copy-pasted in five runners today. One core handler owns it; the answer
carries the errors dictionary and the global handler emits
`TypedResults.ValidationProblem` when it is present. The 400 shape —
keys, ordinal grouping, message order — is pinned by unit test against a
fixture identical to the current runners' output before any runner is
deleted.

### D7. Migration order: mechanism inert first, then one module per commit

1. **Mechanism, inert** — registry + core defaults +
   `ProviderExceptionHandler` rewrite reproduce current global behavior
   byte-for-byte (kernel exceptions only). No module rows, no runner
   deleted. Verified by the existing host error tests plus new
   `Comuki.Host.Unit.Errors` resolution tests.
2. **Projects** — re-parent + handlers + runner deletion. First because
   D-BE's landed work makes it the freshest and it carries the richest
   extensions (`projectId`, `currentVersion`), i.e. the hardest case;
   settling it proves the pattern.
3. **Intake** (largest family, five rows + `IntakeProblems` helper
   dies), **Scheduler**, **Chat**, **Runs**, **Learning**,
   **Knowledge**.
4. **Workers last among HTTP surfaces** (`WorkersReadEndpoints`,
   `WorkerResults`, `WorkerEndpoints`, `UploadArtifactEndpoint`): the
   worker SDK consumes these 4xx shapes for retry/backoff decisions, so
   its migration waits until the pattern is proven; `UploadArtifact`'s
   `ParsedMultipartFile.WithFailure` flow is value-based in part — only
   its catch blocks migrate, the value flow stays.
5. **Proxy admin** (lowest traffic), then delete dead helpers and update
   the `ProviderExceptionHandler` doc comment (the "until PR #20"
   transitional note resolves to "done").
6. **FE gates** run once at the end as a no-change proof
   (typecheck/lint/test/build all green with zero dashboard edits).

Each module step is one commit: add rows + shape tests asserting the
exact pre-migration literals (status/type/title/code/extensions), then
delete the runner arms in the same commit so the tests and the deletion
cannot drift apart. Re-parented module exceptions that previously
escaped a runner would now hit the 422 default instead of 500 — an
improvement, and observable only on paths that were already bugs.

### D8. Coordination with D-BE's in-flight work

D-BE lands: `Code` on the three Projects exceptions, `type:` URNs in the
Projects runner, runner reads `exception.Code`. This change is blocked
on that landing (task 1.1 verifies it). Deltas taken here: re-parent to
`DomainException` (D1), move the URN construction out of the runner into
the answer/registry (D4), delete the runner (D7.2). If D-BE's branch and
this change race, D-BE wins the exception files; this change rebases.

### D9. Tests

New `tests/unit/Comuki.Host.Unit.Errors` (MTP-style `dotnet run --project`):
registry resolution order (exact beats default, base chain walk, sealed
subclass), each core default row, the `ValidationException` shape
fixture, and per-module handler shape tests added in the same commit as
each module's migration. Existing per-module endpoint tests keep
asserting the same 4xx shapes — they are the contract-preservation
proof.

## Risks / Trade-offs

- **[Risk] Wire drift during migration** (a handler literal diverges
  from the runner it replaces) → every module step pins the shape by
  test written from the pre-migration literals before the runner is
  deleted; both live in one commit.
- **[Risk] Merge collision with D-BE** on the Projects exception files →
  D8: D-BE owns those files until landed; this change starts at task 1.1
  only after that.
- **[Risk] A registered handler's exact type is a non-sealed class, so a
  subclass silently falls to the base chain** → registry asserts at
  startup that registered exact types are sealed (loud, at boot).
- **[Trade-off] ~20 tiny handler classes** vs one declarative table →
  bought deliberately: per-type testability, open/closed growth, and the
  owner's explicit "custom handlers per each domain error type".
- **[Trade-off] `DomainException` default stays 422 (fail-open)** rather
  than 500-on-unmapped → an unmapped domain error still answers with its
  own code and URN; the warning log + startup-seal assertions close the
  visibility gap.

## Migration Plan

No schema change, no config change, no deploy coupling: every step is
backend code + tests, deployed as ordinary builds. Rollback of any
module step = revert that module's commit (the runner deletion and the
handler addition are one unit); rollback of the mechanism = revert the
inert-introduction commit — no step leaves a state where an error is
unmapped, because defaults answer everything.

## Open Questions

None blocking. Two recorded trigger conditions, not unknowns: (a) if the
worker SDK ever needs to branch on `type` URNs rather than `code`, that
is a separate SDK contract change; (b) if a future module wants its
error codes surfaced in OpenAPI as typed schemas, that is a new
`IOpenApiOperationTransformer` change, not this registry's concern.

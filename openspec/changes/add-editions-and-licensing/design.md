## Context

See `proposal.md` for the why, `decisions.md` for the verbatim E1–E11
user decisions this design traces to, and `specs/editions/spec.md` /
`specs/host/spec.md` for the normative behavior. Today:

- **No capability registry exists.** Per-feature gates, when added at
  all, would be hand-rolled `if (options.Premium) { ... }` checks in
  handler bodies — the exact thing `Comuki.Modules.Identity.Domain.Permissions.Permissions.cs`
  is, in its `RoleMatrix` half, documented to avoid. `Permissions.cs:8`
  says "every key must be assigned to at least one role … a unit test
  holds that invariant"; the parallel invariant here is every gate
  call site must reference a key in the `Features` catalog.
- **No tier model exists.** Edition / tier is not a configuration
  concept today; the platform runs whatever it ships, no gating.
- **No license handling.** `SecretRef` is the only place ops-stored
  secrets land today (`platform/src/shared/Comuki.Shared.Kernel/Secrets/SecretRef.cs:1`);
  the resolution surface is `ISecretResolver`
  (`platform/src/shared/Comuki.Shared.Kernel/Secrets/ISecretResolver.cs`),
  routed through providers by `Scheme`. License blobs go through the
  same surface, never through a bespoke loader — the binding host
  rule for "ops uses the same TOML / env / vault story as every
  other secret in this repo" applies.
- **TOML config is the front door.** `ComukiConfigFile.Find()`
  (`platform/src/shared/Comuki.Shared.Bootstrap/Config/Toml/ComukiConfigFile.cs:23`)
  resolves `COMUKI_CONFIG_PATH` → `./config.toml` → `/etc/comuki/config.toml`
  (Linux only); the provider always registers and is optional — no
  file means empty config, never an error at boot. License config
  follows the same rule (E5): absent = Community, present-but-broken
  = loud boot failure.
- **Exception mapping is one place.** `ProviderExceptionHandler`
  (`platform/src/host/Comuki.Host/Errors/ProviderExceptionHandler.cs:15`)
  is the single `IExceptionHandler`; its `ExceptionMapping.Map` switch
  arms (`ProviderExceptionHandler.cs:72`) already wire
  `ProviderForbiddenException` → 403 with a stable dot.case `Code`
  (`ProviderExceptionHandler.cs:94`). `edition.feature_unavailable`
  reuses this arm — same exception type, new constant on the gate
  attribute, no new typed exception needed (E7).
- **Permission gating already has the exact MVC-filter +
  minimal-API-middleware shape to mirror.** `RequiresPermissionFilter`
  (`platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/RequiresPermissionFilter.cs:33`,
  one `IAsyncResourceFilter`, reads `EndpointMetadata.OfType<RequiresPermissionAttribute>().LastOrDefault()`
  for "last wins") and `RequiresPermissionMiddleware`
  (`platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/RequiresPermissionMiddleware.cs:16`,
  one `RequestDelegate`-wrapping middleware) both call the shared
  `PermissionGate.EvaluateAsync` (`RequiresPermissionFilter.cs:79`) so
  the decision exists exactly once. The editions gate mirrors this
  shape (E9): one filter + one middleware + one shared
  `EditionGate.EvaluateAsync` — same `last wins` ordering, no
  parallel demand shape invented for MVC vs minimal API.
- **Options pattern is already canonical.** `RateLimitOptions`
  (`platform/src/host/Comuki.Host/Security/RateLimit/RateLimitOptions.cs:20`,
  one small Options class: `public const string SectionName`,
  `[Range]` data annotations, sensible `init` defaults) is the
  template `LicenseOptions` is modeled on; `HostTlsOptionsValidator`
  (`platform/src/host/Comuki.Host/Security/Tls/HostTlsOptionsValidator.cs:13`,
  `IValidateOptions<T>` for cross-field rules) is the template
  `LicenseOptionsValidator` is modeled on. Both run at
  `ValidateOnStart` time; failure fails boot loudly.
- **Background workers have a single registry.** `ComukiWorkerRegistry`
  (`platform/src/shared/Comuki.Shared.Bootstrap/Workers/ComukiWorkerRegistry.cs:22`,
  one `BackgroundService` over every `IComukiWorker` registration,
  per-cycle DI scope, exponential backoff) is the place where a
  `[RequiresFeature]` attribute on an `IComukiWorker` is checked
  before registration. This is the **hosted-service** worker
  abstraction — `LeaseReaperComukiWorker`, `MemorySweepComukiWorker`,
  `ScheduledJobDispatcherComukiWorker` — **not** the ephemeral
  coding-agent worker from `worker-runtime`. The two never share
  a type and this change names the distinction explicitly (E9,
  E10).
- **Realtime codegen has a CLI shape to mirror.**
  `tools/Comuki.Codegen.Realtime/Program.cs` (`--out <path>` else
  stdout, normalized `\n` line endings for Windows / Linux
  byte-identical output) and
  `platform/src/shared/Comuki.Shared.Contracts/Realtime/RealtimeContractEmitter.cs:23`
  (one `export interface` / `export const` block per catalog entry)
  is the template the new `tools/Comuki.Codegen.Editions` CLI is
  modeled on — same CLI shape, same normalized-output discipline.
  `kubb` (`dashboard/kubb.config.ts`) still owns the `/api/v1/edition`
  response shape from the OpenAPI schema; the new emitter owns the
  feature / limit keys themselves, the same relationship
  `RealtimeContractEmitter` already has to SignalR contracts kubb
  doesn't cover.
- **Version identity is one place.** `ComukiBuildInformation`
  (`platform/src/shared/Comuki.Shared.Bootstrap/Versioning/ComukiBuildInformation.cs:17`,
  sourced from `AssemblyInformationalVersionAttribute`) is what
  `add-worker-commit-attribution` reads for the `Generated-by:
  Comuki vX.Y.Z` trailer. This change does not re-specify it; both
  changes read the version the same way (one-sentence cross-reference
  in E11).
- **Existing budget gate is the closest analog.** `OrchestrationBudgetGate`
  (`platform/src/host/Comuki.Host/Costs/OrchestrationBudgetGate.cs:25`,
  `EnforceClaimAsync` checks a cap and throws `BudgetExceededException`
  with stable dot.case `Code` = `"budget.hard_exceeded"` before the
  guarded action runs; soft-cap path only warns) is the shape
  edition gating mirrors — `EnforceAsync(featureKey)` throws or
  passes; grace-period warning is the soft-cap analog.

The design must preserve the modular-monolith law: sibling
`Comuki.Modules.*` never reference each other's implementations (see
`add-multi-repo-projects/design.md`'s "modular-monolith law"
paragraph for the house rule and `Comuki.Architecture.Tests` for how
it is enforced). The capability registry and license / edition-gate
logic must be reachable from *every* module's installer, from Host,
and from background workers — the same shape as
`Comuki.Shared.Kernel/Secrets` or `Comuki.Shared.Telemetry`, not a
`Comuki.Modules.*` module. A module would force every other module
to depend on "the Editions module", which the modular-monolith law
forbids for domain modules but is exactly what `Comuki.Shared.*`
exists for. The new shared library is named accordingly below.

## Goals / Non-Goals

**Goals:**

- Open-core runtime gate: one binary, one codebase, edition = license.
- `Features` and `Limits` smart-typed catalogs as the single source
  of truth; generated Markdown table + generated TypeScript contract
  from the same source.
- Signed-offline license: two-part token, embedded public key, no
  phone-home. Hot-reload via `IOptionsMonitor` so a replaced license
  file is picked up without restart.
- Four enforcement points, each its own attribute / DI helper,
  sharing one `EditionGate.EvaluateAsync` decision function.
- `edition.feature_unavailable` / `edition.limit_exceeded` 403
  ProblemDetails, dot.case, code surface on the existing
  `ProviderForbiddenException` mapping arm.
- Grace-then-read-only-degrade on expiry / downgrade, no data loss.
- `GET /api/v1/edition` returns the tier, license status and visible
  feature / limit keys for the dashboard / CLI to consume.
- Three architecture tests enforce: registry ↔ attribute round-trip;
  every paid registry entry has at least one gate; Community-edition
  DI graph composes cleanly with `ValidateOnBuild`.

**Non-goals:**

- Implementing any of the paid feature capabilities themselves (they
  land in their own changes against this registry).
- Issuing licenses — this change verifies, never issues.
- Phone-home, telemetry exfiltration, or any other network call as
  part of verification.
- Dashboard / CLI **implementation** of the upsell — the endpoint
  contract is in scope, the consumers are not.
- A second binary, a second package, or a build-time edition switch.
- Refusing to boot on license expiry — read-only degrade is the
  product answer.

## Architectural placement

**New shared library `platform/src/shared/Comuki.Shared.Editions`.**
This is the design's answer for "where do the registry, the
license parser, the gate port and the smart-typed catalogs live?".
It lives in `Comuki.Shared.*` for the same reason
`Comuki.Shared.Kernel/Secrets` and `Comuki.Shared.Telemetry` do:
cross-cutting concerns that every module, every installer, and the
host need to reach, without depending on any one module. A
`Comuki.Modules.Editions` would force every other module to
project-reference it — the modular-monolith law forbids that for
domain modules.

The library exposes:

- `EditionTier` (`record struct`, ordinal `Rank` + open string
  `Code`).
- `LicenseKey` (parsed / verified record, payload + verified-on +
  tier + expiry + features / limits).
- `IEditionCapabilityRegistry` (`Feature` / `Limit` keyed, the
  registry enumeration consumed by the Markdown table generator and
  the architecture test).
- `Features` / `Limits` static catalogs built through
  `Feature.Define(...)` / `Limit.Define(...)` factories.
- `IEdition` (`Has(Feature)`, `Limit(Limit) -> int`, `Current`
  tier), backed by `ILicenseProvider` (parses / verifies the
  Ed25519-signed license) and exposed through
  `IOptionsMonitor<LicenseOptions>` so a replaced license file is
  picked up without restart.
- `IEditionGate` (the runtime check port: `EnforceAsync(featureKey)`,
  `IsAvailable(featureKey)`).
- `EditionFeatureAttribute` / `RequiresFeatureAttribute` /
  `EnforceLimitAttribute` (the declarative demand shapes).
- An internal `EditionGate.EvaluateAsync` shared by the MVC filter
  and the minimal-API middleware — same shape as
  `PermissionGate.EvaluateAsync` in
  `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/RequiresPermissionFilter.cs:79`.

The library is composed by Host (options binding, exception-handler
arm, `/api/v1/edition` endpoint, `AddComukiWorkers` extension
check) and referenced by any module's installer / hosted service
that needs a gate. No other project owns these types.

## Entities (wire-level shapes only — DTOs land in the
implementation workstream, not this design)

```text
EditionTier
  Rank: int          // 0 = Community, 1..N = paid (ordinal)
  Code: string       // open string ("community", "team", "enterprise", …)

LicenseKey (record, output of ILicenseProvider.Verify)
  Tier: EditionTier
  Org: string
  Expiry: DateTimeOffset   // UTC
  Features: IReadOnlySet<string>     // explicit allowlist (E4a mode a)
  Limits:   IReadOnlyDictionary<string, int>   // open number map
  Mode: LicenseMode       // ImplicitByRank | ExplicitAllowlist
  VerifiedAt: DateTimeOffset
  VerifiedWith: string    // short fingerprint of the public key that verified it

Feature (record, single registry entry)
  Key: string            // dot.case, resource:action, same well-formedness as PermissionKey
  Description: string
  MinimumRank: int
  SinceUnixMs: long?     // first tier the feature became available, optional

Limit (record, single registry entry)
  Key: string            // dot.case
  Description: string
  CommunityValue: int    // rank-0 cap
  PaidValues: IReadOnlyDictionary<int /* rank */, int>   // optional per-tier caps above Community
```

Naming note: the `Features` catalog is a **closed set of
capability keys**, distinct from the existing
`Permissions` catalog in
`platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Permissions/Permissions.cs`.
Edition gating and permission gating are **independent axes**:
both can apply to the same endpoint, both are `IEndpointFilter`-shaped,
evaluated in either order, and both produce 403 ProblemDetails
with their own dot.case `code` (`permission.denied` and
`edition.feature_unavailable` respectively). A endpoint that
demands both will surface the first failure — order is a
registration concern, not a domain decision.

## Flows

### 1. License resolution and hot reload

1. Host options binding reads `Host:License:Path` from `config.toml`
   (`ComukiTomlConfiguration.Load` → `IConfiguration.GetSection`).
   A missing section is allowed: the `IEditionCapabilityRegistry`
   resolves `Current = Community`, `Status = absent`, and the rest
   of the platform composes as if no license were ever mounted.
2. When `Host:License:Path` is set, the value is a `SecretRef`
   (`env:…` / `file:…` / `vault:…` / `consul:…`); the existing
   `ISecretResolver` reads it. A malformed reference (unknown
   scheme) throws at boot per `ISecretResolver`'s contract.
3. `ILicenseProvider.Verify(secretString)` parses the
   `base64url(payload).base64url(signature)` token, decodes the
   payload JSON, and verifies the signature against the embedded
   Ed25519 public key. A bad signature, malformed payload, expired
   `expiry`, or unknown tier code throws a typed
   `LicenseInvalidException` (host's exception handler maps it to
   502 `license.invalid` — an upstream-style ProblemDetails, never
   a 500 boot crash for an absent license, a loud-but-not-fatal
   boot log entry for an invalid one followed by Community
   behavior).
4. The verified `LicenseKey` lands in `LicenseOptions.Value` via
   `IOptionsMonitor<LicenseOptions>`. A changed file (replace,
   `dotnet`'s `reloadOnChange`, or ops `mv` + `touch`) re-runs
   `Verify`; the registry surfaces the new tier / status on the
   next `IEdition.Current` read.

### 2. Capability registry enumeration

`IEditionCapabilityRegistry.Entries` is an `IReadOnlyList<RegistryEntry>`
over every `Feature` and every `Limit`, sorted by `Key`. Two
consumers read it:

- **`Comuki.Codegen.Editions`** emits one Markdown table at
  `openspec/specs/editions/capability-matrix.md` (regenerated on
  every build, committed alongside code changes when a row is added
  or removed) and one TypeScript module (one `export const Features`
  object and one `export type FeatureKey` union per registry entry)
  to the dashboard / CLI generated path. Both artifacts are
  generated; the registry is the only maintained source.
- **The architecture test** in `Comuki.Architecture.Tests`
  reflection-scans the compiled assemblies for
  `[RequiresFeature]` / `[EnforceLimit]` / `[EditionFeature]`
  attributes and asserts every key referenced there exists in
  `Features` / `Limits` (the reverse direction
  `Permissions.cs:8` documents for roles ↔ permissions, applied
  here to registry ↔ gate).

### 3. Enforcement — four points, one shared gate

Each enforcement point calls one shared decision function so the
verdict exists exactly once.

**3a. Host API gating** — `[RequiresFeature(nameof(Features.X))]`
on MVC controllers / actions and `.RequireFeature(Features.X)` on
minimal-API `IEndpointConventionBuilder` extensions. Both paths
add the same attribute-shaped metadata to the endpoint, read by:
- `RequiresFeatureFilter : IAsyncResourceFilter` for MVC, modeled
  on `RequiresPermissionFilter` (last-wins metadata read,
  `LastOrDefault()` on `EndpointMetadata.OfType<T>()`).
- `RequiresFeatureMiddleware` for minimal APIs, modeled on
  `RequiresPermissionMiddleware`.

Both call `EditionGate.EvaluateAsync(principal, featureKey, ct)`
and produce the same `edition.feature_unavailable` 403
ProblemDetails. `[EnforceLimit(nameof(Limits.Projects))]` is a
sibling demand type — same filter + middleware pair, different
evaluator (compares a live count against
`Limit.CommunityValue` / `Limit.PaidValues[rank]`), same
problem+json code family with a sibling code
`edition.limit_exceeded`.

**3b. DI module / service gating** —
`AddComukiModule<TModule>()` (a generic wrapper that checks a
class-level `[EditionFeature(nameof(Features.X))]` attribute on
`TModule` before invoking that module's concrete installer,
following the "one `AddXxxModule` / `AddXxxApplication` extension
method per module" convention of
`platform/src/modules/Projects/Comuki.Modules.Projects.Application/ProjectsApplicationExtensions.cs:24`
and `platform/src/engine/Comuki.Engine.Compute/Installers/ComputeInstaller.cs`).
A module whose feature is not covered is **logged, not silently
no-op'd** — the boot log names the module and the missing feature
key so ops can see what was skipped. `AddForEdition<TService>(paid:
Features.X, use: typeof(PaidImpl), otherwise: typeof(CommunityImpl))`
swaps one interface's implementation by current edition at
composition time; Community always gets a real, working stub
implementation, never a null / throwing one — the "one codebase,
no data loss" open-core decision requires Community to compose
even with zero paid services registered.

**3c. Background workers** —
`[RequiresFeature(nameof(Features.X))]` on an `IComukiWorker`
implementation, read by `ComukiWorkerRegistry`
(`platform/src/shared/Comuki.Shared.Bootstrap/Workers/ComukiWorkerRegistry.cs:22`)
when the worker is registered. A gated worker is **not
registered** (and therefore not started) when the license does
not cover it — rather than registering and having every
`ExecuteAsync` early-return. Hot-reload applies: if a license
upgrade covers a previously-skipped worker, the next IOptionsMonitor
change re-runs the gate and `AddComukiWorkers` (or its equivalent)
re-evaluates; workers that now pass are added to the registry.
This is the **hosted-service** worker abstraction — not the
ephemeral coding-agent worker from `worker-runtime`.

**3d. Dashboard / CLI feature visibility** —
`GET /api/v1/edition` returns the current tier (`community`,
`team`, …), license status (`valid` / `grace` / `expired` /
`absent`), the set of feature keys visible at the caller's tier,
and the limits (current + cap). The dashboard's `useFeature()`
hook and `<FeatureGate feature={Features.X} fallback={<Upsell/>}>`
component are contract-only descriptions in the proposal — their
bodies are follow-up changes. The CLI's `editions show` /
`features check` helpers follow the same pattern as the kubb-
generated `dashboard/src/shared/api/_generated/**` types, which
already flow automatically through the OpenAPI operation once the
endpoint exists.

### 4. Grace period and read-only degrade

A `Host:License:GracePeriod` `TimeSpan` (default candidate: 14
days) controls how long after `expiry` paid features keep
working with a visible warning. While in grace, `IEdition.Status`
returns `grace` and `/api/v1/edition` exposes
`status: grace`; the gate still passes; the warning surfaces
through the same channel. After grace, `Status` becomes
`expired` and the gate flips to **read-only degrade** (E8):
paid-gated *write* paths return `edition.feature_unavailable`;
paid-gated *read / export* paths for data the customer already
owns keep working, at minimum for export. A downgrade to a tier
that no longer covers data the customer has (e.g. multi-project
under a trial then back to Community) follows the same shape —
the gate checks the registry's `MinimumRank`, not the license
status alone, so a Community downgrade after a multi-project
trial refuses new projects but lets existing ones export. **No
data is deleted by the gate; refusal to boot is never the
answer.**

### 5. Architecture tests (E10)

Three invariants in `Comuki.Architecture.Tests`, each named:

1. **`EditionsRegistryContainsEveryGateKeyShould`** —
   reflection-scans every `[RequiresFeature]` /
   `[EnforceLimit]` / `[EditionFeature]` attribute across the
   compiled assemblies; asserts every `featureKey` / `limitKey`
   exists in the registry's `Features` / `Limits`. The
   recommended attribute constructor takes the smart-typed
   `Feature` / `Limit` instance (`Feature.Define(...)`) rather
   than a bare string — see Open Question OQ1 for the C#
   attribute-argument constraint status; `nameof(Features.X)`
   is the fallback if a constructor cannot be made generic over
   the smart type, with the cost that a typo at the call site is
   a runtime gap rather than a compile error.
2. **`EveryPaidRegistryEntryIsGatedShould`** — walks the
   registry; asserts every `Feature` with `MinimumRank > 0`
   has at least one gate call site (endpoint attribute, module
   installer, or worker check). This is the reverse direction of
   invariant 1, and it is the point of the registry — without
   this check, a paid feature could sit in the registry unused,
   and a paying customer would have paid for a key that does
   nothing.
3. **`CommunityEditionComposesCleanlyShould`** — extends
   `tests/unit/Comuki.Host.Unit.DiComposition/HostServiceProviderShould.cs:19`
   with one case: build the full host service provider with a
   Community (absent) license, force
   `ValidateOnBuild = true` / `ValidateScopes = true`, and
   assert it validates. The whole point of "one codebase, one
   binary" is that Community composes with zero paid services
   registered — not crash resolving one that assumed a license.

## Cross-cutting decisions (cross-reference only)

- **Version identity** (`add-worker-commit-attribution`'s
  `Generated-by: Comuki vX.Y.Z` trailer) is read from the same
  `ComukiBuildInformation`
  (`platform/src/shared/Comuki.Shared.Bootstrap/Versioning/ComukiBuildInformation.cs:17`)
  this change uses for the `/api/v1/edition` `version` field.
  Both changes read the version the same way — no re-specification
  here.
- **`comuki doctor`** (`ProductionSecretValidator` /
  `ProductionSecretAudit` at
  `platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs:24`)
  gets one new audit finding: `license.validity` with
  `ok` / `warn` / `fail` semantics. `warn` fires on `grace` or
  `expiry-within-grace`; `fail` fires on `expired` past grace
  (the gate still serves in read-only mode — `doctor` is
  advisory, not a boot refusal, matching the existing
  audit-finding pattern).
- **The dashboard / CLI consumer** of `/api/v1/edition` is a
  follow-up change. This change owns the endpoint contract and the
  generated TypeScript type; the dashboard's `useFeature()` /
  `<FeatureGate>` and the CLI's `editions show` are listed as
  contract-only items in `proposal.md`'s Non-goals and in
  `tasks.md` workstream 6 as documentation-only task items, not
  code task items.

## Open questions

- **OQ1 — Attribute constructor over `Feature` / `Limit` smart
  types, not bare strings.** C# attribute arguments must be
  compile-time constants; a `Feature` instance built by
  `Feature.Define(...)` at runtime is not. Two options:
  (a) `[RequiresFeature(nameof(Features.X))]` (the brief's
  recommendation as the fallback) — typo at the call site is a
  runtime gap caught only by the architecture test, and only when
  the assembly is scanned;
  (b) a generic-typed attribute with a static method that
  captures the smart type via a source generator — compile-time
  safe, but no source generator exists today and adding one is a
  larger undertaking than this change can carry. The brief
  allows (a) as fallback if (b) is "not achievable with C#
  attribute-argument constraints"; the implementation workstream
  picks (a) and notes it as forward-deferred to a future
  Roslyn-generator change. **Decision recorded: (a) with a code
  comment naming (b) as the future direction.**
- **OQ2 — Ed25519 crypto library.** .NET 10's
  `System.Security.Cryptography` has not historically shipped
  first-class Ed25519 (it relies on `BCryptOpenAlgorithmProvider`
  on Windows and OpenSSL on Linux); the canonical managed
  implementation is `NSec.Cryptography`
  (`Nsec.Cryptography.Ed25519.Verify`). Confirm at implement
  time whether a stable `Microsoft.IdentityModel.Tokens.EdDSA`
  or `Org.BouncyCastle.Cryptography.Signers.Ed25519PublicKeySigner`
  is more appropriate for the host's hot-reload shape. **Not
  resolved here; the implementation workstream picks and notes
  the choice in the PR description.** This is a license-shape
  decision, not a behavior decision — the rest of the design
  is independent of which library verifies the signature.
- **OQ3 — Tier-rank table and SKU naming.** The brief says
  "tier names stay open; do not hardcode Pro / Enterprise as the
  only two". The implementation workstream ships
  `Community` (rank 0) and one paid rank 1 placeholder with code
  `team` (or whatever the first paid SKU is named at issue-time);
  higher ranks (2, 3, …) are added by appending `Feature.Define`
  rows with `MinimumRank = N`. The SKU-name policy itself is a
  product decision, not a spec decision — captured as a
  follow-up under the v1 SKU-launch work.

## Migration plan (no schema change; this change is additive)

1. Add `Comuki.Shared.Editions` to the solution; add `Features` /
   `Limits` initial entries (`Community` rank 0; the first paid
   rank, currently `team`, with the initial feature rows from
   E11).
2. Add `tools/Comuki.Codegen.Editions` CLI; wire it into the
   build to emit both the Markdown table at
   `openspec/specs/editions/capability-matrix.md` and the TS
   contract at the dashboard's generated path.
3. Add `LicenseOptions` + `LicenseOptionsValidator` (cross-field:
   "path set but unreadable" → boot fail). Add
   `ILicenseProvider` + `Ed25519LicenseProvider`. Add the
   hot-reload `IOptionsMonitor<LicenseOptions>` binding.
4. Add `[RequiresFeature]` / `[EnforceLimit]` attributes, the
   MVC filter and the minimal-API middleware, the
   `EditionGate.EvaluateAsync` shared decision function, and the
   `AddComukiModule<TModule>` / `AddForEdition<TService>` DI
   helpers.
5. Add the `[RequiresFeature]` read by `ComukiWorkerRegistry` /
   `AddComukiWorkers`.
6. Add `GET /api/v1/edition` (host-owned route; `editions`-owned
   payload shape).
7. Add `comuki doctor` audit finding `license.validity`.
8. Add the three architecture tests.
9. Add one illustrative worked example per enforcement point
   (one paid module whose installer is gated; one paid worker
   whose registration is gated; one paid endpoint whose
   `[RequiresFeature]` denies a Community request).

Rollback: removing the `Comuki.Shared.Editions` registration from
`HostComposer` returns the platform to Community-only without any
data migration — the registry is additive, the gate is additive,
the worker-registration check is additive, and the only
behavioral change is the new `/api/v1/edition` route.

Workstreams are sized for independent MiniMax/opencode agents on disjoint
file areas — each lists the exact projects/paths it owns so two
workstreams never edit the same file. Deps are hard prerequisites (must
merge first); gates are the commands each workstream's own agent runs
before handing back. The orchestrator still runs `dotnet build
comuki.slnx -c Debug` and the full test suites across workstreams
before archiving this change. The 7-layer ordering follows the BINDING
coordinator addendum: registry + codegen → license / `IEdition` /
hot reload → API gating → DI gating → background-worker gating →
`/api/v1/edition` + FE / CLI contract → architecture tests +
fixtures.

## 1. Shared library skeleton + `Features` / `Limits` catalogs + registry

- [ ] 1.1 Create `Comuki.Shared.Editions` project under
  `platform/src/shared/`, add to `comuki.slnx` by hand, wire
  ProjectReferences (`Comuki.Shared.Kernel` for `SecretRef` /
  `ISecretResolver`, `Comuki.Shared.Bootstrap` for
  `ComukiBuildInformation`), verify `dotnet sln comuki.slnx list`
  shows it and `dotnet build platform/src/shared/Comuki.Shared.Editions/Comuki.Shared.Editions.csproj -c Debug`
  succeeds.
- [ ] 1.2 Add `EditionTier` (`record struct`, `Rank` + open `Code`),
  `Feature` / `Limit` smart-typed records built through
  `Feature.Define(...)` / `Limit.Define(...)` factories, and a
  `Features` / `Limits` static catalog with the E11 initial rows
  (enterprise identity, scale & isolation, infra memory, background
  LLM watchers, AgentEval, white-label, multi-project, and
  worker-commit-attribution); verify unit tests reject duplicate
  keys, `Feature` / `Limit` factory invariant checks, and
  `MinimumRank` ordinal sorting.
- [ ] 1.3 Add `IEditionCapabilityRegistry` (in-memory, enumerable,
  returns `Features.All` ∪ `Limits.All` sorted by `Key`); verify
  the registry's enumeration matches the catalog by an invariant
  unit test that fails when an entry exists in the catalog but not
  in the registry.

Deps: none. Files: `platform/src/shared/Comuki.Shared.Editions/**` (new),
`comuki.slnx` (append-only). Gates: `dotnet build
platform/src/shared/Comuki.Shared.Editions/**/*.csproj -c Debug`;
`dotnet run --project tests/unit/Comuki.Shared.Editions.Unit`
(new test project, created in this workstream).

## 2. Codegen CLI (Markdown table + TypeScript contract)

- [ ] 2.1 Add `tools/Comuki.Codegen.Editions/Program.cs` mirroring
  `tools/Comuki.Codegen.Realtime/Program.cs` (`--out <path>` else
  stdout, normalized `\n` line endings, deterministic output);
  reflect over `Comuki.Shared.Editions.Features` / `Limits` and
  emit two artifacts:
  (a) a Markdown capability table at
  `openspec/specs/editions/capability-matrix.md` (one row per
  entry, columns: `Key` / `Description` / `Community` / `Paid`);
  (b) a TypeScript module at
  `dashboard/src/shared/editions/_generated/registry.ts` with one
  `export const Features = { ... } as const` block and one
  `export type FeatureKey = (typeof Features)[keyof typeof Features]`
  union, plus the parallel `Limits` pair.
- [ ] 2.2 Wire the CLI as an MSBuild target in
  `src/build/Comuki.Build.Tools/Comuki.Build.Tools.targets` so a
  Debug build re-emits both artifacts when the registry changes;
  verify a Debug build produces both files byte-identically across
  Windows and Linux CI (line-ending normalization).
- [ ] 2.3 Commit the **generated** artifacts alongside the source
  change so reviewers see the diff in both the registry and the
  emitted table; verify the architecture test in workstream 7
  asserts the generated table matches the registry by re-emitting
  in CI.

Deps: 1. Files: `tools/Comuki.Codegen.Editions/**` (new),
`platform/src/shared/Comuki.Shared.Editions/Catalog/RegistryEmitter.cs`
(new, internal), `dashboard/src/shared/editions/_generated/registry.ts`
(new, generated), `openspec/specs/editions/capability-matrix.md`
(new, generated), `src/build/Comuki.Build.Tools/Comuki.Build.Tools.targets`
(additive target only). Gates: `dotnet run --project
tools/Comuki.Codegen.Editions -- --out <tmp>` then byte-compare the
two artifacts across two consecutive runs.

## 3. License parsing + `IEdition` + `ILicenseProvider` + `IOptionsMonitor` hot reload

- [ ] 3.1 Add `LicenseOptions` (modeled on `RateLimitOptions.cs:20`,
  `public const string SectionName = "Host:License"`,
  `[Required]` `Path` of type `SecretRef`) and
  `LicenseOptionsValidator : IValidateOptions<LicenseOptions>`
  (modeled on `HostTlsOptionsValidator.cs:13`, cross-field:
  "path set but `ISecretResolver.ResolveAsync` returns null →
  boot fail" — the "present-but-broken fails loud" rule from E5);
  bind through `AddOptions<LicenseOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.
- [ ] 3.2 Add `ILicenseProvider` port + `Ed25519LicenseProvider`
  implementation: parses `base64url(payload).base64url(signature)`,
  decodes payload JSON `{ org, edition, seats, limits, expiry,
  features?, mode }`, verifies Ed25519 against the embedded
  public key (assembly-embedded constant or `.resource`, never
  config — see OQ2 for the crypto library choice; record the
  decision in this workstream's PR description); throw
  `LicenseInvalidException` on bad signature / malformed payload /
  unknown tier code; return `LicenseKey` on success.
- [ ] 3.3 Expose `IEdition` (interface: `Has(Feature)`, `Limit(Limit)
  -> int`, `Current` tier, `Status` enum
  `valid` / `grace` / `expired` / `absent`) backed by
  `IOptionsMonitor<LicenseOptions>` so a replaced license file is
  picked up without restart; verify a unit test that replaces the
  license file under a `FileSystemWatcher` (or a polling
  alternative) sees the new tier within `LicenseOptions.ReloadDelay`
  (default 5s).
- [ ] 3.4 Add `comuki doctor` audit finding `license.validity`
  reading from `IEdition.Status` (`ok` / `warn` on `grace`,
  `fail` on `expired` past grace) — modeled on
  `ProductionSecretAudit` /
  `ProductionSecretValidator.cs:24`; verify a unit test that the
  finding surfaces and `ProductionSecretValidator.Validate`
  consults it.

Deps: 1, 2. Files: `platform/src/shared/Comuki.Shared.Editions/Licensing/**`
(new subfolder), `platform/src/shared/Comuki.Shared.Editions/Options/LicenseOptions.cs`,
`platform/src/shared/Comuki.Shared.Editions/Options/LicenseOptionsValidator.cs`,
`platform/src/shared/Comuki.Shared.Editions/Edition/IEdition.cs`,
`platform/src/shared/Comuki.Shared.Editions/Edition/EditionGate.cs`,
`platform/src/shared/Comuki.Shared.Editions/Edition/Ed25519LicenseProvider.cs`,
`platform/src/host/Comuki.Host/Security/ProductionSecrets/LicenseAuditFinding.cs`
(additive). Gates: `dotnet run --project
tests/unit/Comuki.Shared.Editions.Unit`; a manually-signed
`TestLicense` round-trip test that proves the verify path accepts
a known-good token and rejects a tampered one.

## 4. API gating — `[RequiresFeature]` / `[EnforceLimit]` + filter + middleware

- [ ] 4.1 Add `[RequiresFeature(Feature)]` / `[EnforceLimit(Limit)]`
  attributes in `Comuki.Shared.Editions.Gating` (modeled on
  `RequiresPermissionAttribute.cs:20`; constructor accepts the
  smart-typed `Feature` / `Limit` instance when achievable, else
  falls back to `nameof(Features.X)` per OQ1 with a code comment
  naming the future source-generator direction).
- [ ] 4.2 Add `RequiresFeatureFilter : IAsyncResourceFilter`
  (modeled on `RequiresPermissionFilter.cs:33` — same `last wins`
  metadata ordering, `LastOrDefault()` on `EndpointMetadata`),
  `RequiresFeatureMiddleware` (modeled on
  `RequiresPermissionMiddleware.cs:16`), and the shared internal
  `EditionGate.EvaluateAsync` (same shape as
  `PermissionGate.EvaluateAsync` at
  `RequiresPermissionFilter.cs:79`); both paths produce the same
  `edition.feature_unavailable` 403 ProblemDetails with the
  `code` extension carrying the dot.case code.
- [ ] 4.3 Add `.RequireFeature(Feature)` extension on
  `IEndpointConventionBuilder` (minimal-API path) that adds the
  same attribute-shaped metadata as the MVC `[RequiresFeature]`,
  so both paths read one demand type — no parallel demand shapes
  invented for MVC vs minimal API.
- [ ] 4.4 Wire the filter (via `MvcOptions.Filters.Add<...>()` or
  the existing `HostComposer` extension point) and the middleware
  (via `app.UseMiddleware<RequiresFeatureMiddleware>()`) in
  `HostComposer`; verify a Debug build emits
  `artifacts/openapi.json` with the new 403 `ProblemDetails`
  examples for any endpoint carrying `[RequiresFeature]`.
- [ ] 4.5 Add one illustrative worked example: one paid endpoint
  (any existing controller action — pick the cheapest), tagged
  with `[RequiresFeature(Features.X)]`, verified by a unit test
  that a Community request 403s with `code=edition.feature_unavailable`
  and a paid-tier request passes through to the handler.

Deps: 3. Files: `platform/src/shared/Comuki.Shared.Editions/Gating/**`
(new), `platform/src/host/Comuki.Host/Editions/**` (new folder
under host, mirroring `Identity.Infrastructure/Security/Authorization/**`'s
split; lives in host because the gate is cross-cutting like the
exception handler, not owned by one module — see
`add-multi-repo-projects/design.md` modular-monolith law). Gates:
`dotnet build platform/src/shared/Comuki.Shared.Editions/**/*.csproj -c Debug`;
full unit suite for the worked-example endpoint.

## 5. DI gating — `AddComukiModule<TModule>` + `[EditionFeature]` + `AddForEdition<TService>`

- [ ] 5.1 Add `AddComukiModule<TModule>()` generic extension in
  `Comuki.Shared.Editions.Gating` (reads class-level
  `[EditionFeature(Feature)]` on `TModule`, calls `IEdition.Has(...)`
  before invoking the module's concrete installer; on false,
  **logs at Warning with the module name and the missing feature
  key** — never silently no-ops; returns `IServiceCollection` so
  it composes with the existing "one `AddXxxModule` extension per
  module" convention of
  `ProjectsApplicationExtensions.cs:24` and `ComputeInstaller.cs`).
- [ ] 5.2 Add `AddForEdition<TService>(paid: Feature, use: Type,
  otherwise: Type)` for interface-swap by current edition at
  composition time; Community always gets a real, working stub
  implementation, never null / throwing.
- [ ] 5.3 Pick one existing module installer (the cheapest one)
  and update **only its installer file** to demonstrate the
  `AddComukiModule<TModule>` shape (e.g. wrap an existing
  `AddXxxModule(...)` call in `AddComukiModule<XxxModule>()` with a
  class-level `[EditionFeature(Features.X)]`); verify a unit test
  that building the DI graph with Community skips the inner
  installer and a paid tier runs it.
- [ ] 5.4 Pick one existing paid/Community interface split (or
  invent a minimal one in the test project if no real split
  exists) and demonstrate `AddForEdition<TService>`; verify the
  Community DI graph resolves the Community impl and the paid DI
  graph resolves the paid impl.

Deps: 3, 4. Files: `platform/src/shared/Comuki.Shared.Editions/Gating/AddComukiModule.cs`,
`platform/src/shared/Comuki.Shared.Editions/Gating/AddForEdition.cs`,
`platform/src/shared/Comuki.Shared.Editions/Gating/EditionFeatureAttribute.cs`,
plus one illustrative module-installer update (single file, additive
only). Gates: `dotnet build platform/src/shared/Comuki.Shared.Editions/**/*.csproj -c Debug`;
unit tests for both wrappers.

## 6. Background-worker gating — `[RequiresFeature]` on `IComukiWorker`

- [ ] 6.1 Add `[RequiresFeature(Feature)]` support to
  `ComukiWorkerRegistry`
  (`platform/src/shared/Comuki.Shared.Bootstrap/Workers/ComukiWorkerRegistry.cs:22`):
  when registering a worker, read the attribute; if the
  attribute is present and the current edition does not cover
  the feature, log a `Warning` naming the worker and the missing
  feature key, and **do not add the worker to the registry**
  (so it is not started by the per-worker loop); verify hot
  reload — replace the license file with a paid tier; the
  registry re-evaluates and adds the previously-skipped worker.
- [ ] 6.2 Add one illustrative worked example: pick one existing
  `IComukiWorker` (the cheapest one), tag it with
  `[RequiresFeature(Features.X)]`, verify a Community unit test
  that the registry's `Snapshot()` does not include it and a
  paid-tier test that it does.

Deps: 3, 5. Files:
`platform/src/shared/Comuki.Shared.Bootstrap/Workers/ComukiWorkerRegistry.cs`
(additive only — checks the attribute and gates
`runtimes[name]` insertion), one illustrative worker file. Gates:
unit tests for the worked example; `dotnet run --project
tests/unit/Comuki.Shared.Bootstrap.Unit.Workers` (existing test
project).

## 7. `/api/v1/edition` endpoint + FE / CLI contract

- [ ] 7.1 Add `GET /api/v1/edition` in
  `platform/src/host/Comuki.Host/Editions/EditionsController.cs`
  (or minimal-API equivalent — pick MVC for symmetry with the
  other host endpoints; route constant goes in `ApiRoutes`); the
  response body is `{ tier: string, status: 'valid' | 'grace' |
  'expired' | 'absent', features: { key: string, available: bool
  }[], limits: { key: string, current: number, cap: number }[],
  version: string, expiresAt?: string }` where `version` comes
  from `ComukiBuildInformation` (cross-reference
  `add-worker-commit-attribution` reads the same way); verify
  the OpenAPI operation is in `artifacts/openapi.json` after a
  Debug build.
- [ ] 7.2 Add the contract-only FE / CLI hook descriptions in
  `dashboard/src/shared/editions/hooks.ts` (TypeScript:
  `useFeature(FeatureKey): boolean`, `<FeatureGate feature=
  FeatureKey fallback=ReactNode>: ReactNode` — both thin
  wrappers over the generated registry + `/api/v1/edition`
  fetch) and `cli/src/contracts/_generated/editions.ts` (a
  placeholder module that re-exports the kubb-generated types
  once the endpoint is in the OpenAPI schema); **both are
  contract-only documentation in this change** — their bodies
  are a follow-up change per `proposal.md`'s Non-goals; verify a
  build that imports the placeholders compiles (the contract file
  may be a single typed re-export).

Deps: 3, 4. Files: `platform/src/host/Comuki.Host/Editions/**` (new
subfolder), `dashboard/src/shared/editions/hooks.ts` (new,
contract-only), `cli/src/contracts/_generated/editions.ts` (new,
contract-only), `platform/src/shared/Comuki.Shared.Contracts/ApiRoutes.cs`
(additive constant). Gates: `dotnet build
platform/src/host/Comuki.Host/Comuki.Host.csproj -c Debug`;
`artifacts/openapi.json` includes the new path.

## 8. Architecture tests + fixture licenses

- [ ] 8.1 Extend `Comuki.Architecture.Tests` with three new test
  classes:
  - `EditionsRegistryContainsEveryGateKeyShould` — reflection-
    scans every `[RequiresFeature]` / `[EnforceLimit]` /
    `[EditionFeature]` attribute across the compiled
    assemblies; asserts every key referenced exists in
    `Features` / `Limits` (the
    `Permissions.cs:8`-documented invariant in reverse).
  - `EveryPaidRegistryEntryIsGatedShould` — walks the registry;
    asserts every `Feature` with `MinimumRank > 0` has at least
    one gate call site (the reverse direction; without this,
    a paid feature could sit in the registry unused).
  - `CommunityEditionComposesCleanlyShould` — extends
    `tests/unit/Comuki.Host.Unit.DiComposition/HostServiceProviderShould.cs:19`
    with one case: build the full host service provider with a
    Community (absent) license, force
    `ValidateOnBuild = true` / `ValidateScopes = true`, assert
    it validates — the whole point of "one codebase, one
    binary" is that Community composes with zero paid services
    registered.
- [ ] 8.2 Add `TestLicenseBuilder` in `tests/unit/Comuki.Shared.Editions.Unit`
  (test-only keypair, fixtures `TestLicense.Community` /
  `TestLicense.With(Features.X)`); the production embedded
  public key is **never** used in tests; verify the builder
  signs a known-good token that `Ed25519LicenseProvider`
  accepts and a tampered one it rejects.
- [ ] 8.3 Unit tests for the gate: `EnforceAsync` throws on
  Community request for a paid feature, passes on a paid
  request, returns the typed exception with `Code =
  "edition.feature_unavailable"` and the response extension
  carries the feature key + minimum tier.
- [ ] 8.4 Unit tests for grace / read-only-degrade:
  `IEdition.Status = grace` keeps the gate open and surfaces
  the warning via `/api/v1/edition`'s `status: grace` field;
  `Status = expired` past grace flips the gate to read-only
  degrade (write paths throw, read / export paths for data the
  customer already owns pass through).
- [ ] 8.5 Integration test for `/api/v1/edition` (WebApplicationFactory
  + a `TestLicense` mounted via `Host:License:Path`); verify the
  response shape and that 403 / 200 ProblemDetails are
  byte-correct.

Deps: 1–7. Files: `tests/unit/Comuki.Shared.Editions.Unit/**` (new
unit project), `tests/unit/Comuki.Architecture.Tests/**` (extend),
`tests/unit/Comuki.Host.Unit.DiComposition/HostServiceProviderShould.cs`
(extend), `tests/integration/Comuki.Host.Integration.Editions/**`
(new). Gates: `dotnet run --project
tests/unit/Comuki.Shared.Editions.Unit`; `dotnet run --project
tests/unit/Comuki.Architecture.Tests`; the extended
`tests/unit/Comuki.Host.Unit.DiComposition`; full
`dotnet build comuki.slnx -c Debug` (warnings-as-errors + format
gate, per `process/build-verification.md`).

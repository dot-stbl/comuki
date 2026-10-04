## ADDED Requirements

### Requirement: Seven new feature keys for the orchestra capability

The capability registry SHALL add seven new feature keys to `platform/src/shared/Comuki.Shared.Editions/Catalog/Features.cs`:

| Key | Description | Tier |
|---|---|---|
| `steering` | Live-session steering of in-flight runs (`session` capability) | paid (`team`) |
| `critic` | Self-observation: scheduled sweep over logs/metrics + MCP read tools (`observability` capability) | paid (`team`) |
| `verification` | Run verification axis: gate providers, evidence, derived view (`verification` capability) | paid (`team`) |
| `automation` | Automation read-model object: trigger / action / history (`automation` capability) | paid (`team`) |
| `model-control` | Mutable virtual keys and live model propagation (`model-control` capability) | paid (`team`) |
| `scope-layers` | Four-level precedence resolver for project settings (`scope-layers` capability) | community |
| `harness-spi` | `IHarness` abstraction, second-class-to-first-class (`harness-spi` capability) | community |

The `Limits` static catalog is unchanged for this capability. The codegen at `tools/Comuki.Codegen.Editions` regenerates `openspec/specs/editions/capability-matrix.md` and `dashboard/src/shared/editions/_generated/registry.ts` on every Debug build.

#### Scenario: Codegen refreshes the matrix

- **WHEN** `dotnet build comuki.slnx -c Debug` runs after the seven rows are added
- **THEN** `openspec/specs/editions/capability-matrix.md` lists the seven new rows under the `Features` table with the right Community / Paid (or community-only) marker

#### Scenario: Hand-edit is overwritten on the next build

- **WHEN** an operator hand-edits `openspec/specs/editions/capability-matrix.md` to add a row
- **THEN** the next Debug build's codegen overwrites the hand-edit with the registry's authoritative contents

### Requirement: First paid-feature call-sites land with the orchestra phases

Each of the five paid keys SHALL have at least one `[RequiresFeature]` / `[EditionFeature]` / `[EnforceLimit]` call-site when its owning phase ships:

- `steering` — `[RequiresFeature(Features.Steering)]` on `POST /api/v1/runs/{runId}/steer` (`session` capability, Phase 1).
- `critic` — `[RequiresFeature(Features.Critic)]` on `CriticSweepWorker` (`observability` capability, Phase 4). The first call-site for the new `critic` key lands in the same phase that introduces the key (Phase 4 / Critic-sweep). **Placement:** the worker lives in `Comuki.Host` (one of the three assemblies the `EveryPaidRegistryEntryIsGatedShould` architecture test indexes — see that test's `indexedAssemblyNames`); if it lands anywhere else, the test's `NoGateAttributeLivesOutsideTheIndex` guard fails the build.
- `verification` — `[RequiresFeature(Features.Verification)]` on the gate-provider registration host handler (`verification` capability, Phase 3).
- `automation` — `[EditionFeature(Features.Automation)]` on the automation module installer (`automation` capability, Phase 6).
- `model-control` — `[RequiresFeature(Features.ModelControl)]` on `PATCH /api/v1/proxy/keys/{keyId}` (`model-control` capability, Phase 7).

The architecture test `EveryPaidRegistryEntryIsGatedShould` SHALL pass with the first call-sites in place. A registry entry that lands without a gate fails the build with a typed message naming the orphan. The companion guard `NoGateAttributeLivesOutsideTheIndex` enumerates every `Comuki.*.dll` on disk and refuses any that carries a gate attribute outside the three-assembly index — a gate attribute living in an unindexed assembly would silently satisfy the inverse assertion without the test ever observing the rogue key.

#### Scenario: Each paid key has its gate

- **WHEN** Phase 1 ships `Steering`
- **THEN** the architecture test enumerates the five paid orchestra keys (plus the existing eight) and reports the gate for each

#### Scenario: Adding a paid key without a gate fails the build

- **WHEN** an operator adds `Feature.Define("orphan:no-gate", "An orphan", minimumRank: 1)` without an attribute
- **THEN** `EveryPaidRegistryEntryIsGatedShould` fails the build with a message naming the orphan

#### Scenario: Gate attribute living in an unindexed assembly fails the build

- **WHEN** an operator adds `[RequiresFeature(Features.Critic)]` to a type in an assembly outside the `Comuki.Host` / `Comuki.Shared.Editions` / `Comuki.Shared.Contracts` index
- **THEN** `NoGateAttributeLivesOutsideTheIndex` fails the build with the offending assembly name in the message — the `CriticSweepWorker` call-site MUST therefore land in `Comuki.Host` (the canonical home of hosted-service workers)

### Requirement: Community keys are gated nowhere by design

The two Community keys (`scope-layers`, `harness-spi`) SHALL NOT have `[RequiresFeature]` / `[EnforceLimit]` / `[EditionFeature]` call-sites — they are Community features, available to every tier. The architecture test `EveryPaidRegistryEntryIsGatedShould` accepts Community entries without gates; the test `CommunityEditionComposesCleanlyShould` validates that Community composes with these features registered.

#### Scenario: Community keys compose in Community

- **WHEN** a Community deployment boots
- **THEN** the `scope-layers` and `harness-spi` features are available and the host's DI graph validates with `ValidateOnBuild = true`

#### Scenario: Paid keys are denied in Community

- **WHEN** a Community deployment reaches the steering endpoint
- **THEN** the response is `403` with `code = edition.feature_unavailable` and `featureKey = steering`

### Requirement: Editions limits and the scope-layers axis are independent

`Limits.Projects = 1` (Community) and the `scope-layers` feature key SHALL NOT cross-reference. A Community deployment with `scope-layers` enabled resolves the four-layer precedence chain exactly as a paid deployment does; only the *limit* on project creation differs. The architecture tests do not cross-reference the two axes; the docs record the independence.

#### Scenario: Limit unaffected by scope layers

- **WHEN** a Community deployment reads the orchestra/section/worker chain for an existing project
- **THEN** the precedence resolver returns the same result as a paid deployment for the same configuration

#### Scenario: Scope layers do not lift limits

- **WHEN** a Community deployment with `scope-layers` enabled tries to create a second project
- **THEN** the response is `403 edition.limit_exceeded` with `limitKey = projects` regardless of the orchestra/section/worker values
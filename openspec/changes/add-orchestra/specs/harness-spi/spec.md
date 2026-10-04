# Harness SPI Specification

## Purpose

Defines `IHarness`, the second-class-to-first-class abstraction that replaces `IPiRunner`. Pi is the first implementation; `Comuki.TestFakePi` (today a swap-in replacement for `PiExecutable` in tests) is the second. The SPI declares event schema, env preparation, and capability advertisement (including `LiveSession`, the consumer in Phase 1 / Baton).

Today the harness is smeared across ~10 places: `worker.Dockerfile`, `TranslatorOptions.PiExecutable`, `IPiRunner`/`PiRunner`, the `PiEvent` parser, `PiCodingAgentDirectory`/`PiEnvironment`, the `comuki-worker-sdk` pi-extensions, `agent-core/events/pi.ts`, `MintedKeyUpstream` "anthropic"-shape, and so on. The SPI consolidates them under a single abstraction.

This phase ships *with* cowork 11.1 (slot / execution identity). An SPI declared before the contract lands is a contract that will move.

## ADDED Requirements

### Requirement: IHarness is the abstraction

```csharp
public interface IHarness
{
    string Name { get; }                                          // "pi" | "test-fake-pi" | ...
    HarnessCapabilities Capabilities { get; }                    // LiveSession, ... (record struct)
    IReadOnlyDictionary<string, string> ResolveEnv(              // Project + Profile → env
        Project project, ProfileKey profile, CancellationToken ct);
    HarnessEvent ParseEvent(string streamJsonLine);             // tolerant: returns Unknown for unknown kinds
    Task<HarnessProcess> StartAsync(                            // ProcessStartInfo + env + cwd
        HarnessStartRequest request, CancellationToken ct);
}
```

The Translator consumes `IHarness` only. `IPiRunner` is replaced by `IHarness` in the host composition root; the public Translator surface exposes the harness abstraction. `IPiRunner` is deleted from the public surface (existing `PiRunner` becomes `PiHarness`).

#### Scenario: PiHarness is the first implementation

- **WHEN** the host composes with the harness abstraction
- **THEN** `PiHarness` is registered with `Name = "pi"`, `Capabilities.LiveSession = true`, and the existing pi spawning path runs through `IHarness.StartAsync`

#### Scenario: TestFakeHarness is the second implementation

- **WHEN** the host composes with the harness abstraction in tests
- **THEN** `TestFakeHarness` (lifted from `Comuki.TestFakePi`) is registered with `Name = "test-fake-pi"`, `Capabilities.LiveSession = false`, and the test fake's stream-json emission runs through `IHarness.ParseEvent`

#### Scenario: Harness catalog populates from DI

- **WHEN** the host boots
- **THEN** `IHarnessCatalog` enumerates every `IHarness` implementation registered in DI; the catalog is the source of truth for the dashboard's harness picker

### Requirement: Capabilities is a record struct

`HarnessCapabilities` is a `readonly record struct` carrying `LiveSession` (the only field for now; later phases may add `StreamingToolResults`, `ResumeAcrossRestarts`, etc.). The Translator reads the value at worker start and chooses the spawn strategy. A harness that declares `LiveSession = false` cannot receive `TurnInput` as authoritative; the steering endpoint refuses the request with `code = session.livesession_unavailable`.

#### Scenario: LiveSession is the single source of truth

- **WHEN** the Translator starts a worker and the harness declares `LiveSession = true`
- **THEN** the bidi command channel is open for `TurnInput`

#### Scenario: LiveSession is the only field relevant to steering

- **WHEN** the steering endpoint decides whether `TurnInput` is authoritative
- **THEN** the decision is `Capabilities.LiveSession` alone; no other field is consulted

### Requirement: ResolveEnv and ParseEvent have deterministic shapes

`ResolveEnv` is pure (no I/O, no DI side effects beyond the project and profile) and returns the env dictionary that `HarnessStartRequest` consumes. `ParseEvent` is tolerant: blank lines yield `unknown`; malformed JSON yields `unknown` with a `reason: "unparseable"`; unmodelled event kinds yield `unknown` with `reason: "unknown_kind"`. A single bad line never kills the running session.

#### Scenario: ResolveEnv is pure

- **WHEN** the Translator resolves env for `(project, profile)` twice
- **THEN** the two dictionaries are equal (no I/O); the dictionary is the same shape every worker would see

#### Scenario: ParseEvent survives garbage

- **WHEN** the harness emits a non-JSON line mid-stream
- **THEN** `ParseEvent` returns `unknown` with `reason: "unparseable"` and the worker session continues

### Requirement: Harness catalog is the picker source

`IHarnessCatalog` exposes `IReadOnlyList<IHarness>` populated at host boot from the DI container. The dashboard's project-settings-drawer harness picker renders the catalog's names; the picker writes the harness id to `ProjectSettings.HarnessId` (a new field on `ProjectSettings`, optional, default = `"pi"`).

#### Scenario: Picker defaults to pi

- **WHEN** a project has no explicit `HarnessId` and the worker claims an item
- **THEN** the claim uses `pi`

#### Scenario: Picker selects the test fake

- **WHEN** an operator sets `ProjectSettings.HarnessId = "test-fake-pi"`
- **THEN** the claim matches against `test-fake-pi` workers only (the existing claim-matching engine reads the harness label the same way it reads `envClass`)

### Requirement: Profile declares its harness

`HarnessProfile` is a control-plane marker: `harness: pi` or `harness: test-fake-pi`. The profile lives under `control-plane/profiles/<name>.md`'s `harness:` frontmatter. The claim-matching engine reads the harness label through the same path as `envClass` (the existing label-sanitisation rule).

#### Scenario: Profile with explicit harness

- **WHEN** a profile has `harness: test-fake-pi` in its frontmatter
- **THEN** claims with that profile match against `test-fake-pi` workers and not against `pi` workers

#### Scenario: Profile without harness frontmatter

- **WHEN** a profile has no `harness:` frontmatter
- **THEN** the claim defaults to `pi`

### Requirement: SlotHandle is the cowork 11.1 contract

`IHarness.StartAsync` SHALL accept a `SlotHandle` parameter — the cowork 11.1 slot / execution identity contract. When 11.1 ships, the slot handle is the canonical source of slot / execution identity; before 11.1, a stub `SlotHandle` carries the v1.x `WorkerId` and the SPI lands in a slot-agnostic shape. The stub migration is filed as a follow-up; the SPI does not pre-empt cowork.

#### Scenario: SlotHandle is the parameter

- **WHEN** the Translator starts a worker through `IHarness.StartAsync`
- **THEN** the request carries a `SlotHandle` parameter; v1.x passes a stub carrying `WorkerId`; v2 passes the real slot

#### Scenario: Cowork 11.1 slips

- **WHEN** cowork 11.1 lands after this phase
- **THEN** the `SlotHandle` stub is replaced by the real slot handle; the SPI shape does not change

## ADAPTER Notes

`Comuki.TestFakePi` (test fake) is the documented precedent for the second harness implementation; the capability ships it as a proper `IHarness` instead of a `PiExecutable` swap-in. `worker.Dockerfile` references pi by default; the harness picker in `ProjectSettings.HarnessId` overrides the default. `comuki-worker-sdk` pi-extensions move with `PiHarness`; the SPI does not redesign the SDK, it consumes the existing surface.

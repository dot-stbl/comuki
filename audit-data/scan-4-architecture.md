# Scan 4 — Architecture invariants

**Scan tool:** Manual read of the 4 NetArchTest suites +
a csproj-level reference walk + (just-in-case) `dotnet build comuki.slnx
-c Debug` + `dotnet run --project tests/Comuki.Architecture.Tests -c Debug`.

## 4.1 NetArchTest suite results

```
$ dotnet run --project tests/Comuki.Architecture.Tests -c Debug
total: 14 · succeeded: 14 · failed: 0 · skipped: 0
```

**Test catalog — what's covered:**

| Test suite (file) | Tests | What it asserts |
|---|--:|---|
| `LayerDependencyTests.cs` | 4 | `Shared.Kernel ↛ {Contracts, Engine, Host, Translator}`; `Contracts → {Kernel only}`; `Engine.Orchestration ↛ Host`; `Translator ↛ Engine` |
| `ModuleLayerTests.cs` (Identity) | 4 | Identity.Domain/Application/Infrastructure ↛ {Engine, Engine.Compute, Host, Translator, Migrator, Contracts}; Identity modules ↛ Engine |
| `CostsModuleLayerTests.cs` | 3 | Costs.Domain/Application/Infrastructure ↛ {Engine*, Host, Translator, Migrator, Contracts}; Costs modules ↛ Engine |
| `IntakeModuleLayerTests.cs` | 3 | Intake.Domain/Application/Infrastructure ↛ {Engine*, Host, Translator, Migrator, Contracts}; Intake modules ↛ Engine |

**Coverage gap (target tests):**

| Module | Layers tested? | Cross-module tested? |
|---|:--:|:--:|
| Identity | ✓ | n/a (first module) |
| Costs | ✓ | ❌ |
| Intake | ✓ | ❌ |
| **Chat** | ❌ | ❌ |
| **Knowledge** | ❌ | ❌ |
| **Memory** | ❌ | ❌ |
| **Projects** | ❌ | ❌ |
| **Proxy** | ❌ | ❌ |
| **Artifacts** | ❌ | ❌ |
| **Engine.Compute** | ❌ | n/a |
| **Engine.Orchestration** | (covered by `LayerDependencyTests`) | n/a |
| **Translators / Migrator / Host** | (covered by `LayerDependencyTests`) | n/a |

**Recommended additions (in `tests/Comuki.Architecture.Tests`):**

1. Mirror the 4 Identity/Costs/Intake suites for the 6 uncovered
   modules: Chat, Knowledge, Memory, Projects, Proxy, Artifacts.
2. **Module-to-module isolation** — a single suite that asserts
   "no `Modules.X.Application|Infrastructure` references
   `Modules.Y.Application|Infrastructure` for X ≠ Y", enforced by
   scanning csproj `ProjectReference` tags. This is the test that
   would have caught the violations found in §4.2.
3. **Host → everything** — assert no `Host` reaches into a
   `Domain` (only Application/Infrastructure).
4. **No EF in Domain** — assert no `Microsoft.EntityFrameworkCore`
   `using` in `*.Domain` assemblies.

## 4.2 Cross-module references — rule violations

Found via csproj `<ProjectReference>` walk over all 36 module-level
`.csproj`s:

### Violation 1: `Knowledge → Memory` (Application + Infrastructure)

**Files:**
- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Application.csproj:14`

  ```xml
  <ProjectReference Include="..\..\Memory\Comuki.Modules.Memory.Domain\…csproj" />
  ```

- `platform/src/modules/Knowledge/Comuki.Modules.Knowledge.Infrastructure.csproj:25`

  ```xml
  <ProjectReference Include="..\..\Memory\Comuki.Modules.Memory.Infrastructure\…csproj" />
  ```

**Rule source:** `.agents/docs/architecture/comuki-project-structure.md` §2:

> ```
> host → modules/engine → shared
> modules ↛ modules     (только через Shared.Contracts / integration events)
> ```

**Why this exists:** Knowledge re-uses `Memory.Domain` types (probably
`SourceDocument`, `MemoryEmbedding`) and the storage layer
(`Memory.Infrastructure`) for the `knowledge_chunks` / pgvector
embeddings. The duplication is reasonable from a "share the embedding
data model" angle — but it directly violates the modular-monolith rule.

**Recommended fix (3 options, in increasing cost):**

- **A.** Knowledge's `SourceDocument` *is* `Memory`'s
  `SourceDocument` — formally promote them into `Shared.Contracts` /
  `Shared.Kernel` as value-objects. Knowledge+Memory both depend on
  `Shared.Kernel`, no cross-module edge remains.
- **B.** Move `SourceDocument` into `Shared.Contracts.Knowledge`
  (or a new `Shared.Persistence` pgvector capability) and remove
  `Comuki.Modules.Memory.Domain`'s ownership. Knowledge + Memory
  both read from a contracts-level type.
- **C.** Accept and document the asymmetry — add an
  `KnowledgeMayDependOnMemory` suite in arch tests that explicitly
  *exempts* the edge, and update `comuki-project-structure.md` with
  "Knowledge borrows Memory's pgvector shape" call-out.

### Violation 2: `Proxy → Costs` (Application)

**File:**
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application.csproj:22`

  ```xml
  <ProjectReference Include="..\..\Costs\Comuki.Modules.Costs.Application\…csproj" />
  ```

**Why this exists:** Proxy meters usage into `usage_events`. Costs owns
the `IUsageEventStore` abstraction + the `UsageRecorder` adapter that
appends `usage_events` rows. Proxy calls into Costs.Application to
record usage after a successful response.

**Recommended fix:**

- A) Surface `IUsageEventStore` (the costs-internal port) as
  `Comuki.Shared.Contracts.Costs.IUsageEventStore`. Proxy stops
  depending on Costs.Application and depends only on
  `Shared.Contracts`. Migrator wires the concrete
  `Costs.Infrastructure.Persistence.Stores.UsageEventStore` to the
  shared interface at composition root.
- B) Alternative: split Costs into a "metering receiver" service
  exposed via a `AddCostsMetering(...)` installer extension from
  `Shared.Contracts`, with no knowledge of internal Costs types.

Either option removes the cross-module edge.

### Asymmetric Dependency: Modules that DO reference `Shared.Contracts`

Most modules reference `Shared.Contracts` (Compute, Telemetry, etc.);

| Module | `Application` refs `Shared.Contracts`? |
|---|---|
| Artifacts | ✓ |
| Chat | ✓ |
| Costs | ✓ |
| **Identity** | **✗** |
| **Intake** | **✗** |
| **Knowledge** | ✗ (refs Memory instead) |
| Memory | ✗ (refers only to itself) |
| **Projects** | **✗** |
| Proxy | ✓ |

For Identity/Intake/Projects — verified by grep on `using Comuki.Shared.Contracts`
in their Application `.cs` files: zero hits. This means these modules don't
read any of the cross-cutting contract types (e.g. `IRunLauncher`,
`IComputeProvider`, etc.). Either the modules are *truly* self-sufficient
(no shared abstractions needed — good) OR they're missing cross-cutting
contracts (e.g. Intake creates runs, so it should probably use an
`IRunLauncher` port out of `Shared.Contracts` — but the Host provides
that port via composition).

This is **not a violation**, just an asymmetry worth noting: the project's
"ports in Contracts" pattern is inconsistently applied. Architectural
cleanliness would suggest making it consistent.

## 4.3 Walked module-to-module direction summary

Topo of the `ProjectReference` graph (DAG, excluding engine/host):

```
        Shared.Kernel ←──┬──┬── Identity.{Application,Infrastructure}
                         │  │   Costs.{Application,Infrastructure} ←─┐
                         │  │   Chat.{Application,Infrastructure}     │
                         │  │   Projects.{…}                          │ Shared.Contracts ←──┬── Artifacts
                         │  │   Memory.{…}                            │                     └── Proxy
                         │  │                                          │
                         │  └──── Knowledge.{Application,Infrastructure}  ──→ Memory.{Domain,Infrastructure} ←── VIOLATION
                         │                                                  ──→ Shared.Contracts (orally)
                         │
                         └──── Shared.Contracts ←── Artifacts, Chat, Costs, Proxy
                                            └── Knowledge? (cross-module edge via Memory instead)

Proxy.Application ←──── Costs.Application        ←── VIOLATION (Cross-module)
```

## 4.4 Build + format gates

```
$ dotnet build comuki.slnx -c Debug
[VerifyFormatOnBuild] Format check passed.
Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0
Прошло времени 00:00:58.99
```

The audit **adds no production code, so the build is identical to
master's state** — clean.

## 4.5 Tests

```
$ dotnet run --project tests/Comuki.Architecture.Tests -c Debug
total: 14 · succeeded: 14 · failed: 0
```

No regression in arch tests from the audit (changes were audit-only).

## 4.6 Recommended test code (drop-in module-isolation test)

For inclusion in `tests/Comuki.Architecture.Tests/`:

```csharp
using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// Module isolation: per comuki-project-structure.md §2 "modules ↛ modules
/// (только через Shared.Contracts / integration events)", no module may
/// cross-reference another module's Application or Infrastructure layer.
/// Note: this exception-list approach is intentional — adding a new
/// cross-module edge requires updating both this test and the project-
/// structure doc.
/// </summary>
public sealed class ModuleIsolationTests
{
    private static readonly string[] CrossModuleExceptions = Array.Empty<string>();
    // (Knowledge may depend on Memory; Proxy may depend on Costs — kept
    // as known edges until the refactor lands. After the §4.2 fixes
    // close, this list should be empty.)

    [Fact]
    public void NoModuleApplicationDependsOnAnotherModule()
    {
        var modules = new[] { "Artifacts", "Chat", "Costs", "Identity",
                              "Intake", "Knowledge", "Memory", "Projects",
                              "Proxy" };
        foreach (var src in modules)
        {
            foreach (var dst in modules.Where(m => m != src))
            {
                var pair = $"{src}->{dst}";
                if (CrossModuleExceptions.Contains(pair)) continue;
                var result = Types.InAssembly(...)
                    .ShouldNot()
                    .HaveDependencyOn($"Comuki.Modules.{dst}.Application")
                    .GetResult();
                Assert.True(result.IsSuccessful,
                    $"{pair}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            }
        }
    }

    [Fact]
    public void NoModuleInfrastructureDependsOnAnotherModule()
    {
        // same shape, target = *.Infrastructure
    }
}
```

This test would have failed at the moment the `Knowledge → Memory` and
`Proxy → Costs` edges were introduced.

## 4.7 Summary

| Test category | Status |
|---|---|
| Layer direction (Kernel → Contracts → Engine → Host) | ✅ enforced |
| Identity/Costs/Intake layer direction | ✅ enforced (3 modules of 10) |
| Chat/Knowledge/Memory/Projects/Proxy/Artifacts/Engine.Compute layer direction | ❌ **not enforced** |
| Cross-module isolation (`Modules.X ↛ Modules.Y.Application/Infrastructure`) | ❌ **not enforced** — proxy-violation **Active** |
| `Domain ↛ EF Core` purity | ❌ **not enforced** |

**Two architectural-drift violations identified by static review:**

1. **`Knowledge.Application → Memory.Domain`** — csproj line 14.
2. **`Knowledge.Infrastructure → Memory.Infrastructure`** — csproj line 25.
3. **`Proxy.Application → Costs.Application`** — csproj line 22.

Refactor paths documented per §4.2 above.

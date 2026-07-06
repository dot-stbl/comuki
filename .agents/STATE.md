---
milestone: v1
status: phase-5-in-progress
last_updated: 2026-07-06
progress:
  total_phases: 9
  completed_phases: 2
  total_plans: 5
  completed_plans: 4
  percent: 22
---

# Project State

## Current Position

Phase: 5
Plan: 05-01 (Z.AI key rotation proxy) — done
Status: phase_in_progress (Slice 1 first cut shipped; virtual keys / metering / budgets pending)

## ⚠️ Sequencing note (2026-07-06) — read first

**Phase 3 and Phase 4 are NOT finished.** Work jumped to Phase 5
(Z.AI key rotation) out of urgency, leaving P3 and P4 incomplete.
This contradicts the stale `phase-3-complete` markers and P3 sub-plan
SUMMARYs on disk — treat those as aspirational until reconciled.

- **P3** (Design System & Testing) — NOT finished; the 3 sub-plan
  `✅`/SUMMARYs need re-verification.
- **P4** (Slice 0) — NOT finished; `04-01-PLAN.md` is `status: ready`,
  only partial work landed on `develop`.
- **P5** (Slice 1) — jumped here urgently; 05-01 key-rotation cut done.

**TODO:** return to P3 and P4 before treating them as done. P6+
depend on a real P3 (testing/design contract) and P4 (vertical slice).

## Active Phase

Phase 1: Bootstrap — **DONE** (2026-06-04).
  - `dotnet build comuki.slnx` → 0 warnings, 0 errors.
  - `GET /health` → 200 `{"status":"ok"}`.
Phase 2: Stack Foundation — **DONE** (2026-06-04).
  - bun + Vite 8 + React 19 + TS strict + Tailwind v4
  - shadcn/ui (56 components, Radix base) + Storybook 8.6
  - BE: OpenAPI runtime + Scalar + build-time codegen via
    `Microsoft.Extensions.ApiDescription.Server` (openapi-v1.json
    written next to the .csproj; Kubb reads it; single source of
    truth for FE and any future SDK)
  - GitLab CI (.gitlab-ci.yml, 2 jobs)
  - deploy/ for local dev (postgres+pgvector, minio, nexus, victoria)
  - `agents/` + `control-plane/` directory skeletons (real TS
    packages land in Phase 4)
Phase 3: Design System & Testing Infrastructure — **NOT FINISHED** (stale `done` markers on disk — see sequencing note).
  - 3.1 Testing infra — **DONE** (2026-06-05).
    - BE: xUnit v3 (MTP runner via `dotnet run`), Shouldly, NSubstitute,
      Testcontainers.PostgreSql, Respawn, Bogus, coverlet.collector (70% gate)
    - BE: `tests/Comuki.Platform.Architecture.Tests/` (3 layer tests),
      `tests/Comuki.Platform.Testing/` (shared infra library, no [Fact]),
      `tests/Comuki.Platform.Orchestration.Unit.Lease/` (placeholder smoke test)
    - FE: vitest + testing-library + jsdom + @testing-library/user-event
    - FE: `vitest.config.ts` (jsdom, @/ alias, 70% v8 coverage threshold)
    - FE: `utils.test.ts` (4 tests on cn()), `vitest.setup.ts`
    - FE: Playwright (playwright.config.ts, e2e/landing.spec.ts 3 smoke tests)
    - CI: `test-backend` + `test-frontend` jobs in `.gitlab-ci.yml`
    - Key deviation: xUnit v3 uses `dotnet run` not `dotnet test` (MTP, not VSTest)
  - **3.2 Design tokens — **DONE** (2026-06-05).
    - IBM Plex Mono replaces Geist Mono Variable (`@fontsource/ibm-plex-mono`)
    - Slate-blue + cool-black palette: `#83A1DC`/`#15171B` dark, `#3C5A86`/`#FBFBFA` light
    - `--radius: 0.375rem` (6px); all 6 status tokens with per-theme hex values
    - Storybook backgrounds + `components.json` baseColor updated (`mauve` → `slate`)
  - 3 plans total (3.1 test infra ✓, 3.2 design tokens ✓, 3.3 stories + custom components ✓)
  - 3.3 Stories + 3 custom components — **DONE** (2026-06-05).
    - 3 custom components: `StatusBadge` (semantic pill with --st-* tokens), `RunIdChip` (copy-to-clipboard mono chip), `ModeToggle` (sun/moon/system switcher via local `useTheme`)
    - 58 Storybook stories (55 shadcn + 3 custom), all with 6 canonical states per frontend-construct-rules.md § 2
    - `@storybook/addon-vitest` + `@storybook/addon-a11y` deferred to Phase 7 (v10-only, project uses SB 8)
    - `bun run build-storybook` exit 0 ✓; `bun run test` exit 0 ✓
  - Slice 0 vertical slice moved to Phase 4 (was Phase 3 in the
    original plan).

Phase 4: Slice 0 Vertical Slice — **NOT FINISHED** (partial; see sequencing note).
  - Some work landed on `develop`: Translator
    (`Comuki.Platform.Worker.Translator`) launches `pi` headless +
    parses stream-json, gRPC contract in Orchestration, PiCli integration
    test via `TestFakePi`.
  - `04-01-PLAN.md` is still `status: ready`; the slice (claim primitive,
    gRPC bidirectional stream, full container loop) is **not** complete.

Phase 5: Slice 1 — Proxy & Virtual Keys — **IN PROGRESS**
(branch `feature/comuki-zai-key-rotation`).
  - 05-01 Z.AI key rotation proxy — **DONE**: thin YARP host
    `Comuki.Platform.Proxy` + `Comuki.Platform.Routing` (KeyPool,
    QuotaExhaustionDetector, KeyRotatingForwarder, YarpUpstreamSender).
    Transparent key rotation on quota exhaustion; 18 unit + 2 integration
    tests green; format/build gates clean (after the `dc38be2`
    rule-compliance pass).
  - Plan was executed via the `superpowers` workflow, not soly → relocated
    into `.agents/phases/05-slice-1-proxy/` (`05-01-PLAN.md`,
    `05-01-DESIGN.md`, `05-01-SUMMARY.md`) post-hoc.
  - Pending (rest of Slice 1): virtual keys (signed/TTL/capability),
    role→model routing, metering + cost attribution, budgets + kill-switch,
    egress allowlist, secret-manager (Vault), provider fallback.

## Goal (milestone v1)

Vertical slice through the platform (Slice 0 from `comuki-slice-0.md`): one ticket runs
end-to-end through a single worker — pull-claim, Translator/gRPC bridge, container
lifecycle. After that, Slice 1 (proxy) → 2 (knowledge) → 3 (verification) → 4 (DAG +
dashboard) → MVP polish.

## Progress

2 / 9 phases complete (P1, P2 only). **P3 and P4 NOT finished**
(jumped to P5 out of urgency). Plan-level: 05-01 verified done;
03-* carry stale SUMMARYs pending reconciliation; 04-01 not started
(`status: ready`). — 22 %

## Decisions

| Decision | Rationale | Phase |
|----------|-----------|-------|
| Translator в Phase 4 (Slice 0) — regular `Microsoft.NET.Sdk.Worker`, не NativeAOT. Image ~100MB, cold start ~1s — для эфемерных 30s–30min воркеров оверхед пренебрежим. AOT-накладные (gRPC source-gen, System.Text.Json source-gen, ~3x медленнее build) не оправданы в Slice 0. Ревизия: Phase 5+ если измерения покажут что нужно. | Slice 0 проверяет фундамент, не оптимизации. NativeAOT требует source generators и сломал бы pluggable библиотеки. csproj уже помечен `PublishAot=false` с комментарием, переключение — правка одного свойства. | 4 |
| Worker image в Phase 4 (Slice 0 step 0) — `oven/bun:1.3.10-bookworm-slim` base, не multi-stage. Translator ещё не собирается в этом плане (04-03). Multi-stage добавится когда появится реальный Translator-бинарь для COPY из build stage. | Phase 4 step 0 нужен только для sanity check, что `pi` запускается headless в контейнере. Translator появится в 04-03, тогда добавим build stage. Premature multi-stage сейчас = лишние слои без пользы. | 4 |
|----------|-----------|-------|
| Use **Conventional Commits 1.0.0** in this repo — no `[stbl]` prefix. See `.agents/rules/process/commit-format.md`. | The `[stbl]` prefix from `~/.claude/rules/git.md` is an anlytra-project convention, not a comuki one. The user (this is a hybrid repo) prefers Conventional Commits here. Per-rule hierarchy: `.agents/rules/` overrides `.claude/rules/`. | 1 |
| Apply commit-format rule **forward only** — do not rewrite the 3 pre-rule commits (`229d1dc`, `e36bda4`, `1be5302`). | History rewriting for cosmetic reasons is not worth the risk. New commits are in scope; old ones stay as-is and document the bootstrap era. | 1 |
| Polyglot monorepo split by stack (`platform/` C#, `agents/` TS, `dashboard/` React) | Per `comuki-project-structure.md` §1: each stack keeps its own manifest, lockfile, toolchain. | 1 |
| `comuki.slnx` at repo root, not inside `platform/` | Root entry matches the polyglot layout; future `agents.sln` / `dashboard` workspaces stay siblings, not nested. | 1 |
| Phase 1 ships 5 of 17 projects (Api.Public, Orchestration, Entity.Core, Api.Contracts, Database.Runs) | Minimal compile graph; the other 12 land in the slice that first needs them (Translator, Proxy, Knowledge, …). | 1 |
| `net10.0` target, `TreatWarningsAsErrors=true`, analyzers at `latest` | Per architecture.md §01 — verification is a load-bearing wall; Roslyn warnings-as-errors are non-negotiable. | 1 |
| `Directory.Build.props` at repo root, not inside `platform/` | One place to tune C# defaults; future non-C# stacks (TS, React) live in their own folders and won't be touched. | 1 |
| `.editorconfig` already on disk, supplied by the user | Code below must respect the existing rules — `dotnet build` will fail loudly otherwise. | 1 |
| `.agents/rules/` (C# style/framework/testing) carries over as-is | These rules are explicitly referenced by `comuki-project-structure.md` (`PROJECT-RULES.md`); they apply to the `platform/` solution unchanged. | 1 |
| Soly state files (STATE.md, ROADMAP.md, phases/) are committed, not gitignored | Next developer / next soly session must see the same context; only the runtime cache `rule-mtimes.json` is gitignored. | 1 |
| Two path mistakes in initial csproj (3 `..\` levels instead of 2) | Self-inflicted; both `Orchestration.csproj` and `Database.Runs.csproj` live in `src/feature/` and `src/database/`, not nested. Fixed in same commit. | 1 |
| `NoOpOrchestrationService` co-located in `IOrchestrationService.cs`, not in `Program.cs` | Trying to define a placeholder implementation in `Program.cs` hit IDE0065 (`using` inside namespace) — co-locating the marker class with its interface is cleaner. | 1 |
| `comuki.slnx` rewritten with `<Folder>` elements matching physical paths | PROJECT-STRUCTURE.md §7 requires `Solution folder = physical path`. Flat list was a rule violation caught on user review. | 1 |
| `dotnet sln add --solution-folder` collapsed all projects on .NET 10 SDK (Windows path quirk) — wrote `.slnx` directly instead | `dotnet sln` and `--solution-folder` is the canonical path, but the .NET 10 SDK mishandles `path-with-slashes` on Windows. Bootstrap is an exception to the "руками не редактируем" rule. Future projects: still use `dotnet sln add --solution-folder` with a `.slnx` re-check. | 1 |
| 3-plan split for Phase 3 (3.1 test infra, 3.2 design tokens, 3.3 stories + custom components) | Token change must land before `StatusBadge` consumes `--st-*`; tests must land before CI can gate axe. Linear 3.1 → 3.2 → 3.3 sequencing, each is a single reviewable unit. | 3 |
| Architecture test project at `platform/tests/Comuki.Platform.Architecture.Tests/` (sibling to `platform/src/`, not inside it) | Per `comuki-project-structure.md` §2 the `tests/` folder is at the C# solution root, alongside `src/`. Lives under `/platform/tests/` in the slnx. | 3 |
| Edit `comuki.slnx` directly when adding test projects (do not use `dotnet sln add --solution-folder`) | The .NET 10 SDK on Windows collapses `--solution-folder` paths (decision 1 above). Editing `.slnx` is the established workaround; verify solution folder = physical path after. | 3 |
| Test project naming: `Comuki.Platform.<SrcProject>.<Kind>[.<Feature>]` per `TESTING-RULES.md § 10`; omit `.Feature` for first test of a src project (it'll be added if a second appears) | Matches PROJECT-STRUCTURE.md § 10 convention; only the architecture test gets a non-`<Kind>` suffix (`Architecture.Tests` is singular in the doc). | 3 |
| 70% line coverage gate on both BE and FE (per `TESTING-RULES.md § 10`, explicitly NOT 80%+) | 80%+ forces meaningless tests for the metric. 70% is the documented floor. Gate is wired in 3.1; the empty production code passes trivially and the gate becomes meaningful as features land. | 3 |
| Add `@storybook/addon-vitest` + `@storybook/addon-a11y` in plan 3.3, not 3.1 | 3.1 only sets up top-level vitest + Playwright + CI; the Storybook-testing addons only earn their keep when there are stories to test, which is 3.3. | 3 |
| `next-themes` stays as a dep (do not remove, do not migrate) | `dashboard/src/components/ui/sonner.tsx` imports `useTheme` from `next-themes` (shadcn-official, do not edit). The local `theme-provider.tsx` remains the canonical theme source; `ModeToggle` consumes the local one. | 3 |
| `ModeToggle` consumes the local `useTheme` from `theme-provider.tsx`, NOT the `next-themes` one | Locked decision: local provider is the contract; `next-themes` is only there for sonner. | 3 |
| `StatusBadge` lives in `dashboard/src/components/ui/status-badge.tsx` (next to shadcn) — NOT in a `dashboard/src/comuki/` subfolder | Locked decision: custom components sit alongside shadcn primitives in `ui/`. | 3 |
| Storybook story files for all 55 shadcn components (each with 6 canonical states) + 3 custom components | Locked decision: do not narrow to "top 10"; every primitive gets the full state coverage per `frontend-construct-rules.md § 2`. | 3 |
| Comuki palette: slate-blue accent (#83A1DC dark / #3C5A86 light) + cool-black surfaces (#15171B dark / #FBFBFA light); IBM Plex Mono everywhere; 6 status tokens; --radius: 0.375rem | From `.agents/docs/design-system/Comuki Design System.md` § 3 (source of truth); replaces shadcn's radix-mira + mauve defaults. The `failed` status uses terracotta (`#D6685A` dark / `#B0473B` light) as the danger color — that is the only non-neutral "loud" color. | 3 |
| xUnit v3 uses Microsoft Testing Platform (MTP), not VSTest | Tests run via `dotnet run --project <csproj>` not `dotnet test`. VSTest does not support xUnit v3 test discovery. | 3.1 |
| Storybook test addons (@storybook/addon-vitest, @storybook/addon-a11y) deferred to plan 3.3 | `@storybook/addon-vitest` preset has Node.js 24 compatibility issue (ERR_INTERNAL_ASSERTION on ES module loading). | 3.1 |
| `Comuki.Platform.Testing` (not `Acme.Shop.Testing` per template) | Project prefix matches actual codebase (`Comuki.Platform`), not the TESTING-RULES template name (`Acme.Shop`). | 3.1 |
| `@storybook/addon-vitest` + `@storybook/addon-a11y` are SB 10-only; project uses SB 8; deferred to Phase 7 | Both packages have no v8.x release; `addon-vitest` has Node.js 24 ESM loader bug. Both removed from `package.json`; TODO comments added to `main.ts`/`preview.ts`. | 3.3 |
| `.soly/` layout migrated to `.agents/` (soly 2.0+ reads `.agents/` only). 55 files moved via `git mv`, all internal `.soly/` path refs rewritten; STATE content carried over unchanged. | `soly_read` / `soly_workflow` returned "not found" under the old layout. Migration is mechanical; STATE content-sync done as a separate step. | 5 |
| Phase 5 (Z.AI key rotation) executed via the `superpowers` subagent workflow, **not** soly — root cause of the STATE drift past Phase 3. Plan + design relocated from `docs/superpowers/` into `.agents/phases/05-slice-1-proxy/` and given soly frontmatter post-hoc. | Urgent feature; superpowers was the tool at hand. Post-hoc relocation restores soly visibility without rewriting git history. | 5 |
| **Phase 3 and Phase 4 are NOT finished** — work jumped to Phase 5 (Z.AI key rotation) out of urgency. Corrects an earlier overclaim (commit `b80b232`) that marked them DONE. Progress corrected 4/9 → 2/9. | User correction (2026-07-06). The stale `phase-3-complete` status + P3 sub-plan `✅`/SUMMARYs on disk contradicted reality; treat those as aspirational until reconciled. `04-01` is `status: ready`. | 5 |
| `.editorconfig`: path-scoped `[tests/**.cs]` exempts test methods from the async-suffix naming rule. | TESTING-RULES §3 BDD examples omit `Async` on test methods (`public async Task CreateOrder()`); the global async-suffix rule (error) conflicted. Exemption is scoped to `tests/` only — production async methods still require the suffix. | 5 |
| Phase 5 urgent run skipped `dotnet format` (36 IDE violations, all in phase-5 files); fixed in `dc38be2` — dropped redundant `this.`, camelCased private fields, applied Pyramid Rule to 3 primary ctors, rewrote tests to BDD. | `dotnet build` doesn't enforce IDE0003/IDE1006; only `dotnet format --verify-no-changes` does (build-verification.md DoD). The urgent run ran build only. | 5 |

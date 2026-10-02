## 1. Golden bundle `net10-sdk-bun`

- [x] 1.1 Add `deploy/env/net10-sdk-bun.Dockerfile`: SDK stage publishes Translator; final stage has .NET 10 SDK, bun >= 1.4, git, pi, translator ENTRYPOINT. Verify: `dotnet --version` and `bun --version` via overridden entrypoint in the image sanity path used by `deploy/README.md` (or a documented compose one-shot if docker is unavailable locally — same bar as harden-pi-worker-sandbox 3.4). — **static verification only (no docker on the dev machine): instruction-level diff vs `worker.Dockerfile` shows the single `--runtime`-flag delta; CI owns the build.**
- [x] 1.2 Keep the historic runtime-only `deploy/worker.Dockerfile` as a non-default image; document that implement work MUST NOT use it. Verify: README worker-image table names `net10-sdk-bun` as the implement default.

## 2. Catalog + Project `EnvClass` (scalar until Repositories land)

- [x] 2.1 Add an in-process bundle catalog (id, digest, runtime, permitted restore opcodes, publisher, resource shape) with `net10-sdk` and `net10-sdk-bun` seeded; Production rejects tag-only. Verify: unit test tag-only fails in Production; digest entry resolves. — **`EnvironmentBundlePinningShould` covers tag-only Production refusal.**
- [x] 2.2 Add `EnvClass` on Project (EF + create/patch + views) as the scalar stand-in; empty class means implement items are not claimable. Verify: create/patch tests; migration applies. — **migration `20260930011840_AddProjectEnvClass` generated via `dotnet ef`; the claim-side effect lands with group 3.**
- [x] 2.3 Document the move of `EnvClass` onto Repository when `add-multi-repo-projects` attachment migration runs (comment + tasks cross-link). Verify: the multi-repo migration task list or this file names the hand-off. — **`Project.EnvClass` XML doc names the hand-off.**

## 3. Claim / scale by class (tracer)

- [ ] 3.1 Work item column `env_class`; enqueue copies Project (later Repository) `EnvClass`. Verify: unit test enqueue without class is rejected; with class persists.
- [ ] 3.2 Claim SQL and `POST /workers/claim` match `profile_key × env_class × profiles_ref`; body field `envClass` required. Verify: queue unit tests — class mismatch 204; missing body field 400; happy path returns `envClass`.
- [ ] 3.3 Compute start request carries class; provider resolves digest; labels `comuki.env_class`; `COMUKI_ENV_CLASS` stamped. Verify: Docker/k8s mapping tests; Production start without digest fails (extends harden-pi 5.4).
- [ ] 3.4 Scale policy inputs are queued/idle **per (profile, env class)**; idle other-class workers do not satisfy backlog. Verify: pure policy unit tests including the UE-vs-net10 mismatch scenario.
- [ ] 3.5 `dotnet build comuki.slnx -c Debug` plus touched Orchestration / Compute / Host unit projects pass.

## 4. Toml projection + restore opcodes

- [x] 4.1 Parser for `.comuki/environment.toml` (Tomlyn): schema 1, required `class`/`runtime`, `[restore]` keys ⊆ class opcodes, unknown keys fail. Verify: unit tests for the three dogfood/cpp examples in the discussion and the `postCreate` rejection. — **`EnvironmentTomlShould` (12 cases), parser in `Comuki.Shared.Contracts/Environments`.**
- [ ] 4.2 On attach or settings patch, if the source ref has a valid toml, set `EnvClass`; invalid toml does not claimable-ify. Verify: handler test with a git fixture or in-memory tree.
- [ ] 4.3 Translator after clone runs declared restore opcodes (host-side) before pi; failure fails the item. Verify: Translator test with fake restore processes; pi not started on restore fail.
- [x] 4.4 Commit `.comuki/environment.toml` for this repository (`net10-sdk-bun`, restore slnx + dashboard/agents). Verify: parser accepts the committed file. — **content mirrors the parser's dogfood test case.**

## 5. Locks and Brain authoring (thin)

- [ ] 5.1 Confirm default install locks still deny `dotnet add package` / `npm install` after restore exists. Verify: existing lock tests plus one that restore is not invoked via agent Bash.
- [ ] 5.2 Attach-without-class path: Brain MAY open a PR limited to `.comuki/environment.toml` filling the closed schema; implement stays unclaimable until merge. Verify: a unit or contract test that empty `EnvClass` ⇒ no claimable item; document the PR author profile (no full Brain loop required in this slice).

## 6. Importer and marketplace shelves (follow-up, not tracer)

- [ ] 6.1 Attach-time importer: read `.devcontainer/devcontainer.json` if present, map image/official features/hostRequirements to a *proposed* toml, list ignored fields. Verify: unit test hostile `postCreate` + dind are ignored and never executed.
- [ ] 6.2 Catalog shelves + publisher allowlist; unallowlisted community class does not start. Verify: unit test allowlist miss.
- [ ] 6.3 Dashboard/CLI: show bound class on the project/repo; capacity miss copy (not external-block). Verify: typecheck + a view test or CLI snapshot.
- [ ] 6.4 Type the projects endpoints' success responses (`TypedResults.Ok<ProjectView>` / list shape or `[ProducesResponseType]`) so `ProjectView` reaches `artifacts/openapi.json` — today the projects GETs return untyped `IResult` and the WHOLE view (id/name/slug/tags/envClass/…) is invisible to the generated FE client (validation finding 2026-09-30, pre-existing; surfaced by the envClass codegen check). Then `bun run generate-api` + FE gates, commit the regenerated client. Verify: `rg envClass artifacts/openapi.json` hits; `_generated` diff reviewed.

## 7. Gates

- [ ] 7.1 `dotnet build comuki.slnx -c Debug` (0/0) after the tracer slices (1–5).
- [ ] 7.2 Architecture tests still pass (no new illegal module edges).

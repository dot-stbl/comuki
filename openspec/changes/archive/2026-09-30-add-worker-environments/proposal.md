## Why

A work item today claims `profile_key × image × profiles_ref`. The worker image smuggles both the agent runtime (Translator + pi) and the project toolchain. The shipped image is bun + pi + .NET 10 *runtime* — no SDK — while default locks forbid side-effect installs. Even this repo cannot `dotnet build` on a worker; UE5 / MSVC / GPU make the same hole unbearable. Profile is a *role*. Toolchain must be a named environment class on the **target repository**, inherited by the work item, not chosen by Brain per task.

## What Changes

- Introduce an **environment class** (bundle): operator-owned OCI image with Translator already in, plus runtime (`linux` | `windows`), advertised resource shape, and the restore opcodes that class permits.
- Bind class to the work item's **target repository**. Authoritative store is the Repository registry row (`EnvClass`); `.comuki/environment.toml` in the repo is the git projection (schema 1, TOML, unknown fields refused). Merge of that file confirms, same law as RepositoryLinks.
- **BREAKING** for claim/scale: workers match `profile_key × env_class × profiles_ref`. Image digest is resolved from the catalog at `StartAsync`, never smuggled as the claim identity. Scale groups warm hosts by class.
- Slot restore runs only catalog opcodes (`dotnet restore`, `bun install`, …) from the toml; never free shell. Compilers live in the bundle, not in the claim.
- Marketplace of bundles: Comuki golden / org / community shelves. Fleet allowlists publishers. Missing pool for a class is a capacity miss, not `blocked-external`.
- Brain / human may author the toml as a PR on attach (closed schema). LLM does not install toolchains and does not write `devcontainer.json` as the source of truth.
- First golden bundle `net10-sdk-bun` closes dogfood for this repo and console.x-shaped products. Attaching this git URL as a Project is allowed; AGENTS.md MUST NOT be read as a ban on self-host implement.
- Slot start later composes through `add-worker-admission`; this change only supplies the class axis.

## Capabilities

### New Capabilities

- `worker-environments`: bundle catalog, `.comuki/environment.toml` schema, repository `EnvClass` binding, attach/confirm/drift, restore opcode contract, marketplace shelves, Brain authoring of the declaration (not the runtime).

### Modified Capabilities

- `compute`: `StartAsync` resolves class → digest + runtime class + mounts/devices; supervisor places by class; missing class capacity is a planner delay.
- `worker-runtime`: claim labels carry `env_class`; workspace restore opcodes after clone; worker image is agent runtime + class toolchain (layered), never a client `devcontainer` image as ENTRYPOINT.
- `work-queue`: claim match includes `env_class`; items without a confirmed class are not claimable.
- `agents-sdk`: default install locks stay; restore opcodes are not `dotnet add package` / `npm install` side-effects.
- `runs`: work item carries `EnvClass` alongside existing claim labels; journal types for restore.
- `projects`: scalar `EnvClass` stand-in until the Repository attachment migration.

## Impact

`Comuki.Engine.Compute` (start request, labels, supervisor), Orchestration work-item / claim SQL, Translator prepare (restore after clone), Projects/Repositories binding, Host composition, `deploy/worker.Dockerfile` split into runtime vs class bundles, dashboard/CLI env class on repo, catalog API. Depends on `add-multi-repo-projects` for Repository as the binding aggregate; until that lands, `EnvClass` MAY sit on the scalar source repo of a Project and MUST move with the primary attachment migration.

v2 capability. Tracer: golden `net10-sdk-bun` + claim-by-class for this repo, before Windows/UE bundles.

## Non-goals

- Becoming the Dev Container spec runtime (lifecycle shell, privileged, DinD, compose, VS Code customizations, official `devcontainer` CLI in-cluster). A present `.devcontainer/devcontainer.json` MAY feed an attach-time importer that proposes `.comuki/environment.toml`; it is never executed.
- Bake-as-worker / LLM `apt-get` / publishing a snapshot digest as the claim label.
- Installing Unreal / Xcode / MSVC inside a claim; those are bundle contents or host mounts declared by the class.
- Path-prefix env on one git URL (this repo's `generate-api` needs the union class).
- Replacing control-plane profiles (role) with env class (toolchain).
- Community bundle execution without fleet publisher allowlist.

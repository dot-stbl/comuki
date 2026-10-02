## Context

See `proposal.md` for why. Today claim identity is `profile_key × image × profiles_ref`; `deploy/worker.Dockerfile` is bun + pi + .NET 10 runtime (no SDK); default locks forbid side-effect installs. `add-multi-repo-projects` makes Repository the owner of rules/credentials (R3) and one-task-one-target-repo (R8). `add-mission-cowork` worker-pools already advertise images/tools/resource shape but do not name a class. `harden-pi-worker-sandbox` forbids docker.sock, privileged, and Brain-added egress — bake-as-worker and the official `devcontainer` CLI in-cluster are incompatible with that floor.

Until the Repository module lands, `EnvClass` lives on the Project scalar source and moves with the primary-attachment migration already specified there.

Sibling changes (do not duplicate here): clone (`harden-pi-worker-sandbox` 4.x), secrets (`agent-runtime-capabilities`), pools (`add-mission-cowork` worker-pools), attribution (`add-worker-commit-attribution`), admission composition (`add-worker-admission`), isolated verify (`isolate-verifier-runtime`).

## Goals / Non-Goals

**Goals:**

- Split agent runtime from toolchain: class is the claim key; digest is resolved at start.
- Closed in-repo schema (TOML) as git projection; registry row as claim source of truth.
- Golden `net10-sdk-bun` so this repo and console.x-shaped products restore without LLM installs.
- Same schema for Windows/UE later (`runtime`, `mounts`) without a second format.

**Non-Goals:** (see proposal.) Design additionally excludes a per-attach Feature-install pipeline and path-prefix classes on one git URL.

## Decisions

### D1. Claim identity is `env_class`, not image digest

Image digest changes when a bundle is rebuilt; class is the stable pool key. Warm hosts of `net10-sdk-bun` must not claim `ue5.4-win`. Supervisor groups by `(profile, env_class)`. `COMUKI_WORKER_IMAGE` remains the resolved digest for diagnostics and pin-in-prod.

Alternative considered: keep image as claim label and treat class as metadata. Rejected — SKIP LOCKED would still match the wrong pool.

### D2. Registry row is authoritative; toml is projection

`Repository.EnvClass` (or Project scalar until then) is what enqueue copies onto the work item. `.comuki/environment.toml` at the target ref is how humans and Brain PRs write it. Merge updates the row. File↔row drift notifies; claims follow the row so a force-push cannot silently retarget a running fleet.

Alternative considered: git-only (read the file at claim). Rejected — claim SQL cannot cheaply parse TOML, and a missing file mid-queue would 204-storm workers.

### D3. TOML schema 1, closed fields, Tomlyn

Host config is already TOML (`config.toml`, Tomlyn). Unknown keys fail (not warn). `schema = 1` is the only evolution lever. YAML rejected (implicit types). JSON rejected (no comments, LLM trailing commas). Markdown+frontmatter rejected (this is not prose; control-plane keeps markdown for roles).

Restore keys are opcodes the *class* advertises (`dotnet`, `bun`, later `cmake`/`vcpkg`). Not a script block.

### D4. Layered image: Translator in every bundle, toolchain is the variant

Do not `FROM` a client Dev Container image as ENTRYPOINT. Bake: SDK stage publishes Translator; final stage is the class base (sdk+bun, or clang, or Windows toolset) + pi + translator. Client `image:` in a leftover `devcontainer.json` is at most an importer hint.

Alternative considered: sidecar/volume of toolchain onto a thin agent image. Deferred — Docker/k8s start today is one container; mounts are enough for UE Engine later.

### D5. Restore on the slot, lock hash is a cache key

`dotnet restore` / `bun install` after clone, Translator-owned, before pi. Cache volumes keyed by lockfile hash. Claim identity stays the class so a lock bump does not rebuild the warm pool (UE-sized disaster if it did).

### D6. Marketplace is bundles only

Three shelves (Comuki / org / community). Features from the Dev Container registry are Dockerfile lines of a *bundle*, never a runtime SKU. Community pull requires fleet publisher allowlist.

### D7. Devcontainer is an attach-time importer, not a runtime

Map `image` / official features / `hostRequirements` → proposed toml. Ignore lifecycle, privileged, DinD, compose, customizations. Never run `devcontainer` CLI on the worker cluster (sandbox + no bake-plane on `IComputeProvider`).

### D8. Missing pool ≠ R5 external-block

R5 is “no write attachment to a git host”. No Windows+GPU node is a planner capacity miss. Operators must see the compute screen, not the external-request inbox.

### D9. Brain fills the closed schema as a PR

Explore-readonly (or a dedicated author profile with Write limited to `.comuki/environment.toml`) proposes `class` + `restore`. Confirm is merge. Brain does not `apt-get` and does not author `devcontainer.json` as source of truth.

### D10. Tracer is `net10-sdk-bun` + claim-by-class

Windows/UE bundles are schema-compatible but not the first implementation slice. Path-prefix on this repo is rejected: `dashboard` `generate-api` needs the union class.

### D11. Dogfood is in scope

AGENTS.md's "Comuki does not write its own product clients" is not a ban on attaching this git URL. The tracer is this repository building on `net10-sdk-bun`.

## Risks / Trade-offs

- **[Risk] Two sources (file vs row) drift.** → Notify; claim the row; human reconciles. Same pattern as RepositoryLinks.
- **[Risk] Closed schema cannot express a weird native tool.** → New opcode on a new class (catalog PR), not a shell hole in schema 1.
- **[Risk] Supervisor still has Project image override.** → Spec: override MUST NOT replace repository class; migrate callers; fail start if they disagree.
- **[Risk] Bake plane does not exist; someone will run Feature install on claim.** → Forbidden in spec. Bundle CI is operator/catalog pipeline, same as today’s worker image build.
- **[Risk] `add-multi-repo-projects` not merged.** → Dual-write `EnvClass` on Project scalar + migrate with primary attachment.
- **[Risk] SDK image without sandbox floor.** → Do not ship Production implement on `net10-sdk-bun` until harden-pi non-root + egress land (admission will refuse).
- **[Trade-off] Union class `net10-sdk-bun` is fatter than `net10-sdk`.** → One pool for this repo beats two classes that `generate-api` would hop between.

## Migration Plan

1. Publish golden image `net10-sdk-bun` (SDK in final stage). Keep runtime-only image for explore-readonly if needed, as its own class later.
2. Add `env_class` column / claim body; dual-read: if `env_class` empty, derive from legacy `image` only in Development (not Production).
3. Bind this repository to `net10-sdk-bun`; commit `.comuki/environment.toml`.
4. Flip Production to refuse tag-only and empty class.
5. When Repository module lands, move `EnvClass` onto the Repository row.

Rollback: set fleet default class back to the previous digest via catalog, not by restoring image-as-claim in SQL (forward-only label).

## Open Questions

- Exact cache-volume driver per provider (Docker named volume vs k8s PVC) — does not change the spec (lock hash is not a claim label).
- Whether explore-readonly keeps a thinner class than implement — can ship as a second catalog id without schema change.

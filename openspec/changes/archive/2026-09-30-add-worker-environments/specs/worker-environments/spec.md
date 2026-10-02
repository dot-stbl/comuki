## Purpose

Defines how a repository declares the toolchain a worker needs, how the platform catalogs environment-class bundles, and how a work item inherits that class so claim, scale, and restore stay deterministic.

## ADDED Requirements

### Requirement: Environment class is a catalog bundle
An environment class SHALL be a named bundle in the platform catalog: a stable id (`net10-sdk-bun`, `net10-sdk`, `cpp-clang-18-linux`, `ue5.4-win`), an OCI image digest (tag-only is refused in Production), a runtime (`linux` | `windows`), advertised resource shape (cpu, memory, optional gpu), optional host mounts, the restore opcodes the class permits, and a publisher (`comuki` | org | community). The agent runtime (Translator + pi) SHALL already be in the image. The bundle SHALL NOT contain a product-repository checkout.

#### Scenario: Production refuses a tag-only bundle
- **WHEN** a catalog entry for `net10-sdk-bun` has image `ghcr.io/comuki/env/net10-sdk-bun:latest` with no digest
- **THEN** Production start of a worker for that class fails at the compute gate and the class is not claimable

#### Scenario: Bundle has no product sources
- **WHEN** a worker of class `net10-sdk-bun` starts
- **THEN** the image does not contain this repository's tree; the Translator clones the target after start

### Requirement: Marketplace shelves and fleet allowlist
Bundles SHALL sit on three shelves: Comuki golden, organization, community. A fleet SHALL allowlist publishers it will pull. A community bundle whose publisher is not allowlisted SHALL NOT be resolved. Presence of a class in the catalog SHALL NOT imply the fleet has capacity for its runtime; a queued item whose class has no advertised pool is a capacity miss (planner delay), not an external-block.

#### Scenario: Unallowlisted community bundle is not started
- **WHEN** a repository binds `class = "cool-cpp"` published by an unallowlisted community publisher
- **THEN** workers are not started for that class and the operator sees that the publisher is not on the fleet allowlist

#### Scenario: Missing GPU pool is a capacity miss
- **WHEN** a repository binds `ue5.4-win` and the fleet has no Windows+GPU hosts advertising that class
- **THEN** the work item stays queued as a capacity wait, not `blocked-external`

### Requirement: Repository binds one environment class
The target repository of a work item SHALL bind exactly one environment class. The authoritative binding is the Repository registry field `EnvClass`. Until `add-multi-repo-projects` lands, the scalar Project source repository SHALL carry the same field and MUST migrate onto the primary attachment's Repository. A Project SHALL NOT override the target repository's class with a different image. Control-plane profiles remain the worker *role* and SHALL NOT encode toolchain.

#### Scenario: Two repos in one Project keep distinct classes
- **WHEN** Project console.x attaches `console.x` (`net10-sdk-bun`) and `console.x.sdk` (`net10-sdk`)
- **THEN** a task targeting the SDK claims `net10-sdk` and a task targeting the product claims `net10-sdk-bun`

#### Scenario: Brain cannot pick a class per task
- **WHEN** Brain decomposes two tasks against the same target repository
- **THEN** both work items carry that repository's `EnvClass`

### Requirement: `.comuki/environment.toml` is the git projection
A repository MAY declare the binding in `.comuki/environment.toml` at the work item's target ref. The file SHALL be TOML, `schema = 1`, with required keys `class` (catalog id) and `runtime` (`linux` | `windows`), optional table `[restore]` whose keys are opcodes the bound class permits, and optional table `[mounts]` only for mounts the class advertises. Unknown keys, unknown opcodes, or a `class` not in the fleet catalog SHALL be a parse failure: the file is not accepted and the work item is not claimable. Comments are allowed. JSON and YAML SHALL NOT be accepted at this path.

#### Scenario: Closed schema rejects shell
- **WHEN** `.comuki/environment.toml` contains a `postCreate` or `run` key
- **THEN** parse fails and no work item for that ref becomes claimable

#### Scenario: Opcode not advertised by the class is refused
- **WHEN** class `net10-sdk` (no bun) has `[restore] bun = ["dashboard"]`
- **THEN** parse fails

#### Scenario: Valid Comuki dogfood file is accepted
- **WHEN** the file is `schema = 1`, `class = "net10-sdk-bun"`, `runtime = "linux"`, `[restore] dotnet = "comuki.slnx"` and `bun = ["dashboard", "agents"]`
- **THEN** the binding is accepted and work items targeting that ref carry `net10-sdk-bun`

### Requirement: Confirm is merge; registry follows the pin
A newly attached repository without a confirmed class SHALL NOT enqueue claimable implement work. A human MAY commit `.comuki/environment.toml`; the Brain MAY open a pull request that fills the closed schema (not a shell script, not `devcontainer.json` as source of truth). Merge of that file at the tracked ref SHALL confirm the class and update `EnvClass` on the registry row. Drift between the file at the work item's ref and the registry row SHALL notify and SHALL NOT silently rewrite the row; claims use the registry class until a human reconciles.

#### Scenario: Attach without class blocks implement
- **WHEN** a repository is attached and neither `EnvClass` nor a merged `.comuki/environment.toml` exists
- **THEN** implement work items for that target are not claimable

#### Scenario: Brain PR is the declaration
- **WHEN** Brain authors a PR that only adds a valid `.comuki/environment.toml` for a known class
- **THEN** the item stays unclaimable until that PR merges, and merge sets `EnvClass` to the file's `class`

### Requirement: Dev Container file is import only
If `.devcontainer/devcontainer.json` exists on attach, the platform MAY map `image` / official `features` / `hostRequirements` into a *proposal* for `.comuki/environment.toml`. Lifecycle scripts, `privileged`, `capAdd`, Docker-in-Docker, `initializeCommand`, compose, and editor `customizations` SHALL be ignored and listed as ignored. The platform SHALL NOT execute the Dev Container spec, SHALL NOT run the official `devcontainer` CLI in the worker cluster, and SHALL NOT claim compatibility with the spec.

#### Scenario: Hostile fields are not executed
- **WHEN** an attached repo's `devcontainer.json` contains `postCreateCommand` and `features` docker-in-docker
- **THEN** those fields are reported as ignored and no worker runs them

#### Scenario: Importer proposes toml, does not execute
- **WHEN** attach finds a `devcontainer.json` with official `features` dotnet and node
- **THEN** the platform may open a proposal for `.comuki/environment.toml` with `class = "net10-sdk-bun"` and does not run the Dev Container CLI

### Requirement: Restore opcodes run on the slot after clone
After the target repository is cloned into the working directory, the Translator SHALL run only the `[restore]` opcodes from the accepted toml, in catalog order, as host-side process starts (not as the coding agent). `dotnet restore <solution>`, `bun install` in listed directories, and other class-advertised opcodes are permitted. `dotnet add package`, `npm install` without a lockfile, `pip install`, and arbitrary shell SHALL NOT be restore opcodes. Restore failure SHALL fail the work item before the agent starts. Lockfile hash SHALL key a cache volume, not the claim identity.

#### Scenario: Restore cache miss still claims the same class
- **WHEN** `bun.lock` changed since the last slot of class `net10-sdk-bun`
- **THEN** the worker still claims that class; restore is slower on a cold volume; the warm pool is not rebuilt

#### Scenario: Restore failure skips the agent
- **WHEN** `dotnet restore comuki.slnx` exits non-zero
- **THEN** the work item is failed with the restore reason and pi is not started

### Requirement: Golden class `net10-sdk-bun`
The Comuki shelf SHALL publish class `net10-sdk-bun`: Linux, .NET 10 SDK, bun >= 1.4, git, Translator + pi as ENTRYPOINT, restore opcodes `dotnet` and `bun`. This class SHALL be sufficient to restore and verify this repository (solution build plus dashboard/agents bun gates). Class `net10-sdk` SHALL be the same without bun, for SDK-only repositories.

#### Scenario: This repository builds on the golden class
- **WHEN** a worker of class `net10-sdk-bun` clones this repository and runs the declared restore
- **THEN** `dotnet build comuki.slnx -c Debug` and the bun typecheck/test scripts in `dashboard/` and `agents/` are executable without further toolchain install

### Requirement: Comuki dogfood is a first-class target
The platform repository SHALL be attachable as a Project whose implement workers use class `net10-sdk-bun` and `.comuki/environment.toml`. Product copy MAY still say Comuki writes *other* products; it SHALL NOT forbid a deployment from using Comuki to change this repository. A fleet that cannot restore this repository on the golden class SHALL be treated as a failed tracer of `worker-environments`, not as out of scope.

#### Scenario: Self-host implement is allowed
- **WHEN** an operator attaches this git URL as a Project with write access and confirmed `net10-sdk-bun`
- **THEN** implement work items for this repository are claimable on that class

### Requirement: Bundle catalog is readable
The host SHALL expose a read-only catalog of environment classes the fleet allowlists: id, runtime, publisher, permitted restore opcodes, resource shape, and whether a digest is pinned (the digest value MAY be omitted from unauthenticated responses). Write of catalog entries is an operator/fleet concern, not a Brain tool.

#### Scenario: List shows golden classes
- **WHEN** an authenticated operator lists environment classes
- **THEN** `net10-sdk-bun` and `net10-sdk` appear with runtime `linux` and opcodes `dotnet` / `bun` as advertised

#### Scenario: Digest is not required on the public list
- **WHEN** the catalog is listed
- **THEN** a missing digest on a class still shows the id, and Production start of that class still fails per the bundle requirement

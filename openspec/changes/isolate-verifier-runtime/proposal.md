## Why

Product verification (`dotnet build`, test suites, format) today can run via `Process.Start` in the Host process (`GenericCommandVerifierWorker` / #47). An env-class worker with an SDK makes that worse: the orchestrator box is the same machine as postgres. Verify must use the same environment class as the work item, in an isolated runtime, never the API host.

## What Changes

- Verification of a work item SHALL run in a compute slot (or dedicated verifier container) admitted with the **same `env_class`** as the item, not in `Comuki.Host`.
- Host-side `Process.Start` of product toolchains SHALL be removed from the verify path once the isolated runtime exists. Health probes of the host itself stay in-process.
- The verifier reuses `IComputeProvider` + SlotAdmission; it SHALL NOT open a docker.sock from Host and SHALL NOT share the Host filesystem as cwd.
- Results return as a structured report on the work item / run journal (`verify.completed` / `verify.failed`). Non-zero build is a failed verify, not a Host crash.

## Capabilities

### New Capabilities

- `verify`: isolated verification runtime, same env class as the work item, report contract, ban on Host `Process.Start` for product gates.

### Modified Capabilities

- `compute`: verifier slots are ordinary starts with admission; optional profile `verify` vs `implement`.
- `worker-runtime`: a verify execution MAY skip pi and run restore + declared verify opcodes (build/test) only.
- `host`: composition MUST NOT register a product-toolchain process runner inside the API process for work-item verification.
- `runs`: journal types `verify.completed` and `verify.failed`.

## Impact

`Comuki.Modules.Verify`, Host worker registry, Compute, Translator or a thin verifier entrypoint on the same class image. Builds on `add-worker-environments` (class + restore) and `add-worker-admission`. Recovers the intent of #47 / `rescue/generic-command-verifier` without putting the compiler next to the database.

## Non-goals

- Replacing the coding agent with a CI system. Verify is the gate after implement, not GitHub Actions.
- Running `bun run dev` / watch from verify.
- Host `dotnet --info` style self-checks (those are doctor, not product verify).

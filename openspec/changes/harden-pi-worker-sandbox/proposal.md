## Why

Docs promise a fenced worker (virtual model key, egress allowlist, no docker.sock). The running 1:1 container is still root, has full egress, inherits no minted key into `pi`, and starts `pi -p BRIEF --mode json --no-session` in an empty `/work`. Prompt-injection from the repo can `curl` anywhere. This change is the **floor for isolation class `strong`**: a fence around pi, not a new orchestrator. Cowork warm slots (`trusted-process`) stay out of scope.

## What Changes

- Default-deny egress enforced by the compute provider (`profile ∩ project`). Brain/pi cannot add hosts. Fail-closed unless `Compute:AllowUnfencedEgress=true` (dev only).
- Host **mints** a virtual key on claim/lease; Translator stamps `ANTHROPIC_BASE_URL` + token into **that** pi `ProcessStartInfo`. Revoke on complete/fail/lease-lost. Not a container-lifetime or config-seed key.
- Project gains `SourceGitUrl` / `SourceGitRef` and an optional git-credential secret ref. Translator clones HTTPS into cwd **before** `Process.Start(pi)`. Token never lands in pi env.
- Docker/k8s apply CPU/memory **limits** (not only k8s requests) plus hardening: non-root, cap_drop ALL, no-new-privs, seccomp RuntimeDefault. Rootfs stays RW.
- Translator loop emits journal conditions `WorkspacePrepared` / `EgressApplied` / `AgentRunning` and flushes artifacts before complete/fail.
- Opt-in operator exec into the Translator container (default off). Skills/locks/MCP become pi-extensions. Image pin by digest in prod. `translator --fixture` for local prepare tests.

Current 1:1 container **is** `strong`. Profiles without a class default to `strong`.

## Capabilities

### New Capabilities

- `worker-sandbox`: fence contract for `strong` — egress policy, allowlist, fail-closed, conditions, debug opt-in. Cowork `worker-pools` SHALL treat this as the floor for `strong`.

### Modified Capabilities

- `compute`: provider applies egress, limits, hardening; refuses unfenced start.
- `worker-runtime`: mint-stamp virtual key into pi; workspace clone before spawn; journal conditions; artifact drain; fixture CLI.
- `projects`: `SourceGitUrl` / `SourceGitRef` + git credential secret ref; PATCH includes them; slug still immutable.
- `proxy`: runtime mint/revoke of virtual keys bound to a work-item lease (store is no longer config-seed-only).
- `artifacts`: Translator MAY upload the bundle before complete; packager remains the fallback.
- `agents-sdk`: locks, skills, and MCP SHALL be enforced through pi-extensions, not files on disk alone.

## Impact

`Comuki.Engine.Compute` (Docker/k8s HostConfig/Job), `ScaleSupervisorCycle`, `Comuki.Modules.Proxy` (`IVirtualKeyStore` mint), `Comuki.Host.Translator` (`PiRunner` env, Prepare, journal, drain), Projects API/EF, worker image user, dashboard journal conditions, `comuki-worker-sdk` pi-extensions. No change to claim/lease/heartbeat, `--no-session`, or gRPC event shape.

v1 hardening / floor for v2 `strong`. First code slice: mint + egress + limits/hardening. Workspace and extensions follow as listed issues.

## Non-goals

- Google AX / Agent Substrate / gVisor / microVM as a dependency.
- Suspend/resume, pi session persistence, `ax ssh` guest APIs, Workspace `goal` LLM bootstrap.
- Warm multi-slot hosts, `trusted-process`, quarantine planner (cowork).
- SSH git, Brain-supplied clone URLs, RO rootfs, inbound listeners on the worker.
- Replacing YARP virtual keys with raw upstream secrets in the container.

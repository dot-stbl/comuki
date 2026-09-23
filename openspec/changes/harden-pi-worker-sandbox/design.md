## Context

See proposal.md for why. Constraints that shape the how:

- Runtime is **pi** spawned by Translator: `pi -p BRIEF --mode json --no-session`. No session, no resume, no AX runner.
- Pull-claim stays. Fence is around that loop, not a push Task YAML.
- Proxy already authenticates virtual keys (`IVirtualKeyStore.FindAsync` / `RemoveAsync`); store is config-seed only. Claim body has no key today.
- `PiRunner` does not set child env; pi inherits the container.
- Docker `HostConfig` is network-mode only; Kubernetes Jobs have requests, not limits, and no NetworkPolicy.
- Project has `ProfilesGitUrl` only. Product clone has nowhere to live until `SourceGitUrl` exists.
- Cowork `worker-pools` already named `strong` vs `trusted-process`. This design is the floor for `strong` on today's 1:1 container.

## Goals / Non-Goals

**Goals:**

- One fence contract that Docker and Kubernetes both implement through `IComputeProvider`.
- Minted keys that die with the lease, stamped only onto the pi process.
- Prepare cwd before pi so the brief is not lying.
- Hardening that does not require gVisor.

**Non-Goals:** (design-level)

- A shared in-process key store across orchestrator replicas (v1 token store is already process-local; mint follows that until a later durable store).
- FQDN NetworkPolicy if the cluster CNI cannot do it — fall back to CIDR of resolved git host at start, or fail-closed.
- Rewriting pi itself. Extensions live in `comuki-worker-sdk`.

## Decisions

### D1. Isolation class `strong` = this 1:1 container

Do not invent a third name. Profiles with no class default to `strong`. `trusted-process` is unimplemented and MUST NOT be claimed.

**Alternative:** leave class unnamed until cowork. Rejected — two dictionaries.

### D2. Docker fence = internal compose network + attached egress services

Workers join `comuki-worker-net` with `internal: true` (no default route to the internet). Proxy, Nexus, and the orchestrator gRPC/HTTP endpoints attach to that network **and** the outer stack network. Workers reach them by compose DNS (`comuki-proxy`, `nexus`, `comuki-host`). Git HTTPS goes through the host of `SourceGitUrl`; if that host is not one of the attached services, Docker SHALL fail-closed unless `AllowUnfencedEgress` (typical local: git is github.com, which is not on the internal net).

For local git-to-GitHub, the supported dev paths are: (1) set the flag, or (2) run a git-https forwarder on the worker net (out of first slice). First code slice still ships the internal network + flag; clone against public GitHub on Docker Desktop requires the flag until the forwarder exists.

Kubernetes: NetworkPolicy default-deny egress, allow DNS, the proxy Service, Nexus, and the CIDR of `SourceGitUrl` resolved at start.

**Alternative:** iptables in the worker. Rejected — needs cap_net_admin, fights non-root.

**Alternative:** fail-open on Docker. Rejected — production compose would be silent.

### D3. Mint on claim, stamp in PiRunner, revoke on every terminal path

Claim 200 body gains `proxyBaseUrl` + `virtualKey` (plaintext once, like `COMUKI_WORKER_TOKEN` at container start). Translator keeps them in memory for the cycle and passes them as `ANTHROPIC_BASE_URL` / `ANTHROPIC_AUTH_TOKEN` on `ProcessStartInfo.Environment`. `IVirtualKeyStore` gains `MintAsync` (token, project, expiry, allowed models). `RemoveAsync` already exists. In-memory overlay next to config seed is enough for v1 (lease TTL bounds the window). Host restart drops minted keys — in-flight pi dies with the worker token anyway.

**Alternative:** stamp on container start. Rejected — warm pool / future slots would share one key.

**Alternative:** durable mint table now. Deferred; same as worker tokens.

### D4. Clone in Translator, HTTPS only, credential not in pi env

After claim, before spawn: `git clone --depth 1 --branch <ref> https://...` into `WorkingDirectory` (or a clean subdirectory that then becomes cwd). Credential from project settings secret ref via `ISecretResolver`, injected as `url.insteadOf` / extraheader in a throwaway git config file under a Translator-only path, deleted after clone. Pi never sees it.

`SourceGitUrl` / `SourceGitRef` on `Project`; git credential ref on `ProjectSettings`. Claim does not carry a caller-chosen URL.

**Alternative:** provider bind-mount. Rejected — k8s Job would need a PVC per run; Translator already clones profiles.

**Alternative:** SSH keys. Non-goal.

### D5. Limits + softening pack; RW rootfs

Profile (later) or provider options supply CPU/memory limits. Docker: `Memory`, `NanoCpus`. Kubernetes: requests **and** limits. Image USER non-root; Job `securityContext`: `runAsNonRoot`, `allowPrivilegeEscalation: false`, `capabilities.drop: [ALL]`, `seccompProfile: RuntimeDefault`. Docker: `User`, `CapDrop=ALL`, `SecurityOpt=no-new-privileges:true`.

RO rootfs deferred — pi/bun write tmp; needs a tmpfs map spike.

### D6. Conditions are journal events, not columns

Reuse `worker.reported` (or a dedicated condition payload) with names `WorkspacePrepared`, `EgressApplied`, `AgentRunning`. Dashboard already reads the journal. Reaper still keys off lease.

### D7. Drain is best-effort in Translator; packager stays

Translator calls `IRunArtifactStore` if the host injects it (or a small HTTP upload already used by workers). Failure → journal, still complete. Packager skips already-bundled prefixes.

### D8. First implementation slice vs later issues

Code order (does not shrink the spec):

1. Mint + stamp + revoke
2. Egress fail-closed (k8s NetworkPolicy + Docker internal net + flag)
3. Limits + hardening

Follow-ups: SourceGit + clone, conditions, drain, debug exec, pi-extensions, digest pin, fixture CLI.

## Risks / Trade-offs

- **[Risk] Docker Desktop cannot fence github.com without a forwarder** → Mitigation: fail-closed + documented `AllowUnfencedEgress` for local; first slice does not pretend public git is fenced on Docker.
- **[Risk] Minted keys die on host restart** → Same as worker tokens; lease reaper requeues.
- **[Risk] Git credential in Translator process memory** → Short-lived, not exported to pi, not logged. Align with secret-resolver lifetime.
- **[Risk] non-root breaks current bun image** → Image change is part of slice 3; if pi cannot run, slice 3 does not merge.
- **[Risk] Cowork later wants slots** → Stamp-on-claim and per-execution cwd already match slot identity; container-lifetime keys would not.

## Migration Plan

Additive. Existing deploys without `SourceGitUrl` keep claiming until workspace prepare lands — then they fail loud until the field is set. Egress fail-closed will stop unfenced production starts: operators must ship NetworkPolicy (k8s) or the internal network (compose) or temporarily set the flag. Rollback: flag + previous image; minted-key code is unused if claim omits the new fields (Translator requires them once the slice ships — **BREAKING** for old Translators against a new host: rolling update host then workers).

## Open Questions

None that change specs. Git-https forwarder for fenced Docker→GitHub is a follow-up issue, not a spec hole (dev uses the flag).

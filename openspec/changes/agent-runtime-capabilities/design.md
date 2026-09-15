## Context

v1 ships with the brain / chat / memory / compute / intake stack; the
` ` `ISSecretResolver` and its four providers are wired into the host,
brain, intake, and proxy. What is missing for agent autonomy:

1. Workers cannot reach project credentials — `ComputeStartRequest.Env`
   is the only container-env channel, and refs are opaque strings.
2. There is no catalog of secret definitions — secrets are
   resolution-time only.
3. Discovery is a worker profile, not a brain-facing capability.
4. Memory recall is global-only at the brain call boundary.

See `proposal.md` for motivation; this design covers the **how**.

## Goals / Non-Goals

**Goals**

- One place to define, store, audit, and resolve secrets — local
  (encrypted in our DB) and external (ref to upstream KMS).
- A path that gets a value from the catalog into a worker container
  in a provider-agnostic way.
- Project-scoped recall at brain call time, fed by trusted digest
  assembly, never by raw tool args.
- Discovery as a brain-callable MCP tool that runs the existing
  `explore-readonly` profile and persists findings.

**Non-Goals**

- KMS / SaaS envelope encryption (deferred to a follow-up change
  with its own `IKmsProvider` adapter).
- Auto-rotation, scheduled rotation, bulk import.
- External providers beyond the existing four
  (`vault`, `consul`, `env`, `file`).
- Replacing or rewriting the v1 chat / brain / intake / compute
  contracts. Everything here is additive.

## Decisions

### D1. Envelope encryption with AES-256-GCM + KEK version

Each `SecretVersion.Ciphertext` is sealed with a per-version random
DEK; the DEK is sealed with the current KEK and stored alongside
the ciphertext as `keyVersion = N`. The KEK itself is loaded from
`COMUKI_SECRETS_KEK` (env, 32 bytes base64) on self-hosted
deployments. A future `IKmsProvider` adapter (Vault Transit, AWS
KMS) replaces the env loader without changing the on-disk shape.

**Alternative considered:** seal values directly with the KEK. Rejected
because rotating the KEK then requires re-encrypting every version of
every secret under load. With an envelope, KEK rotation = re-wrap every
still-current DEK, leaves old ciphertext alone until the version is
superseded.

**Alternative considered:** use Postgres `pgcrypto`. Rejected —
`pgcrypto`'s `encrypt` / `decrypt` is synchronous, blocks on every
row, and we want decryption centralised under `ISecretResolver` so
plaintext lifetime is bounded by a single stack frame.

### D2. `ISSecretProvider` adds `DbSecretProvider`; signature unchanged

`DbSecretProvider` joins the existing provider list behind the same
`ISSecretProvider.Scheme` / `ISSecretResolver.ResolveAsync` contract.
Existing callers — `OidcClientSecrets`, intake providers,
`ProxyTransforms`, `BrainSecretsInstaller`, embedding client — pick
up the new backend transparently. No interface change.

**Alternative considered:** introduce `ISecretDefinitionService` as a
parallel API to the resolver. Rejected — two paths means two audit
stories, two RBAC stories, and two sets of unit tests. Keep one.

### D3. Worker env injection is provider-side; worker contract is opaque env

`ComputeStartRequest.SecretRefs` lists `(EnvName, Reference, Required)`
triples. The Docker provider resolves each ref before `docker run`
and exports via `-e`; the Kubernetes provider creates a per-worker
`Secret` and mounts via `volumeMounts` at `/run/secrets/{EnvName}`. The
worker runtime contract stays the same — secrets are env entries.
The runtime adds a logger filter that drops any record whose payload
matches an injected value (best-effort).

**Alternative considered:** mount a single `/run/secrets.json` and
parse on worker boot. Rejected — violates the principle of least
surprise; most tooling expects env vars, not a JSON blob, and the
worker runtime already deals with env.

**Alternative considered:** resolve refs lazily inside the worker via
a sidecar that calls back to the orchestrator. Rejected for v1 —
adds network roundtrips to every secret read, complicates failure
modes (network blip → secret gone), and makes rotation in the
running worker a different problem from the same in catalog.

### D4. RBAC layers onto existing `Permissions.cs` and `RoleMatrix`

Five new keys (`secret:read`, `secret:use`, `secret:reveal`,
`secret:write`, `secret:delete`) + one for discovery (`discovery:run`)
land in `Permissions.cs`. The `RoleMatrix` extends with platform /
project rows; personal scope is per-user (owner == subject) and does
not need a new role. The existing `PermissionDemandStartupValidator`
already enforces that every demanded key is declared in the matrix,
so a missing key on a controller is a startup failure, not a runtime
gap.

### D5. Memory scope flows through `BrainRequest`, not through tool args

`BrainRequest` gains `ScopeKind` / `SubjectId`. The brain runtime
calls `IMemoryStore.SearchAsync(scope, subjectId)` once before the
model loop and prepends the digest. `memory.search` / `memory.write`
/ `memory.forget` tools remain global-only — the security argument
that closes them off in `BrainAgent.cs:135` (an unsafe call is
unrepresentable) keeps holding. Personal / project isolation lives
in the digest, not in free-form tool args.

**Alternative considered:** make `memory.search` scope-aware and
let the model pick the scope. Rejected — prompt-injection surface
too large for a brain with no subject context.

### D6. Discovery is a worker run on `explore-readonly`, not a host engine

`discovery.scan` builds a `brief` argument for the existing
`explore-readonly` profile and starts a normal `WorkerRun`. The host
parses the report and writes `MemoryFact` rows. The runs dashboard
already shows discovery runs (`reason = discovery.scan`), they
share the worker pool and the project's `MaxConcurrent`, and they
hit the same claims / leases / retries / scale policy as any other
run. No new pool, no new quota, no new run lifecycle.

**Alternative considered:** a host-side scanning module that walks
the repo with `IFileSystem`. Rejected — discovery needs the LLM to
*interpret* what it sees, not just list files. Embedding the worker
profile gets us the LLM for free, plus the same policies we have for
any other worker run.

## Risks / Trade-offs

- **[Risk] Production deploy with `COMUKI_SECRETS_KEK` unset** →
  `ProductionSecretAudit` gate extends to require the env var and a
  length check (32 bytes). Startup fails fast with a typed error.
- **[Risk] KEK rotation while older versions are still referenced** →
  version envelope: each `SecretVersion` carries its `KeyVersion`;
  the resolver keeps the active KEK set in-memory and falls back to
  older keys only to decrypt superseded versions (which are read
  for audit, never by the live resolver path).
- **[Risk] Secrets in worker logs / crash dumps** → logger filter
  on the runtime; docs in the worker template warn against
  `console.log(process.env.X)`; existing workers built from
  `control-plane/profiles/explore-readonly` already have minimal
  logging discipline.
- **[Risk] MCP tool callers leaking secrets through chat** → the
  `secrets.list` and `secrets.get` tools return metadata only; the
  brain context assembly does not include the catalog; the `reveal`
  endpoint is RBAC-gated and audit-logged.
- **[Trade-off] Worker restarts on secret rotation** → documented in
  the secret detail page; the next worker start picks up the new
  value. Live re-injection is out of scope.
- **[Trade-off] Discovery runs count against `MaxConcurrent`** →
  intentional; discovery is a real worker run with the same failure
  modes. A discovery run that saturates the project is a signal, not
  a bug.

## Migration Plan

- **Add** the new module `Comuki.Modules.Secrets` and the new
  capability `secrets`. Migration lives in `__comuki_secrets` per
  the same pattern `Memory` and `Chat` use.
- **Add** `DbSecretProvider` alongside the existing
  `EnvSecretProvider` / `FileSecretProvider` / `VaultSecretProvider` /
  `NullSecretProvider`. Existing callers pick it up at startup; no
  config flag.
- **Extend** `ComputeStartRequest` with `SecretRefs`; both providers
  extend their `StartAsync` implementations to resolve before
  starting the container. Wire-format additions only.
- **Extend** `BrainRequest` with `ScopeKind` / `SubjectId`; callers
  in `Comuki.Host.Chat` (chat graph) and `Comuki.Host.Brain` (any
  orchestration call) pass the scope when they have one.
- **Roll out** in four phases, ordered by dependency:
  1. **Memory gap close** (per-project facts at brain call)
  2. **Discovery v0** (MCP tool + slash command + memory writes)
  3. **Secret store v0** (catalog + RBAC + audit + `DbSecretProvider`)
  4. **Worker secret injection** (`ComputeStartRequest.SecretRefs` +
     Docker + K8s providers)
- **Rollback** for each phase is independent — phases 1–3 leave
  the existing `ISecretResolver` and `ComputeStartRequest.Env` paths
  intact; phase 4 is opt-in per project via the project's settings
  flag `Compute:InjectSecrets = true` (default `false` for one
  release) so an incident on phase 4 can be isolated without
  unrolling phases 1–3.

## Open Questions

- **Vault dynamic DB credentials.** Vault can issue per-worker
  credentials with TTL. We model secrets as static for v1; dynamic
  credentials would require worker-side renewal logic and are
  deferred. Resolved at `IKmsProvider` design time, not now.
- **Discovery brief size cap.** A 200 KiB `brief` argument per
  worker run is fine for the existing pool; a multi-megabyte brief
  (e.g. embedding the full repo tree) would inflate the claim
  payload. We default to `quick` / `secrets` / `integrations` modes
  with curated topic lists; `full` mode is bounded to 200 KiB.
- **Cross-project secret sharing.** A project that wants to
  reference another project's secret is not modelled — the
  recommendation is to copy the secret into the consumer project or
  make it `platform`-scoped. We add this only if a real use case
  appears.
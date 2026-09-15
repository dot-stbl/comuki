## Why

Today Comuki can think (brain), remember (`memory_facts` + pgvector + digest),
and run ephemeral workers — but workers start with only the `COMUKI_*`
contract env and have **no path to project credentials**. The brain resolves
secrets via `ISecretResolver`, but `ComputeStartRequest.Env` is the only
way anything reaches a container, and refs are written as opaque strings
(`secretEnvRef`) with no catalog. Result: workers cannot deploy, push,
or call third-party APIs without help from the host.

We want the chat operator to say "add the new project", "deploy to prod",
"rotate the Jira token" — and have the brain scan, remember, plan, and
execute through workers that already carry what they need. This change
turns secrets, memory, and discovery into agent-facing capabilities
rather than operator-only configuration.

## What Changes

- **New `secrets` capability.** First-class `ISecretDefinition` with
  envelope encryption (AES-256-GCM, DEK wrapped by KMS or local root
  key), scopes `Scope, SubjectId, OwnerUserId`, `Source = local | external`,
  audit log (`create`, / /, `rotate`, `rename`, `reveal`, `delete`,
  `grant`, `revoke`, `use_error`; never `use_success`), new
  `secret:read` / `secret:use` / `secret:reveal` / `secret:write` /
  `secret:delete` permissions in `identity`, CRUD API on
  `/api/v1/secrets`, MCP tools `secrets.list` / `secrets.get-meta`.
  `ISecretResolver` gains a `DbSecretProvider` alongside the existing
  env / file / vault providers; signature is unchanged.
- **New `discovery` capability.** MCP tool `discovery.scan` runs the
  existing `explore-readonly` worker profile with a project-scoped brief,
  captures the report into `memory_facts` (scope = `project`, source =
  `run`). The dashboard gains a `/discover` route and a chat slash
  command; the brain can emit a discovery plan as a `Proposal`.
- **Compute contract gains `SecretRefs`.** `ComputeStartRequest`
  carries a ` ` `SecretRefs: IReadOnlyList<SecretRefSpec>` field. The
  Docker provider runs an init step that resolves each ref via
  `ISecretResolver` and exports the value as env; the Kubernetes
  provider mounts each resolved value through `valueFrom.secretKeyRef`
  on a transient `Secret`. Workers see `COMUKI_SECRET_*` env entries
  matching the names of refs they requested.
- **Memory gains per-scope recall at brain call.** `BrainRequest`
  adds optional `ScopeKind` / `SubjectId`; `BrainAgent.RunAsync` calls
  `IMemoryStore.SearchAsync(scope, subject)` before the loop, so the
  digest carries project-specific facts without exposing tool args to
  the model. `discovery.scan` results land as `Project`-scope facts
  with `source = run`.
- **Chat gets two slash commands and three MCP tools.** `/memory` lists
  visible facts and offers forget (requires `memory:write`);
  `/discover` triggers a discovery run on the current project.
  MCP: `secrets.list`, `secrets.get-meta`, `discovery.scan`.

## Capabilities

### New Capabilities

- `secrets`: first-class secret catalog with envelope encryption,
  RBAC, audit, and `DbSecretProvider` integrated with
  `ISecretResolver`.
- `discovery`: scan-a-project flow built on the `explore-readonly`
  worker profile; findings land in `memory_facts`.

### Modified Capabilities

- `compute`: `ComputeStartRequest` carries `SecretRefs`; Docker and
  Kubernetes providers resolve them at start.
- `worker-runtime`: workers receive `COMUKI_SECRET_*` env entries from
  resolved refs; resolver runs under a worker-scoped subject.
- `memory`: `BrainRequest` exposes per-scope recall; `discovery.scan`
  writes project facts; `MemoryDigest.Build` accepts a scope.
- `chat`: `/memory` and `/discover` slash commands; three new MCP
  tools.
- `identity`: new permissions `secret:read`, `secret:use`,
  `secret:reveal`, `secret:write`, `secret:delete`; grant table for
  personal-secret sharing.

## Impact

- New migrations in `__comuki_secrets` (ciphertext DEKs,
  `secrets`, `secret_versions`, `secret_grants`, `secret_audit_log`).
- New C# project `Comuki.Modules.Secrets`; `Comuki.Modules.Memory`
  and `Comuki.Modules.Compute` get additive fields.
- Dashboard gets `/secrets` (list + detail + audit) and `/discover`
  (run + history) routes; existing forms (`sources`, `init-wizard`)
  switch from `TextField` to a `SecretPicker`.
- Wire-format additions only (no breaking changes to existing
  `BrainRequest` / `ComputeStartRequest` callers — fields are
  optional).
- KMS adapter is **deferred** (SaaS). Self-hosted uses a KEK from
  `COMUKI_SECRETS_KEK` env var; `ProductionSecretAudit` gate
  extended to require it.

## Non-goals

- KMS / envelope encryption for SaaS deployments (deferred; we ship
  the `IKmsProvider` interface and the local implementation only).
- Auto-rotation on a schedule (future; we manual rotation via UI
  + audit event).
- Bulk import of env / Vault / AWS secrets (future; we manual
  create + grant).
- External providers beyond `vault` / `consul` (AWS / GCP / 1Password
  / Bitwarden are deferred).
- Personal-secret `share-by-link` with TTL (future).
- Any change to the v1 chat / brain / intake / compute contracts
  beyond additive fields.

## Version

Post-v1.0 capability. Lands as a single change with three
implementation phases (A memory / B discovery / C secrets-store + D
worker injection) tracked in `tasks.md`; phase order is A → B → C → D.
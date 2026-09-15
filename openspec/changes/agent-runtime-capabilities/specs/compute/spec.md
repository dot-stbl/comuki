## ADDED Requirements

### Requirement: ComputeStartRequest carries SecretRefs

`ComputeStartRequest` SHALL add an optional `SecretRefs` field of
type `IReadOnlyList<SecretRefSpec>`. `SecretRefSpec` carries
`EnvName` (string, the env var the worker will receive),
`Reference` (string, the reference passed to `ISecretResolver`),
and optional `Required` (bool, default `true`). When `Required`
is true and the resolver returns `SecretRefUnsetException`, the
provider SHALL abort the start and surface a typed error to the
caller. The field is additive; existing callers that omit it see
no behavior change.

#### Scenario: Missing required ref aborts start

- **WHEN** a `ComputeStartRequest` carries a `SecretRefs` entry with
  `Required = true` and `ISecretResolver.ResolveAsync(reference)`
  throws `SecretRefUnsetException`
- **THEN** the provider returns a typed `ComputeStartError` with
  `code = secret.required_unset` and does not start the container

### Requirement: Docker provider resolves refs on start

The Docker provider SHALL, before starting the container, iterate
`SecretRefs` and call `ISecretResolver.ResolveAsync(reference, ct)`
for each. Resolved values SHALL be exported as `docker run -e NAME=value`
on the resulting container. The init step SHALL run under the
caller's subject scope (no worker token yet — the host is starting
the container). Failures during resolution SHALL roll back the
start; no container SHALL be left running with partial secrets.

#### Scenario: Docker start with one secret ref

- **WHEN** `ComputeStartRequest.SecretRefs` contains
  `{ EnvName = OPENAI_API_KEY, Reference = local:<id> }`
- **THEN** the container's env includes `OPENAI_API_KEY` and no
  other ref entries are added

### Requirement: Kubernetes provider mounts secrets as K8s Secrets

The Kubernetes provider SHALL, before creating the Job, iterate
`SecretRefs` and call `ISecretResolver.ResolveAsync(reference, ct)`
for each. Resolved values SHALL be packaged into a `batch/v1` Job
that mounts a per-job `Secret` (named `comuki-ws-{worker-id}`)
via `volumeMounts` at `/run/secrets/{EnvName}`. The Job SHALL
delete the `Secret` resource in the `OnExit` cleanup hook.

#### Scenario: K8s mount path

- **WHEN** a worker starts with `SecretRefs = [OPENAI_API_KEY]`
- **THEN** the worker process sees `OPENAI_API_KEY` at
  `/run/secrets/OPENAI_API_KEY` and the K8s Secret is removed
  after the Job completes

### Requirement: Resolution runs under the start subject

The `ISecretResolver.ResolveAsync` call from a compute provider
SHALL run under the subject that issued the `ComputeStartRequest`
(typically the orchestration runtime or the scale supervisor under
`AsSystem`). Resolution SHALL NOT run under any worker token, because
no worker exists yet. This means the resolution path inherits the
caller's RBAC; orchestration code MUST verify that the requested
refs are reachable before issuing the start request.

#### Scenario: Provider cannot bypass RBAC

- **WHEN** the orchestration runtime lacks `secret:use` on a ref
  it tried to inject
- **THEN** resolution throws and the start is rolled back; the
  RBAC denial is logged as a `use_error` audit event with consumer
  `compute`
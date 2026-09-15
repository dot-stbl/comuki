## Purpose

First-class catalog of secrets — local (encrypted in Comuki's database)
and external (ref to Vault / env / file — we never see the plaintext) —
with rotation, audit, and per-scope RBAC. Provides the resolution path
that turns a `secretRef` into a value an agent or worker can use,
without ever exposing the value through chat, the dashboard list, or
audit.

## ADDED Requirements

### Requirement: Secret definition entity

A secret SHALL be persisted as `Secret` with: id, `Name` (unique within
its scope), `Category` (`model` / `source` / `proxy` / `oidc` / `storage`
/ `generic`), `Source` (`local` | `external`), `ScopeLevel`
(`platform` | `project` | `personal`), `ScopeProjectId` (nullable),
`OwnerUserId` (nullable, set iff `ScopeLevel = personal`),
`ExternalRef` (nullable, present iff `Source = external`,
e.g. `vault:secret/data/bravo/openai#token`), `ExternalProvider`
(nullable), `Rotatable` (bool), `Tags` (string array),
`CreatedBy`, `CreatedAt`, `RotatedAt` (nullable), `ExpiresAt`
(nullable), `DeletedAt` (nullable, soft delete), `LastHealthCheck`
(nullable, for external), `LastHealthOk` (nullable bool).

#### Scenario: Personal secret has owner

- **WHEN** a secret is created with `ScopeLevel = personal`
- **THEN** `OwnerUserId` is set to the creating user's id

#### Scenario: External secret carries ref

- **WHEN** a secret is created with `Source = external`
- **THEN** `ExternalRef` is non-empty, `ExternalProvider` is set, and
  `Rotatable` reflects whether the provider supports rotation

### Requirement: Secret versions

Local secrets SHALL persist versions in `SecretVersion`:
`id`, `SecretId`, `Version` (int, monotonic per secret),
`Ciphertext` (bytea, AES-256-GCM), `Nonce` (bytea), `KeyVersion`
(int — KEK envelope version), `CreatedBy`, `CreatedAt`,
`SupersededAt` (nullable). Creating a new value SHALL insert a new
row and stamp `SupersededAt = now()` on the prior current version.

#### Scenario: Rotation keeps history

- **WHEN** a local secret is rotated
- **THEN** the previous version's `SupersededAt` is set, and the new
  version becomes the current value used by `Resolve`

### Requirement: Envelope encryption

The platform SHALL encrypt secret values with AES-256-GCM using a
data-encryption key (DEK) that is itself wrapped by a key-encryption
key (KEK). On self-hosted deployments the KEK SHALL be loaded from
`COMUKI_SECRETS_KEK` (32 bytes, base64); production SHALL refuse to
start when the variable is unset or shorter than 32 bytes. Dec
Dec
- Only `ISecretResolver` MAY decrypt ciphertext; decrypted values
  SHALL live in a single local variable and never be logged, returned
  to chat context, or returned to a list endpoint.

#### Scenario: Production refuses missing KEK

- **WHEN** the orchestrator starts with `ENV = Production` and
  `COMUKI_SECRETS_KEK` is unset
- **THEN** startup fails fast with a typed configuration error

### Requirement: Resolve by reference

`ISecretResolver.ResolveAsync(reference, ct)` SHALL accept a reference
of the form `kind:name` (e.g. `local:<secret-id>`,
`vault:secret/data/bravo/openai#token`, `env:GH_TOKEN`,
`file:/etc/comuki/gh-token`) and return the resolved plaintext or a
typed error: `SecretRefUnsetException` when the source carries no
value, `SecretRefFormatException` when the kind is unknown. The
composite resolver SHALL dispatch by kind prefix to the matching
provider; `DbSecretProvider` joins the existing
`EnvSecretProvider` / `FileSecretProvider` / `VaultSecretProvider` /
`NullSecretProvider`.

#### Scenario: Resolved value is single-use

- **WHEN** a caller receives a resolved value
- **THEN** the value is bound to a single local; subsequent
  resolution produces a separate value

### Requirement: External health probe

External secrets SHALL be probed on a 5-minute interval. The probe
MUST resolve through `ISecretResolver` and MUST NOT log, persist, or
return the resolved value. A failure SHALL stamp `LastHealthCheck` and
set `LastHealthOk = false`; three consecutive failures SHALL mark the
secret `unreachable` in list responses.

#### Scenario: Unreachable external secret

- **WHEN** an external secret fails three probes in a row
- **THEN** the dashboard surfaces it with status `unreachable` and the
  audit log records a `use_error` event with the probe consumer

### Requirement: Audit log

Audit events SHALL be written to `SecretAuditLog`: id, `SecretId`,
`Action` (`create` | `rotate` | `rename` | `reveal` | `delete` |
`grant` | `revoke` | `use_error`), `ActorSubject` (subject ref),
`ActorIp` (inet), `Consumer` (`brain` | `proxy` | `oidc` | `source`
| `discovery` | `ui`), `At` (timestamptz). The audit log MUST NOT
record successful service use; that lives in the `comuki.secrets.*`
OTel metrics. A human `reveal` MUST always write an audit event with
the actor's id, ip, and the consumer that requested the reveal.

#### Scenario: Service use is not audited

- **WHEN** the brain resolves `OPENAI_API_KEY` to call the lead model
- **THEN** no `SecretAuditLog` row is written; the OTel counter
  `comuki.secrets.resolved{result = "ok"}` increments instead

### Requirement: RBAC

New permissions SHALL be added to `Permissions.cs`:
`secret:read`, `secret:use`, `secret:reveal`, `secret:write`,
`secret:delete`. The matrix SHALL allow:
`secret:read` for `platform` requires `PlatformAdmin` / `Operator`;
for `project` requires membership in the project with `ProjectRead`
or higher; for `own personal` requires authenticated subject.
`secret:use` mirrors `secret:read` plus `Member` and `ProjectAdmin`
on `project`. `secret:reveal` on `platform` requires `PlatformAdmin`
/ `Operator`; on `project` requires `ProjectAdmin`; on `own personal`
requires the owner. `secret:write` on `platform` requires
`PlatformAdmin`; on `project` requires `ProjectAdmin`; on
`own personal` requires the owner. `secret:delete` mirrors write
permissions; deletes are soft and the row stays recoverable for
7 days.

#### Scenario: Member cannot reveal a platform secret

- **WHEN** a user with role `Member` calls `GET /api/v1/secrets/{id}`
  with a `reveal` query on a platform-scoped secret
- **THEN** the endpoint returns 403 `permission.denied`

### Requirement: Personal-secret grants

The owner of a personal secret SHALL be able to grant another user
`secret:use` (read metadata + reference, never reveal) on that secret
via `POST /api/v1/secrets/{id}/grants`. Grants SHALL be persisted in
`SecretGrant` (id, `SecretId`, `GranteeUserId`, `Scope` (`use`),
`GrantedBy`, `GrantedAt`, `RevokedAt`). Grants SHALL appear in the
grantee's secret list tagged `by grant`. Granting and revoking SHALL
write `grant` / `revoke` audit events.

#### Scenario: Grant reveals the secret name only

- **WHEN** Alice grants Bob use of her `MY_OPENAI_PRO` personal
  secret
- **THEN** Bob's secret list contains that row with `by grant from
  alice@`, and the value remains hidden behind `secret:reveal` Alice
  has not granted

### Requirement: List endpoint

`GET /api/v1/secrets` SHALL return the secrets visible to the
caller's subject, with: id, name, category, source, scope level,
scope project id (nullable), owner user id (nullable), tags, version,
created at, rotated at, expires at, last health check, health ok.
It SHALL NOT include the resolved value. Filters: `category`,
`source`, `scopeLevel`, `scopeProjectId`, `tag`, `q` (matches name).

#### Scenario: List never returns values

- **WHEN** any caller lists secrets
- **THEN** no field in the response carries a resolved value

### Requirement: Reveal endpoint

`POST /api/v1/secrets/{id}/reveal` SHALL return the resolved value of
a local secret in the response body exactly once, write a `reveal`
audit event, and require `secret:reveal`. External secrets SHALL
return `409` with code `secret.external.not_revealable` — the value
lives outside Comuki.

#### Scenario: External secret refuses reveal

- **WHEN** a caller reveals an external secret
- **THEN** the endpoint returns 409 with `code = secret.external.not_revealable`
  and no audit event is written (the reveal never happened)

### Requirement: Production-secret audit gate

The existing `ProductionSecretAudit` gate SHALL additionally refuse to
start in `ENV = Production` when `COMUKI_SECRETS_KEK` is unset or
shorter than 32 bytes. Dev / test environments MAY auto-generate a
session-scoped KEK with a clear log line warning that secrets are not
durable across restarts.

#### Scenario: Dev auto-KEK warning

- **WHEN** the orchestrator starts with `ENV = Development` and
  `COMUKI_SECRETS_KEK` unset
- **THEN** startup succeeds with a warning log
  `secrets.keek.autogenerated`; the in-memory KEK dies on restart
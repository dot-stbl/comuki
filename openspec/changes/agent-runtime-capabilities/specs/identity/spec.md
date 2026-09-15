## ADDED Requirements

### Requirement: Secret permission vocabulary

`Permissions.cs` SHALL declare five new permission keys:
`secret:read`, `secret:use`, `secret:reveal`, `secret:write`,
`secret:delete`. The permission catalog at startup SHALL reject any
endpoint that demands one of these keys without the key being in
the role matrix (existing `PermissionDemandStartupValidator`).
`RoleMatrix` SHALL declare each new key on the appropriate roles:

- `secret:read` (platform): `PlatformAdmin`, `Operator`.
- `secret:read` (project): `PlatformAdmin`, `Operator`,
  `ProjectAdmin`, `Approver`, `Member`, `Viewer` — gated on project
  membership by the existing `PermissionEvaluator`.
- `secret:use` (platform): `PlatformAdmin`, `Operator`.
- `secret:use` (project): `PlatformAdmin`, `ProjectAdmin`,
  `Member` — project-membership gated.
- `secret:reveal` (platform): `PlatformAdmin`, `Operator`.
- `secret:reveal` (project): `PlatformAdmin`, `ProjectAdmin`.
- `secret:write` (platform): `PlatformAdmin`.
- `secret:write` (project): `PlatformAdmin`, `ProjectAdmin`.
- `secret:delete` (platform): `PlatformAdmin`.
- `secret:delete` (project): `PlatformAdmin`, `ProjectAdmin`.

Personal scope uses existing per-user checks (owner == subject) and
does not need new role entries.

#### Scenario: New endpoint demands are validated at boot

- **WHEN** the host starts with a controller that demands
  `secret:reveal` and no role in `RoleMatrix` carries it
- **THEN** `PermissionDemandStartupValidator` refuses to start and
  reports the undeclared key

### Requirement: Discovery permission

`Permissions.cs` SHALL declare `discovery:run`. The role matrix SHALL
assign `discovery:run` to `PlatformAdmin`, `Operator`, `ProjectAdmin`,
`Member` for the project's members.

#### Scenario: Approver cannot trigger discovery

- **WHEN** a user with role `Approver` (and no `ProjectAdmin` grant)
  on project `<p>` calls a discovery endpoint
- **THEN** the endpoint returns 403 `permission.denied`

### Requirement: Personal grants table

The platform SHALL add a `SecretGrant` table — id, `SecretId`
(uuid fk to `secrets.id`, indexed), `GranteeUserId` (uuid),
`Scope` (`use`), `GrantedBy` (subject ref), `GrantedAt`
(timestamptz), `RevokedAt` (timestamptz, nullable). A grant SHALL
add `secret:use` for the grantee on the matching personal secret.
Grants SHALL NOT carry `secret:reveal`; that stays with the
owner.

#### Scenario: Grant does not confer reveal

- **WHEN** Alice grants Bob `use` on her personal secret
- **THEN** Bob can list and reference the secret; calling
  `POST /secrets/{id}/reveal` for it returns 403
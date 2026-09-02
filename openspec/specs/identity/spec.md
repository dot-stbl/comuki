# Identity Specification

## Purpose

Defines users, `ck_` API keys, role assignments (subject × role × scope), the compiled permission catalog and its enforcement filter, and OIDC account linking. Permissions are the action axis; scope (platform or one project) is the object axis. Roles are fixed in code — the database holds assignments only.

## Requirements

### Requirement: User accounts with own store

User accounts SHALL live in their own `users` table behind a module-owned store — NOT ASP.NET Identity's `IdentityDbContext`. Passwords SHALL be hashed with `PasswordHasher<User>` from `Microsoft.Extensions.Identity.Core`. Email SHALL be stored trimmed and lower-cased with a unique index. An account SHALL carry `TokensVersion` (a security-stamp counter), a `Disabled` flag, and timestamps; `PasswordHash` is null for OIDC-only accounts. Disabling an account SHALL also bump its tokens version.

#### Scenario: OIDC-only account
- **WHEN** an account is provisioned via OIDC linking
- **THEN** its password hash is null and local password login refuses it

### Requirement: ck_ API key tokens

An API key token SHALL be `ck_` + an 8-character public prefix (lowercase letters and digits) + a 43-character base64url secret (256 bits). Parsing SHALL never throw: a malformed bearer is an authentication failure, not an exception. Only the public prefix and the `HMAC-SHA256(pepper, token)` digest (lowercase hex) SHALL be stored — never the secret. The pepper SHALL come from the `COMUKI_IDENTITY_APIKEY_PEPPER` environment variable (dev-only default when unset; rotation invalidates every stored digest by design).

#### Scenario: Malformed bearer
- **WHEN** a request presents a bearer that is not a well-formed `ck_` token
- **THEN** authentication fails with "malformed api key" and no store lookup happens

### Requirement: API key show-once and burned prefixes

The plaintext key SHALL be returned exactly once at issue time. The public prefix SHALL be unique across ALL keys ever issued — including revoked ones (the prefix stays burned for audit). Revocation SHALL be idempotent and keep the row.

#### Scenario: Prefix never reused
- **WHEN** a key is revoked and a new key happens to generate the same prefix
- **THEN** the unique index rejects the insert

### Requirement: API key authentication semantics

The API-key scheme SHALL accept `Authorization: Bearer ck_…`: resolve the row by prefix (one indexed lookup), verify the HMAC constant-time, and refuse revoked keys, unverifiable digests, and keys whose owner is missing or disabled. On an unknown prefix the handler SHALL still pay the HMAC cost against a dummy digest so timing cannot confirm guessed prefixes. A successful authentication SHALL build the same claim grammar as the cookie scheme plus the api-key marker claim, so the key itself is the RBAC subject.

#### Scenario: Disabled owner closes all keys
- **WHEN** a key's owner account is disabled
- **THEN** every one of that owner's keys fails authentication without individual revocation

#### Scenario: last_used throttle
- **WHEN** a key authenticates and `last_used_at` was refreshed within the throttle interval (default 5 minutes)
- **THEN** no write happens (write amplification control)

### Requirement: Role assignments (subject × role × scope)

An assignment SHALL bind a subject (a user or an API key — both first-class) to one of six fixed roles (`platform-admin`, `operator`, `project-admin`, `approver`, `member`, `viewer`) on a scope (platform-wide or exactly one project). The stored form is the kebab-case role key. Revocation SHALL be a timestamp, never a delete — the audit trail stays. The "one active assignment per subject+role+scope" invariant SHALL be enforced by TWO partial unique indexes over active rows: one for platform rows (`scope_project_id IS NULL`) and one for project rows (`scope_project_id IS NOT NULL`), because Postgres treats NULLs as distinct.

#### Scenario: Same role on three projects
- **WHEN** a user holds `member` on projects A, B and C
- **THEN** three assignment rows exist and all three contribute permissions

#### Scenario: Duplicate active grant rejected
- **WHEN** an active platform-scope grant of a role already active at platform scope is inserted
- **THEN** the partial unique index rejects it

### Requirement: Seniority guard on grants

A grant SHALL be legal only when the target role's seniority is at or below the granter's own highest seniority — equal seniority IS allowed (the bootstrap platform-admin must be able to re-grant its own role). The ladder (with gaps for future roles): platform-admin 100, operator 70, project-admin 60, approver 40, member 30, viewer 20. An escalating grant SHALL throw.

#### Scenario: Operator cannot grant platform-admin
- **WHEN** a subject whose highest role is operator (70) tries to grant platform-admin (100)
- **THEN** the grant is refused as escalation

### Requirement: Compiled permission catalog and role matrix

The permission vocabulary SHALL be declared in one place as `dot:verb` keys (e.g. `run:read`, `plan:approve`, `chat:use`, `settings:write`, `identity:write`, `platform:admin` — 23 keys total). The role→permissions map SHALL be compiled in code, not data: changing what a role can do is a commit and a release. Every declared key MUST be held by at least one role — a unit test holds that invariant.

#### Scenario: Roles are fixed
- **WHEN** a deployment wants a custom role
- **THEN** the answer is a code change, not a database row

### Requirement: Startup validation of demanded permissions

At boot, the host SHALL scan its controller assemblies for every `RequiresPermission` demand and verify each key is declared in the catalog. An undeclared (typo'd or never-matrixed) key SHALL fail the boot, not the first request.

#### Scenario: Typo'd demand
- **WHEN** an endpoint demands `run:reads`
- **THEN** the host refuses to start naming the demand and the known keys

### Requirement: Permission enforcement filter

A global resource filter SHALL enforce the `RequiresPermission` attribute on every MVC action: an anonymous caller on a demanding endpoint gets 401 `authentication.required`; an authenticated subject without the key gets 403 `permission.denied` — both as `application/problem+json` with a stable `code` extension. Endpoints without the attribute are open. The attribute carries exactly one key ("A and B" is a third declared key; "A or B" has no honest reader). The object axis is not the filter's business — out-of-scope rows surface as 404 downstream. Permission evaluation SHALL cache a subject's effective authorization for at most 30 seconds and invalidate eagerly on grant/revoke.

#### Scenario: Anonymous on demanding endpoint
- **WHEN** no credentials accompany a request to an endpoint demanding `plan:read`
- **THEN** the answer is 401 with code `authentication.required`

#### Scenario: Missing permission
- **WHEN** a viewer-role subject calls an endpoint demanding `settings:write`
- **THEN** the answer is 403 with code `permission.denied`

#### Scenario: Platform grants widen globally
- **WHEN** a subject holds operator at platform scope
- **THEN** its permission set applies in every project, while project-scope grants apply only inside their project

### Requirement: OIDC providers and account linking

OIDC SHALL be opt-in per deployment via a possibly-empty provider list (`auth:oidc:providers`), each with a unique `Name`, `Authority`, `ClientId` and the NAME of the environment variable holding the client secret (secrets never live in config files; a missing secret env fails startup for that provider). The linker SHALL resolve an external identity (provider + `sub` claim + email) to a local account in priority order: an existing link wins; else a local account with the matching email is linked; else a password-less account is provisioned and linked. The `sub` and email claims are required — a ticket without them fails linking. The callback SHALL rewrite to the versioned API surface and exchange the external ticket for the local cookie grammar; permissions stay in Comuki assignments — the IdP is never the RBAC source.

#### Scenario: Email match links silently
- **WHEN** an OIDC identity arrives whose email matches an existing local account with no prior link
- **THEN** the existing account is linked (not duplicated) and used

#### Scenario: Brand-new identity provisioned
- **WHEN** no link and no matching email exist
- **THEN** a password-less account is created, linked, and reported as created

### Requirement: Cookie sessions with tokens_version validation

The cookie scheme (default `comuki.auth`, 7-day sliding expiry, HttpOnly, SameSite=Lax) SHALL validate on every request that the account exists, is not disabled, and its `TokensVersion` matches the stamp embedded in the cookie; otherwise the principal is rejected. Password change and disable bump the version — killing every outstanding session. API keys are unaffected (they carry no stamp). A request bearing `Authorization: Bearer` SHALL forward to the API-key scheme instead of cookie authentication.

#### Scenario: Password change kills sessions
- **WHEN** a user's password is changed
- **THEN** every cookie issued before the change fails validation at its next request

### Requirement: Ambient subject scope (object axis)

Authorization's object axis SHALL be an ambient `SubjectScope` on the async flow: unrestricted (platform-scope / system) or confined to an explicit set of project ids. Request middleware SHALL establish the scope from the authenticated subject's assignments before MVC/minimal endpoints run. Background workers and the worker runtime SHALL declare `ISubjectScopeAccessor.AsSystem("<consumer>")` inside a `using` for the duration of their work. Reading `Current` with no established scope SHALL throw — there is no default empty or unrestricted fallback for filters.

#### Scenario: Missing scope fails loud
- **WHEN** a DI-built DbContext with a scope accessor runs a query with no scope established
- **THEN** accessing the ambient scope throws rather than returning zero or all rows

#### Scenario: System consumer
- **WHEN** the lease reaper runs inside `AsSystem("lease-reaper")`
- **THEN** query filters see every project for that flow and restore the previous scope on dispose

### Requirement: Out-of-scope rows are absent

Persistence contexts that carry project-scoped aggregates (orchestration runs today; projects similarly) SHALL apply EF global query filters so out-of-scope rows are invisible. Downstream APIs SHALL surface misses as 404, never as 403 "deny" for the object axis. Permission filters remain responsible only for the action axis (`RequiresPermission`).

#### Scenario: Scoped list omits foreign projects
- **WHEN** a subject confined to project A lists runs
- **THEN** runs of project B are absent from the result set

## ADAPTER Notes

Tables: `users`, `api_keys`, `role_assignments`, `oidc_links` (unique `(provider, sub)`), with a module-private migrations history (`__comuki_identity`) so multiple contexts migrate one database without colliding. The bootstrap admin (see host) writes through the same create/grant handlers.

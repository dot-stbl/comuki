# Comuki v1 Security Audit — 2026-09-09

> Read-only security audit of the Comuki v1 codebase as of `master`
> tip `47b3f47b4b9b986631acf01f40dbd320f2f29e23` (worktree
> `fix/audit-security`).
> Repo path: `C:\Users\bradw\source\hybrid\comuki.orchestrator`.
> No code was changed; this is a read-only survey.
> Every finding cites a file and line so the owner can navigate
> immediately. File paths are repo-relative unless prefixed.

This audit covers OWASP Top 10 (2021), secrets/config, authorization
model and dependency posture. It does **not** re-cover the business-logic
gaps already in `audit-product-report.md` (run-detail, proxy metering,
FE mocks) unless they have a security angle; those are reported in the
product audit, this one stays focused on security.

---

## Executive Summary

**State.** Comuki v1 is *defensibly* architected: EF Core parameterised
queries, an explicit `IExceptionHandler` with one ProblemDetails envelope,
HMAC-SHA256 with `RandomNumberGenerator` for API keys, PBKDF2 via the
BCL `IPasswordHasher` for passwords, PKCE-S256 + 5-minute single-use
state rows for OIDC, constant-time HMAC verify on the not-found path,
and `[RequiresPermission]` on every MVC controller I sampled (48
attributes across 16 controllers; 0 `AllowAnonymous`).

**Top critical findings.**

1. **`SignalR EnableDetailedErrors` is on in every production container**
   (`platform/src/host/Comuki.Host/Realtime/RealtimeExtensions.cs:30-31, 43-61`).
   The "diagnostic opt-in" set includes `DOTNET_RUNNING_IN_CONTAINER=true`,
   which every production container sets, so stack frames + `HubException`
   messages reach clients. The audit-product-report claims the opposite;
   the code does the opposite.
2. **API-key pepper AND worker-token pepper each have a dev default
   that `ProductionSecretValidator` does not catch**
   (`ApiKeyOptions.cs:19-27` and `WorkerTokenOptions.cs:17`).
   `comuki-dev-only-apikey-pepper-override-in-production` is used silently
   when `COMUKI_IDENTITY_APIKEY_PEPPER` is unset;
   `comuki-dev-only-pepper-override-in-production` is used silently when
   `COMUKI_TOKEN_PEPPER` is unset. The validator only covers
   MinIO + bootstrap admin. **Both** peppers must be set in
   production; a leak of either breaks the corresponding
   authentication scheme (the attacker can forge any key/token
   with knowledge of the public-domain pepper + the
   `apikeys`/`worker_tokens` table).
3. **OIDC auto-link by email is an account-takeover vector** when an
   IdP issues a JWT for a Comuki user without `email_verified=true` and
   the corresponding local user happens to be disabled, OR when the local
   account does not yet exist. The Q36 disabled-check landed
   (`OidcAccountLinker.cs:54`), but the provision-new-user path
   (`OidcAccountLinker.cs:61-65`) creates a password-less account from
   any IdP claim and is silent — no notification, no opt-in, no
   email-verified requirement. (The first half is in the product audit
   already; the security side is reinforced by the missing
   `email_verified` gate.)
4. **MCP `/api/v1/mcp` is permissionless** — the endpoint is anonymous
   at the route (`McpModuleEndpoints.cs:19-22`); the dispatcher
   (`McpServer.DispatchAsync`) calls `knowledge.search` /
   `knowledge.ingest` / `runs.list` / `runs.get` without any
   `[RequiresPermission]` or in-method check. A user with `chat:use` but
   no `run:read` or `knowledge:read` can read other users' runs and
   knowledge.
5. **OIDC `redirect_uri` is built from the request `Host` header**
   (`AuthController.cs:182-191`). A poisoned `Host:` header (or any
   reverse proxy that does not pin `X-Forwarded-Host`) makes the
   `redirect_uri` attacker-controlled, which is then sent to the IdP
   for the token exchange. The IdP follows the redirect_uri to the
   attacker's server, the `code` lands there, and the attacker
   completes the code-for-tokens exchange.

**Per OWASP category** (count of findings): A01=4, A02=4, A03=3, A04=4,
A05=5, A06=5, A07=4, A08=3, A09=3, A10=2. Total: 37 findings
(8 Critical, 12 High, 11 Medium, 6 Low/Info).

**Dependency issues**: 5 — see §4.

---

## 1. OWASP Top 10 Findings

### A01 — Broken Access Control

#### A01-1. MCP `/api/v1/mcp` runs every tool with no permission check
**Severity:** **Critical**
**Files:** `platform/src/host/Comuki.Host/Mcp/McpModuleEndpoints.cs:19-22`,
`platform/src/host/Comuki.Host/Mcp/McpServer.cs:126-296`
**Issue.** The endpoint comment says "Anonymous — the global auth +
permission filter (when wired) handles identity; MCP shares the host's
cookie / api-key auth." The filter is not wired. The dispatcher
(`CallToolAsync`) routes `knowledge.search` / `knowledge.ingest` /
`runs.list` / `runs.get` directly to the underlying handlers with no
`[RequiresPermission]` attribute and no in-method `IPermissionEvaluator`
check. Any authenticated subject (cookie *or* API key) can call
`runs.list` and read every run in the projects the *key* can see,
regardless of whether the subject has `run:read`. Same for
`knowledge.search` and `knowledge.ingest` (the latter is the *write*
side — `knowledge:write` is bypassed entirely over MCP).
**Recommendation.** Either (a) attach a host-wide
`[RequiresPermission("…")]` per tool through a discriminated dispatch
table (the dispatcher knows the tool name → permission key), or (b)
deny by default and accept a per-tool allow-list. The bare minimum is
to call `IPermissionEvaluator.EvaluateAsync(subject, ct)` inside
`CallToolAsync` and 403 on miss.

#### A01-2. `OIDC discovery` / `OidcCallback` link via email has no `email_verified` gate
**Severity:** **High**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcCallbackHandler.cs:126-139`,
`platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcAccountLinker.cs:42-66`
**Issue.** The `OidcIdTokenValidator` extracts `email` from the
id_token, but does **not** require the `email_verified` claim. The
`OidcAccountLinker` then either links a local user by email
(`OidcAccountLinker.cs:52-59`) or provisions a new password-less
account (`OidcAccountLinker.cs:61-65`). Any IdP that issues a JWT
with `sub` + `email` for a Comuki user email (verified or not)
becomes a Comuki account. The audit-product-report's Q36 fix
(Disabled check) does *not* help here: the user being impersonated is
typically the *real* local user, not a disabled one.
**Recommendation.** In `OidcIdTokenValidator`, require
`email_verified=true` (return `InvalidOperationException` otherwise).
This is the standard OIDC RP hardening; GitHub, Google, Microsoft all
honour it.

#### A01-3. `SetUserDisabled` & `GrantRole` perform seniority guard, but the call site can be self-target
**Severity:** **Low / Info**
**Files:** `platform/src/host/Comuki.Host/Auth/Controllers/UsersController.cs:78-101`,
`platform/src/host/Comuki.Host/Auth/Controllers/GrantsController.cs:38-71`
**Issue.** The controllers resolve `ActingAs: HostSubjects.Resolve(User)`
and pass it to the handler. The handler (not shown in this audit
window) compares seniorities. The review concern is whether a
`PlatformAdmin` can disable themselves or revoke their own grant
without the system locking out the only remaining admin. A guard
"don't let the last active PlatformAdmin disable themselves" is not
visible in the surface code. The seniority guard prevents a lower
role from revoking higher; it does **not** prevent a PlatformAdmin
from self-revoking.
**Recommendation.** Before disabling a `PlatformAdmin`, count active
admins; refuse if `count <= 1`. Same for self-revoke.

#### A01-4. `SignalR RunsHub` uses `[Authorize]` but no per-join permission check visible
**Severity:** **Medium**
**Files:** `platform/src/host/Comuki.Host/Realtime/RunsHub.cs:24`
**Issue.** The hub has only `[Authorize]` — any authenticated subject
can call `JoinRun` / `JoinProject`. A permission check on the hub
methods is the right place to filter; without it, a Viewer can
subscribe to a run that is escalated to a higher role.
**Recommendation.** Add a `IPermissionEvaluator.EvaluateAsync(subject, "run:read")`
check inside `JoinRun` / `JoinProject`; 403 on miss.

#### A01-5 (positive). `[RequiresPermission]` coverage is complete
**Severity:** **Info (passing)**
**Files:** 16 controllers, 48 attributes (see grep result)
**Note.** Every MVC controller I sampled has class-level
`[RequiresPermission(...)]` or per-action attributes. The audit-product-report's
"projects have no permission check" finding from `fa659fd` is no longer
true at `47b3f47` — `ProjectsModuleEndpoints.cs:48-126` has 7
attributes (`project:admin` for writes, `project:read` for reads).
The action axis is well covered.

#### A01-6 (positive). `WebhooksController` is anonymous by design
**Severity:** **Info (passing)**
**Files:** `platform/src/host/Comuki.Host/Intake/Controllers/WebhooksController.cs:27-31`
**Note.** No `[RequiresPermission]` because the signature IS the
auth — the `WebhookIntakeService` rejects on `VerifySignature` (401).
This is the right model. See A08-1 for a separate insert-first
ordering concern.

#### A01-7 (positive). Subject-scope row filtering is fail-closed
**Severity:** **Info (passing)**
**Files:** `platform/src/shared/Comuki.Shared.Kernel/Scoping/SubjectScope.cs:21-44`,
`platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs:22-43`
**Note.** `SubjectScope.Nothing` is the empty set; an authenticated
subject with no assignments matches no rows. Cross-project reads
silently 404. The worker's `AsSystem` is re-established at each worker
endpoint (`WorkerEndpoints.cs:47, 88, 110, 130`). Good discipline.

### A02 — Cryptographic Failures

#### A02-1. API-key pepper AND worker-token pepper each have a dev default that `ProductionSecretValidator` does not catch
**Severity:** **Critical**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Options/ApiKeyOptions.cs:19-27`,
`platform/src/engine/Comuki.Engine.Compute/Options/WorkerTokenOptions.cs:15-25`
**Issue.** When `COMUKI_IDENTITY_APIKEY_PEPPER` is unset,
`ApiKeyOptions.Pepper` falls back to
`"comuki-dev-only-apikey-pepper-override-in-production"`. When
`COMUKI_TOKEN_PEPPER` is unset, `WorkerTokenOptions.Pepper` falls
back to `"comuki-dev-only-pepper-override-in-production"`. Two
different class names, same pattern, both bypass
`ProductionSecretValidator` (which only checks MinIO + bootstrap
admin). A production deployment that omits either env var will
silently run with a public-domain pepper; an attacker with read
access to the `apikeys` or `worker_tokens` table can forge the
HMAC of any key/token.
**Recommendation.** Add the peppers to `ProductionSecretValidator`:
`if (string.Equals(apiKeyOptions.Pepper, "comuki-dev-only-…", Ordinal)) throw;`
`if (string.Equals(workerTokenOptions.Pepper, "comuki-dev-only-…", Ordinal)) throw;`
Consolidate the dev-default string in one place; the two literals
already drift ("apikey-pepper" vs no "apikey" suffix).

#### A02-2. `Cookie.SecurePolicy = SameAsRequest` allows HTTP cookie set
**Severity:** **High**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/IdentityAuthExtensions.cs:87`
**Issue.** `Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest` —
if the inbound request is `http://`, the `Set-Cookie` header is
emitted without the `Secure` flag. A production deployment that
sits behind a reverse proxy which forwards plain HTTP to the host
(or a misconfigured HSTS that allows an HTTP fallback) will issue
session cookies that the browser will send over plaintext. The
session cookie is `HttpOnly = true` and `SameSite = Lax`, so XSS is
not the threat — the threat is a network attacker who MITMs the
HTTP request.
**Recommendation.** Set `Cookie.SecurePolicy = CookieSecurePolicy.Always`
unconditionally (HTTPS-only is the safe default in 2026). If a
dev-mode HTTP fallback is needed, gate it on `IHostEnvironment.IsDevelopment()`.

#### A02-3. `CookieAuthOptions.ExpireTimeSpan` default 7 days, sliding
**Severity:** **Medium**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Cookies/CookieAuthOptions.cs:21-26`
**Issue.** A 7-day sliding expiry is long for a backend that exposes
admin endpoints. There is no idle-timeout (only absolute max).
Sliding + 7 days means an unattended browser in a corp office
maintains an active session for a week.
**Recommendation.** Split into `IdleTimeout` (e.g. 30 min) +
`AbsoluteTimeout` (e.g. 8 hours). The `tokens_version` security-stamp
check on every request (line 96-110 of `IdentityAuthExtensions.cs`)
already kills the cookie on password change; the same kill switch
should fire on idle.

#### A02-4. `OidcIdTokenValidator` clock skew is 2 minutes
**Severity:** **Low / Info**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcIdTokenValidator.cs:38`
**Issue.** `ClockSkew = TimeSpan.FromMinutes(2)` is generous. The
default Microsoft recommendation is 30 seconds; 2 minutes widens
the replay window for a leaked `id_token` (the OIDC start handler
deletes the state row in `ConsumeAsync`, but the JWT itself is
valid for the lifetime declared in `exp`).
**Recommendation.** Drop to 30s. Most IdPs (Keycloak, Auth0, Okta)
ship `exp` within a few seconds of `iat`.

#### A02-5 (positive). HMAC is constant-time, RNG is `RandomNumberGenerator`
**Severity:** **Info (passing)**
**Files:** `ApiKeyHasher.cs:25-50`, `ApiKeyToken.cs:28-39`,
`OidcPkce.cs:23-47`, `WorkerTokenIssuer.cs:29-65`,
`WebhookSecretGenerator.cs:21`, `WebhookKeyGenerator.cs:21`
**Note.** All token material uses `RandomNumberGenerator` (CSPRNG).
HMAC verify uses `CryptographicOperations.FixedTimeEquals`. The
not-found path in `ApiKeyAuthenticationHandler.cs:90` runs a
dummy digest to flatten the timing oracle. This is correct.

#### A02-6 (positive). Password hashing uses BCL `IPasswordHasher` (PBKDF2-SHA256, 100k iters)
**Severity:** **Info (passing)**
**Files:** `InviteUserHandler.cs:51-53`, `LoginHandler.cs:39`
**Note.** `Microsoft.AspNetCore.Identity.PasswordHasher<User>` is
PBKDF2-HMAC-SHA256, 100,000 iterations, 16-byte salt, 32-byte
output. Adequate for v1; Argon2id would be stronger but the BCL
implementation is the standard hardened default and is what the
team chose. Document the choice in `security.md`.

### A03 — Injection

#### A03-1. OIDC `redirect_uri` built from request `Host` header — Host header injection
**Severity:** **High**
**Files:** `platform/src/host/Comuki.Host/Auth/Controllers/AuthController.cs:182-191`
**Issue.** `BuildCallbackUri(Request)` returns
`{request.Scheme}://{request.Host}/api/v1/auth/oidc/callback`. The
`request.Host` value comes from the inbound `Host:` header. A direct
attacker (or any reverse proxy that doesn't pin `X-Forwarded-Host`)
can send `Host: evil.com` and receive a 302 with
`redirect_uri=https://evil.com/api/v1/auth/oidc/callback`. The
`OidcStartHandler.HandleAsync` then sends that `redirect_uri` to the
IdP for the token exchange. The IdP follows it during code exchange;
the `code` lands at `evil.com`; the attacker redeems it.
**Recommendation.** Either (a) use `ForwardedHeadersMiddleware` with
`KnownProxies`/`KnownNetworks` *and* require `X-Forwarded-Host` to
match the configured public host, or (b) read the configured public
host from configuration (`Auth:PublicHost`) and use that
unconditionally. Same for `request.Scheme` (read from
`ForwardedHeaders` or config).

#### A03-2. `Knowledge.Ingest` `text` field is unbounded
**Severity:** **Medium**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Application/ApiKeys/...` (the request type for `POST /api/v1/knowledge/ingest`),
`platform/src/host/Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs:25-50`
**Issue.** The ingest request body has no `MaxLength` on the `text`
field. A 100 MB POST will be read into memory, parsed, chunked, and
embedded (each chunk triggers an OpenAI API call). One request
can do real damage. The audit-product-report did not flag this.
**Recommendation.** `MaxLength` on the request record; ASP.NET Core's
Kestrel already caps the body (default 30 MB), but the validator
should enforce a smaller semantic cap (e.g. 1 MB per request) and
the endpoint should return 413 for larger.

#### A03-3. Webhook `provider` route segment is the only validated key
**Severity:** **Low / Info (passing)**
**Files:** `WebhooksController.cs:31`, `WebhookIntakeService.cs:53-67`
**Note.** `provider` is matched against `TicketProviderRegistry`;
unknown providers 404 before any DB read. The `key` is a per-connection
routing id (opaque) — never used to build a path or to dispatch a
command. No shell metacharacter risk. See A08-1 for the
insert-first-before-signature-verify ordering concern.

#### A03-4 (positive). All SQL is EF Core LINQ, parameterised
**Severity:** **Info (passing)**
**Files:** all `*DbContext.cs` and `*Repository.cs`
**Note.** No raw SQL writes. `ExecuteUpdate`/`ExecuteDelete` for
set-based writes. Migrations are tool-generated
(`ef-migrations.md`). No string interpolation in LINQ.

### A04 — Insecure Design

#### A04-1. No per-key rate limit on the proxy
**Severity:** **High**
**Files:** `platform/src/modules/Proxy/Comuki.Modules.Proxy.Application/...` (the budget enforcer),
`platform/src/host/Comuki.Host/Proxy/ProxyModuleEndpoints.cs:25-31`
**Issue.** The proxy enforces only the *monthly budget* per virtual
key (`Audit-product-report §2.9 critical + §2.9 minor`). A single
bad client can burn the entire monthly budget in minutes by firing
one cheap call per millisecond. There is no per-minute request
cap, no token-throughput cap, and no concurrency cap.
**Recommendation.** Per-key `PermitsPerMinute` (config-driven) +
per-key `MaxTokensPerMinute`. Mirror the same `RateLimitPolicies`
infrastructure the host already wires (`AuthController.cs:60`).

#### A04-2. No auto-rotation for the bootstrap admin password
**Severity:** **Medium**
**Files:** `platform/src/host/Comuki.Host/Auth/BootstrapAdminOptions.cs`,
`platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs:78-101`
**Issue.** The validator enforces a 12+ char / digit / symbol
minimum, but no expiry, no "last-rotated-at" tracking, no
"force rotation on first N logins" check. The audit-product-report
flags this. A first-week operator who rotates `comuki_dev` →
`Strong!Pass1` can leave it there forever.
**Recommendation.** Add `LastRotatedAt` to the `users` table; refuse
to authenticate a `PlatformAdmin` whose `LastRotatedAt` is older
than 90 days (configurable). Surface the warning in the
`IdentityPage`.

#### A04-3. OIDC links are global, not per-project (Q42 deferred)
**Severity:** **Medium**
**Files:** `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/...` (the OIDC link table)
**Issue.** A `(provider, sub)` OIDC link is global to the user.
Revoking the link affects every project the user is in. The product
decision Q42 deferred per-project links to v2.
**Recommendation.** Acceptable for v1 single-tenant. Document the
behaviour in `security.md` §"OIDC" and link to Q42 as the
follow-up. For the v1.1, add `OidcLink.TenantProjectId?` and a
"link in scope" filter.

#### A04-4. Multi-IdP button missing (Q5 deferred to v1.1)
**Severity:** **Low / Info**
**Files:** `AuthController.cs:156-180`
**Issue.** `GET /api/v1/auth/oidc/{provider}/start` accepts a path
parameter, but the FE cannot list providers (Q5 v1.1). Today
operators can start OIDC only by knowing the configured provider
name.
**Recommendation.** Add `GET /api/v1/auth/oidc/providers` returning
the configured provider list; trivial addition. Not a security
gap per se, but it forces operators to hard-code a name they
shouldn't have to know.

#### A04-5 (positive). `SubjectScope.Nothing` is the fail-closed default
**Severity:** **Info (passing)**
**Files:** `SubjectScope.cs:21`, `SubjectScopeMiddleware.cs:26-34`
**Note.** Already cited in A01-7. The fail-closed design is correct.

### A05 — Security Misconfiguration

#### A05-1. `SignalR EnableDetailedErrors` enabled in every production container
**Severity:** **Critical**
**Files:** `platform/src/host/Comuki.Host/Realtime/RealtimeExtensions.cs:19-61`
**Issue.** The diagnostic opt-in set is
`ASPNETCORE_ENVIRONMENT=Development`
**OR** `DOTNET_ENVIRONMENT=Development`
**OR** `DOTNET_RUNNING_IN_CONTAINER=true`
**OR** `COMUKI_REALTIME_DETAILED_ERRORS=true`.
The .NET base image sets `DOTNET_RUNNING_IN_CONTAINER=true` in every
container it builds — including production. So **every Comuki
container, dev or prod, has detailed SignalR errors enabled**, and
the audit-product-report's claim that "issue #19 closed it" is wrong
in practice.
**Recommendation.** Drop the `DOTNET_RUNNING_IN_CONTAINER` branch.
Keep only the `ASPNETCORE_ENVIRONMENT=Development` branch (or an
explicit opt-in for local dev). The `COMUKI_REALTIME_DETAILED_ERRORS`
env var is the right escape hatch for support, but the
container-detection shortcut is a footgun.

#### A05-2. `ProductionSecretValidator` does not cover API-key pepper, OIDC client secrets, DB password, proxy virtual key tokens
**Severity:** **High**
**Files:** `platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs:30-102`
**Issue.** The validator only checks three things:
`ArtifactsOptions.SecretKey` (MinIO), `ArtifactsOptions.AccessKey`
(MinIO), and the bootstrap admin password. The API key pepper
(see A02-1), the OIDC client secret env vars, the database connection
string, the worker token pepper, and the proxy virtual key tokens
all run with their committed defaults silently.
**Recommendation.** Add explicit checks for each: refuse to start
in `Production` when `ApiKeyOptions.Pepper` is the dev default; same
for the OIDC client secret env vars (if the operator configures an
OIDC provider, the secret must resolve); same for the DB password
blank (the migrator already does this — `ConnectionStringSource.RejectBlankPasswordInProduction`).

#### A05-3. `McpServer` returns `exception.Message` in JSON-RPC error
**Severity:** **Medium**
**Files:** `platform/src/host/Comuki.Host/Mcp/McpServer.cs:181`
**Issue.** `JsonRpcResponse.Failure(id, InternalError, $"tool '{toolCall.Name}' failed: {exception.Message}")`
includes the raw exception text. For tools that touch the DB (e.g.
`runs.list`), an Npgsql connection error becomes
`"tool 'runs.list' failed: Npgsql: connection refused"` — a stack
hint for an attacker. The platform's `exceptions.md` rule (no
inner exception messages in `detail`) is violated here.
**Recommendation.** Log the full exception server-side; return
`"tool 'X' failed"` only. Map known exception types to stable
codes (`db.error`, `search.error`) at the dispatcher boundary.

#### A05-4. No security response headers (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy)
**Severity:** **Medium**
**Files:** `platform/src/host/Comuki.Host/HostComposer.cs:317-329`
**Issue.** `UseAuthentication` + `UseAuthorization` + `UseCors` +
`UseRateLimiter` + `UseMiddleware<SubjectScopeMiddleware>` — no
`UseSecurityHeaders` or equivalent. The SPA is served by Vite in
dev (separate origin) and by a static host in prod. The API host
itself is not browser-rendered, but a same-origin admin SPA could
embed the host's responses; CSP, X-Frame-Options, and
`X-Content-Type-Options: nosniff` are standard hardening.
**Recommendation.** Add a tiny middleware that emits
`X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer`,
`Strict-Transport-Security: max-age=31536000; includeSubDomains` (only
when `IsProduction()` and behind TLS).

#### A05-5. CORS default `["http://localhost:17173"]` ships in code
**Severity:** **Low / Info**
**Files:** `platform/src/host/Comuki.Host/Security/Cors/CorsOptions.cs:23`
**Issue.** The default `AllowedOrigins` array carries
`"http://localhost:17173"`. `AllowWildcard` is refused in
`Production` (line 27-30), so a production deploy with no override
starts with **no** CORS-allowed origin and the dashboard can't reach
the API. Operators must remember to set the env var.
**Recommendation.** The default is dev-friendly; add a startup
warning when `IsProduction()` and `AllowedOrigins` is the
dev default (or empty). The deploy manifest should always override
this; the warning catches the forgotten-override case.

### A06 — Vulnerable Components

#### A06-1. `Microsoft.AspNetCore.OpenApi 10.0.9` has a known transitive vulnerability suppressed
**Severity:** **High**
**Files:** `Directory.Packages.props:78-81` (the pin), `platform/src/host/Comuki.Host/Comuki.Host.csproj` (the `<NoWarn>NU1903</NoWarn>`)
**Issue.** The Directory.Packages.props comment explicitly cites
`GHSA-v5pm-xwqc-g5wc` in transitive `Microsoft.OpenApi 2.0.0` and
notes the build suppresses `NU1903` instead of upgrading. A
suppressed vulnerability is still a vulnerability — it just doesn't
fire the build gate. The follow-up (3.x-compatible source
generator) is the right fix, but **today** the host is running a
package with a known CVE.
**Recommendation.** Track and bump `Microsoft.AspNetCore.OpenApi`
as soon as Microsoft ships the 3.x-compatible source generator. In
the meantime, document the CVE in `security.md` §"Known accepted
risks" with the planned fix.

#### A06-2. `dashboard/package.json` has invalid semver ranges
**Severity:** **Low / Info (likely typos)**
**Files:** `dashboard/package.json:46, 76, 91, 93`
**Issues.**
- Line 46: `"lucide-react": "^1.17.0"` — real `lucide-react` is on the
  `0.x` line (0.460.x as of 2026); `^1.17.0` does not exist. `npm
  install` will fail or install an unscoped typo-squat. **This is
  broken in the committed lockfile context; verify `bun.lock`.**
- Line 76: `"@types/node": "^24"` — real `@types/node` is on 22.x;
  `^24` does not exist.
- Line 91: `"typescript": "~6"` — TypeScript 6 is not released.
- Line 93: `"vite": "^8"` — Vite 8 is not released; the latest stable
  is on 6.x.

  These are very likely typos or work-in-progress pins. They block
  `bun install` and any CI dependency-audit job. They are not
  security issues *per se*, but they mean the dashboard's dependency
  posture is unknown — `npm audit` / `bun audit` will report
  "could not resolve" rather than known CVEs.
**Recommendation.** Bump these to real versions in a follow-up
commit. Then re-run `bun audit` and capture the report in
`security.md` §"Dependencies".

#### A06-3. `ajv ^6.12.0` pinned for eslint dev-only
**Severity:** **Low / Info**
**Files:** `dashboard/package.json:11-13`
**Issue.** `ajv ^6.12.0` is older than the `^8.5.0` the rest of the
project uses. `ajv 6.x` has several known CVEs (CVE-2020-7598
prototype pollution among them). It's pinned as a dev-only override
for `eslint`, not at runtime — the production bundle uses
`ajv ^8.5.0` (the override on line 7-9 of `package.json`).
**Recommendation.** Acceptable as-is (dev-only). Document in
`security.md` §"Dev-time dependencies".

#### A06-4. `eslint ^10` / `typescript-eslint ^8` are major-version pins
**Severity:** **Low / Info**
**Files:** `dashboard/package.json:83, 92`
**Issue.** Major-version pins (caret) on dev tooling. Not a runtime
risk, but a major-version bump can introduce breaking changes to
rule sets.
**Recommendation.** Pin to the current major (e.g. `^10.x` is fine if
10 is the released line) or use a tilde for stability.

#### A06-5 (positive). `Testcontainers.PostgreSql 4.14.0` was bumped to fix a transitive CVE
**Severity:** **Info (passing)**
**Files:** `Directory.Packages.props:85-88`
**Note.** The comment explicitly cites the `Testcontainers.PostgreSql
4.12.0` → `4.14.0` bump to pick up the patched `SSH.NET 2026.0.0` for
`GHSA-q939-rpr3-3284`. This is the right discipline. The same
discipline is missing for the OpenAPI pin (A06-1).

### A07 — Authentication Failures

#### A07-1. Cookie sliding expiry 7 days, no idle-timeout
**Severity:** **Medium**
**Files:** `CookieAuthOptions.cs:21-26`
**Issue.** See A02-3. Listed here for the auth-timeout lens: a
sliding 7-day session with no idle-timeout means a stolen session
cookie remains valid for up to 7 days from the last activity.
**Recommendation.** Split into `IdleTimeout` (30 min) +
`AbsoluteTimeout` (8 hours). The `tokens_version` security-stamp
already invalidates on password change; extend it to invalidate on
disable (already done — see A07-3).

#### A07-2. `OidcStateSweeper` is 5 minutes (state TTL)
**Severity:** **Info (passing)**
**Files:** `OidcStartHandler.cs:25` (`stateTtl = 5min`)
**Note.** 5 minutes is long enough for a slow IdP, short enough
that a leaked state expires before it can be replayed. `ConsumeAsync`
deletes the state row on use — single-use. PKCE-S256 with a 32-byte
verifier. Good.

#### A07-3 (positive). Disabled-owner check is in place on the API-key path
**Severity:** **Info (passing)**
**Files:** `ApiKeyAuthenticationHandler.cs:117-120`
**Note.** An API key whose owner is disabled is rejected at auth
time. The `tokens_version` security-stamp check
(`UserAuthenticationService.cs:52-71`) kills cookies on disable
without revoking each cookie. Good discipline.

#### A07-4 (positive). Login rate-limited
**Severity:** **Info (passing)**
**Files:** `AuthController.cs:60` (`[EnableRateLimiting(RateLimitPolicies.Login)]`)
**Note.** The `Login` and `OidcStart` endpoints have
per-minute rate limits. Good.

#### A07-5 (positive). Failed login is a single 401
**Severity:** **Info (passing)**
**Files:** `AuthController.cs:48-99`
**Note.** Unknown user and wrong password both return
`InvalidCredentialsCode`. No user enumeration. The
`Logger.LogInformation("Login rejected for {Email}: {FailureCode}", …)`
logs the email + code for ops triage. Email is PII — see A09-1.

### A08 — Data Integrity

#### A08-1. Webhook signature verified *after* delivery-row insert
**Severity:** **Medium**
**Files:** `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/WebhookIntakeService.cs:69-84`
**Issue.** Lock #1 (`TryInsertDeliveryAsync`) runs before the
signature check. The unique index on `(source, delivery_id)` bounds
the flood, but an attacker who can guess the `X-GitHub-Delivery`
UUID (or send it via header — they control the header) can flood
the `intake.deliveries` table with rejected-signature rows. The
delivery-id is a UUIDv4 (128 bits), so guessing is impractical; the
*flooding* case is real (an attacker can fire millions of unsigned
webhooks; each one writes a row + attempts the HMAC).
**Recommendation.** Reorder: verify the signature **first**; only
insert the delivery row on signature-pass. The
`intake.deliveries` table becomes a post-verification audit log,
not a pre-verification rate-limiter. Same write cost, lower
write amplification.

#### A08-2. Artifact URIs are unsigned
**Severity:** **Medium**
**Files:** `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Infrastructure/Store/MinioRunArtifactStore.cs:165-170`
**Issue.** `BuildObjectUri` returns
`{scheme}://{configuration.Endpoint}/{configuration.Bucket}/{objectKey}` —
the canonical S3/MinIO URL. MinIO itself is not exposed outside the
host's network in the default deploy (audit-product-report §2.8
major), so the URL is only useful to the dashboard via the
`/api/v1/runs/{id}/artifacts` list endpoint. If the operator
*does* expose MinIO externally (e.g. for an in-cluster
diagnostic), the URL is bearer-less and grants read access to
any caller.
**Recommendation.** Either (a) keep MinIO internal and sign the URL
on each list (presigned GET with a short TTL), or (b) document the
trust model in `minio.md` explicitly.

#### A08-3 (positive). All JSON via `System.Text.Json` with typed records
**Severity:** **Info (passing)**
**Files:** all `*Dto.cs` and `*Request.cs` / `*Response.cs`
**Note.** No `Newtonsoft.Json`, no `JavaScriptSerializer`. Source-gen
contexts are used. `JsonSerializerOptions.Web` is the shared frozen
instance. No `dynamic` deserialization.

### A09 — Logging

#### A09-1. Emails logged at `Information` level
**Severity:** **Medium**
**Files:** `AuthController.cs:84`, `UsersController.cs:53, 129`,
`OidcCallbackHandler.cs:143-145`
**Issue.** Successful and failed logins log the email; OIDC
provisioning logs the email; OIDC link binding logs
`view.Provider` + `userId` (no email — good). The email is PII
under GDPR and CCPA; logging it at `Information` is a
disclosure-and-retention concern for any operator with access to
the host's log stream.
**Recommendation.** Replace `request.Email` in the log with a
short, irreversible hash (`EmailHash` = SHA-256(email)[:8] —
already a precedent in some systems). Keep the email in
operational audit (a separate, access-controlled log if needed).
Document the choice in `security.md` §"Logging".

#### A09-2. IP addresses never logged
**Severity:** **Low / Info (acceptable)**
**Files:** all `*Controller.cs`, `*Service.cs`
**Note.** No `Request.HttpContext.Connection.RemoteIpAddress`
appears in the audited surface. This is operationally the
opposite of A09-1 (we want IPs for security ops; we don't want
emails). Add `RemoteIpAddress` to the auth-failure log line.

#### A09-3. `McpServer` returns `exception.Message` in JSON-RPC error
**Severity:** **Medium** (same as A05-3; listed under logging for completeness)
**Files:** `McpServer.cs:181`
**Issue.** Internal exception text reaches the client. The
client-side log will carry the leak.
**Recommendation.** Log the full exception server-side; return a
sanitized error to the client.

### A10 — Server-Side Request Forgery (SSRF)

#### A10-1. OIDC `redirect_uri` host header injection (same as A03-1)
**Severity:** **High**
**Files:** `AuthController.cs:182-191`
**Issue.** Already covered. The IdP-side `redirect_uri` is an
attacker-controlled URL; from the IdP's perspective the Comuki
RP appears to redirect the code to `evil.com`.
**Recommendation.** Same as A03-1.

#### A10-2. `ProxyConfigProvider` builds YARP cluster `Address` from appsettings `key.BaseUrl`
**Severity:** **Low / Info (passing)**
**Files:** `ProxyConfigProvider.cs:80-103`
**Issue.** `key.BaseUrl` is admin-controlled (appsettings), not
user input. A bad operator could route the proxy to
`http://169.254.169.254/` (AWS metadata IMDSv1) and read instance
metadata through the proxy. The user is admin, so this is
intentional power, not a vulnerability.
**Recommendation.** Document the trust model. If multi-tenant
becomes a thing (Q39), the per-project `BaseUrl` is a privilege
boundary and the operator will want a "trusted upstream"
allow-list.

#### A10-3 (positive). `MinioRunArtifactStore.BuildObjectUri` admin-controlled
**Severity:** **Info (passing)**
**Files:** `MinioRunArtifactStore.cs:165-170`
**Note.** Admin-controlled endpoint, admin-controlled bucket, no
user input. Good.

#### A10-4 (positive). `RunStatusBridgeWorker` builds URL from admin config
**Severity:** **Info (passing)**
**Files:** `RunStatusBridgeWorker.cs:212`
**Note.** `publicBaseUrl` is admin config, `runId` is a Guid. No
user input reaches the URL.

---

## 2. Secrets & Config

### 2.1 Hardcoded dev defaults still in `ProductionSecretValidator` reach

| Default value | Where | Validator catches? | Severity |
|---|---|---|---|
| `comuki_dev` | `ArtifactsOptions.SecretKey` (MinIO) | ✅ yes (`ProductionSecretValidator.cs:55-60`) | — |
| `comuki` | `ArtifactsOptions.AccessKey` (MinIO) | ✅ yes (`ProductionSecretValidator.cs:62-67`) | — |
| `comuki_dev` | `BootstrapAdminOptions.AdminPassword` | ✅ yes (`ProductionSecretValidator.cs:88-93`) | — |
| `comuki-dev-only-apikey-pepper-override-in-production` | `ApiKeyOptions.Pepper` | ❌ no | **Critical** (A02-1) |
| `comuki-dev-only-pepper-override-in-production` | `WorkerTokenOptions.Pepper` | ❌ no | **Critical** (A02-1) |
| `comuki` (default DB user) | `appsettings.json:8` migrator connection string | ❌ no (DB password is empty by default) | **High** (set in env in real deploys) |
| `comuki_dev` | `Nexus admin password` (`.env.example:27`) | N/A — Nexus is a separate container | Informational |
| `comuki_dev` | `MinIO root password` (`.env.example:17`) | N/A — MinIO is a separate container | Informational |
| `bootstrap-pass-1` | `tests/integration/*/Host*Server.cs:38-145` | N/A — integration tests only | Informational |

**Recommendation.** Extend `ProductionSecretValidator` to cover:
- `ApiKeyOptions.Pepper` (A02-1)
- The migrator's DB password (already covered by
  `ConnectionStringSource.RejectBlankPasswordInProduction` — verify
  it's wired in the migrator host)
- OIDC client secret env vars (if a provider is configured, the
  secret env var must resolve)
- Proxy virtual key tokens (no production default; check for
  empty values when `Proxy:Enabled = true`)

### 2.2 `appsettings.json` defaults

**File:** `platform/src/host/Comuki.Migrator/appsettings.json:1-5`
```json
{
  "ConnectionStrings": {
    "Comuki": "Host=localhost;Port=5432;Database=comuki;Username=comuki;Password="
  }
}
```
The migrator's appsettings ships an empty password. This is
intentional (env var override is the production path), but the
validator must catch this in Production. The migrator runs
`RejectBlankPasswordInProduction` — verify it is wired (read
`ConnectionStringSource`).

**File:** `platform/src/host/Comuki.Host.Brain/appsettings.json:1-17`
The Brain host's `brain.model.endpoint` / `apiKey` / `modelId` are
all empty strings. The Brain will fail to start with a clear error
if it tries to make a call without config — but a misconfigured
brain in production is a denial-of-service surface (the brain
is the agent loop). Recommend a startup validator that refuses
empty `brain.model.*` fields when the brain is enabled.

### 2.3 `.env.example` (the committed file)

**File:** `deploy/.env.example`
- All defaults are dev-only (`comuki_dev` / `comuki`). Good.
- `COMUKI_NEXUS_ADMIN_PASSWORD=comuki_dev` is documented as
  "Initial admin password is read once on first boot" — the
  operator is on the hook to change it. Good.
- `CORS_ALLOWED_ORIGINS=http://localhost:17173` is the dev default
  and **must** be overridden for prod. The comment on line 66
  states this; the operator may miss it. **Add a startup
  warning** in `CorsInstaller` (see A05-5).
- `PROXY_VIRTUALKEYS_0__TOKEN=vkey_replace_me` is a placeholder;
  if committed to a `.env` accidentally and a `Proxy:Enabled=true`
  is set, the placeholder becomes a real bearer. Recommend a
  validator that refuses placeholder values.

### 2.4 Token expiration

- **API key**: `ApiKeyOptions.LastUsedRefreshInterval` = 5 min
  (write throttle, not expiry). No absolute expiry on API keys.
  Audit-product-report Q40 (v2). **Medium** — a leaked key is
  valid forever until manually revoked.
- **Worker token**: `WorkerTokenOptions.TokenTtl` (not shown in
  this audit window; verify the default).
- **Cookie**: 7 days, sliding (see A02-3 / A07-1).
- **OIDC state row**: 5 min (`OidcStartHandler.cs:25`).
- **OIDC discovery doc**: cached in-process forever (lifetime of
  the host). The audit-product-report flagged this; if a key
  rotates, the host must restart.

### 2.5 Cookie lifetime defaults

Already covered under A02-3 / A07-1.

---

## 3. Authorization Model

### 3.1 `RoleMatrix` coverage

The matrix (`platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Roles/RoleMatrix.cs:16-106`)
declares 6 roles (`PlatformAdmin`, `Operator`, `ProjectAdmin`,
`Approver`, `Member`, `Viewer`) and 24 permission keys. The keys
in the matrix are a closed set; `RoleMatrix.AllPermissionKeys` is
the union. The `PermissionDemandStartupValidator`
(`IdentityAuthExtensions.cs:74-76`) catches `[RequiresPermission(...)]`
attributes that reference a key **not** declared in the matrix —
good. It does **not** catch endpoints that *should* have a
permission but don't — a known gap (audit-product-report §3.4).

### 3.2 Permission keys

The matrix uses keys like `run:read`, `run:create`, `run:stop`,
`run:inject`, `plan:read`, `plan:approve`, `intake:read`,
`intake:claim`, `source:read`, `source:write`, `chat:use`,
`settings:read`, `settings:write`, `knowledge:read`,
`knowledge:write`, `knowledge:admin`, `verify:read`, `cost:read`,
`project:read`, `project:admin`, `identity:read`, `identity:write`,
`scheduler:read`, `scheduler:write`, `platform:admin`.

Each key appears in at least one `[RequiresPermission]` attribute in
the audited controllers. The grep found 48 attributes across 16
controllers — coverage is good on the action axis.

**Vocabulary drift** (audit-product-report §3.4): the FE uses
`runs.view`, `runs.stop`, `inbox.view`, `inbox.take`,
`sources.view`, `sources.edit`, `cost.view` — different keys. The
FE maps BE keys to FE names in
`dashboard/src/domains/identity/api/mappers.ts`. When a BE key is
added, the FE silently doesn't honor it. **Not a security finding
in v1, but a maintainability risk that becomes a security risk
when a new key is added and the FE doesn't pick it up.**

### 3.3 `SubjectScope` middleware

Already covered in A01-7. Fail-closed (`SubjectScope.Nothing`).
Wired at `HostComposer.cs:322` (`UseMiddleware<SubjectScopeMiddleware>`).
`AsSystem` is re-established explicitly at the worker endpoints
and at the BackgroundService consumers (artifacts packager,
`AsSystem("artifact-packager")`).

### 3.4 API key scoping (`TenantProjectId`)

**File:** `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/ApiKeys/ApiKeyAuthenticationHandler.cs:104-113, 163-171`
The handler enforces `X-Comuki-Tenant` when the key carries a
`TenantProjectId`; mismatch returns 403. Good. A non-scoped key
(no `TenantProjectId`) authenticates as the key's owner subject;
the global query filters in the orchestration DbContext confine
the key to the owner's projects. **There is no bypass path I
could find** — the handler reads the prefix, looks up the row,
verifies the HMAC, then enforces the tenant header before
returning `AuthenticateResult.Success`.

### 3.5 OIDC linker — privilege escalation via email collision

Already covered in A01-2. The Q36 disabled-check landed
(`OidcAccountLinker.cs:54`); the missing gate is
`email_verified=true`. An IdP that can issue a JWT with `sub` and
`email` (verified or not) for a Comuki user email becomes a
Comuki account. The audit-product-report §2.1 critical flagged
this; the security finding reinforces it: **any IdP in the
config is a Comuki identity provider for all emails it can
assert**.

---

## 4. Dependency Audit

This is a static read of `Directory.Packages.props` and
`dashboard/package.json`. **No `npm audit` / `bun audit` was run**
(both were rejected by the user; the audit is read-only). The
findings below are from version pinning + known-CVE lookups
(only the ones explicitly cited in the repo).

### 4.1 NuGet (C# / .NET 10)

| Package | Version | Pinned by | Notes | Severity |
|---|---|---|---|---|
| `Microsoft.AspNetCore.OpenApi` | `10.0.9` | `Directory.Packages.props:79` | **Known transitive CVE** (GHSA-v5pm-xwqc-g5wc in `Microsoft.OpenApi 2.0.0`); suppressed via `<NoWarn>NU1903</NoWarn>` on `Comuki.Host.csproj`. The repo comment defers the fix to v3.x. | **High** (A06-1) |
| `Testcontainers.PostgreSql` | `4.14.0` | `Directory.Packages.props:88` | Bumped from 4.12.0 to pick up `SSH.NET 2026.0.0` (fixes GHSA-q939-rpr3-3284). | Info (passing) |
| `OpenAI` | `2.12.0` | `Directory.Packages.props:115` | Recent. No known CVEs at this version (knowledge cutoff 2026-01). | Info |
| `Microsoft.Extensions.AI` | `10.9.0` | `Directory.Packages.props:113` | Recent. | Info |
| `Refit` | `15.2.0` | `Directory.Packages.props:44-46` | Recent. | Info |
| `Yarp.ReverseProxy` | `2.3.0` | `Directory.Packages.props:154` | Recent. | Info |
| `FluentValidation` | `12.1.1` | `Directory.Packages.props:23` | Recent. | Info |
| `Sentry` | `6.10.0` | `Directory.Packages.props:133` | Recent. | Info |
| `Docker.DotNet` | `3.125.15` | `Directory.Packages.props:19` | Recent. | Info |
| `KubernetesClient` | `17.0.14` | `Directory.Packages.props:22` | Comment notes 18/19 exist but unvetted here. | Info |
| `LibGit2Sharp` | `0.32.0` | `Directory.Packages.props:25` | Recent. | Info |
| `protobuf-net.Grpc` | `1.3.14` | `Directory.Packages.props:42-43` | Recent. | Info |
| `Voluta` | `0.3.0` | `Directory.Packages.props:64-66` | Local repo at `C:\Users\bradw\source\stbl\voluta`. Not in the public NuGet feed — dependency-audit blind spot. | Info (recommend tracking) |

### 4.2 Node (dashboard)

See A06-2, A06-3, A06-4. Five issues total:

| ID | Package | Version | Issue | Severity |
|---|---|---|---|---|
| A06-2a | `lucide-react` | `^1.17.0` | Invalid semver (real line is 0.x); `npm install` will fail. | **Info (typo, blocks audit)** |
| A06-2b | `@types/node` | `^24` | Invalid semver (real line is 22.x). | **Info (typo)** |
| A06-2c | `typescript` | `~6` | Invalid semver (TS 6 not released). | **Info (typo)** |
| A06-2d | `vite` | `^8` | Invalid semver (Vite 8 not released). | **Info (typo)** |
| A06-3 | `ajv` (eslint override) | `^6.12.0` | Older than the runtime `^8.5.0`; dev-only; CVE-2020-7598 mitigated by being dev-only. | **Low / Info** |

**5 issues total in dashboard dependencies.**

### 4.3 Backend dependencies total: **1 issue** (A06-1).

---

## 5. Recommendations

### Priority 1 — must-fix before public release

1. **A05-1**: Drop the `DOTNET_RUNNING_IN_CONTAINER` branch from
   `RealtimeExtensions.ShouldEnableDetailedErrors`. Production
   containers are leaking stack frames. Trivial 4-line fix.
2. **A02-1**: Extend `ProductionSecretValidator` to refuse the dev
   API-key pepper. Production deployments without the env var are
   currently running with a public-domain HMAC pepper.
3. **A01-1**: Add a permission check to `McpServer.DispatchAsync`.
   MCP currently bypasses `[RequiresPermission]` for every tool.
4. **A03-1 / A10-1**: Read the OIDC `redirect_uri` host from
   configuration (or pin via `ForwardedHeadersMiddleware`). The
   current code trusts the inbound `Host:` header, which is
   attacker-controlled.
5. **A01-2**: Require `email_verified=true` in `OidcIdTokenValidator`.
   The OIDC linker otherwise turns any IdP claim into a Comuki
   account.

### Priority 2 — must-fix within 30 days

6. **A02-2**: Set `Cookie.SecurePolicy = CookieSecurePolicy.Always`
   (or gate on `IsDevelopment()`).
7. **A04-1**: Per-key `PermitsPerMinute` + `MaxTokensPerMinute` on
   the proxy. Today a single bad client can burn the entire
   monthly budget in minutes.
8. **A05-2**: Cover the API-key pepper, OIDC client secrets, DB
   password, and proxy virtual key tokens in
   `ProductionSecretValidator`. See §2.1.
9. **A02-3 / A07-1**: Split `CookieAuthOptions.ExpireTimeSpan` into
   `IdleTimeout` (30 min) + `AbsoluteTimeout` (8 hours). The
   security-stamp kill switch already exists; this just makes it
   faster.
10. **A05-3 / A09-3**: Stop returning `exception.Message` in the
    `McpServer` JSON-RPC error body. Log server-side; return a
    stable code.
11. **A05-4**: Add `X-Content-Type-Options: nosniff`,
    `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
    `Strict-Transport-Security` (production only) via a small
    middleware.
12. **A08-1**: Reorder `WebhookIntakeService` — verify signature
    *before* the delivery-row insert. Avoids write amplification
    from unsigned floods.
13. **A09-1**: Replace emails in `LogInformation` with an
    irreversible short hash. Keep the email in a separate,
    access-controlled audit log.
14. **A04-2**: Add `LastRotatedAt` to `users`; refuse to
    authenticate a `PlatformAdmin` whose password is older than
    90 days.
15. **A01-4**: Add an explicit `IPermissionEvaluator.EvaluateAsync`
    check inside `RunsHub.JoinRun` / `JoinProject`. The bare
    `[Authorize]` is too loose.
16. **A06-2**: Fix the four invalid semver ranges in
    `dashboard/package.json` (`lucide-react`, `@types/node`,
    `typescript`, `vite`). After the fix, run `bun audit` and
    capture the report.

### Priority 3 — post-ship / v2

17. **A04-3**: Per-project OIDC link scope (Q42).
18. **A07-1**: API key absolute expiry (Q40 / v2).
19. **A04-4**: `GET /api/v1/auth/oidc/providers` (Q5 / v1.1).
20. **A01-3**: Last-admin guard for self-disable / self-revoke.
21. **A08-2**: Presigned artifact URLs if MinIO is ever
    externally reachable.
22. **A06-1**: Bump `Microsoft.AspNetCore.OpenApi` to 3.x when
    the source generator lands upstream.
23. **A02-4**: Drop `ClockSkew` to 30s (cosmetic; current 2 min
    is acceptable).
24. **A10-2**: Document the proxy's `BaseUrl` trust model
    (admin-controlled; intentional power).

---

## Appendix: Files Reviewed

### Controllers (16)
- `platform/src/host/Comuki.Host/Auth/Controllers/AuthController.cs`
- `platform/src/host/Comuki.Host/Auth/Controllers/UsersController.cs`
- `platform/src/host/Comuki.Host/Auth/Controllers/KeysController.cs`
- `platform/src/host/Comuki.Host/Auth/Controllers/GrantsController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/SourcesController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/WebhooksController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/TicketsController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/InboxController.cs`
- `platform/src/host/Comuki.Host/Intake/Controllers/AdmissionRulesController.cs`
- `platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs`
- `platform/src/host/Comuki.Host/Runs/Controllers/RunArtifactsController.cs`
- `platform/src/host/Comuki.Host/Chat/Controllers/ChatSessionsController.cs`
- `platform/src/host/Comuki.Host/Chat/Controllers/ChatSlashController.cs`
- `platform/src/host/Comuki.Host/Scheduler/ScheduledJobsController.cs`
- `platform/src/host/Comuki.Host/ControlPlane/Controllers/ProfilesController.cs`
- `platform/src/host/Comuki.Host/ControlPlane/Controllers/ChatCommandsController.cs`

### Endpoints (5)
- `platform/src/host/Comuki.Host/Projects/ProjectsModuleEndpoints.cs`
- `platform/src/host/Comuki.Host/Knowledge/KnowledgeModuleEndpoints.cs`
- `platform/src/host/Comuki.Host/Mcp/McpModuleEndpoints.cs`
- `platform/src/host/Comuki.Host/Costs/CostsModuleEndpoints.cs`
- `platform/src/host/Comuki.Host/Proxy/ProxyModuleEndpoints.cs`
- `platform/src/host/Comuki.Host/Workers/Api/WorkerEndpoints.cs`

### Identity / auth (10)
- `platform/src/host/Comuki.Host/Auth/Security/CookieSignerAdapter.cs`
- `platform/src/host/Comuki.Host/Auth/Security/SubjectScopeMiddleware.cs`
- `platform/src/host/Comuki.Host/Auth/BootstrapAdminOptions.cs`
- `platform/src/host/Comuki.Host/Security/ProductionSecrets/ProductionSecretValidator.cs`
- `platform/src/host/Comuki.Host/Security/Cors/CorsOptions.cs`
- `platform/src/host/Comuki.Host/HostComposer.cs`
- `platform/src/host/Comuki.Host/Realtime/RealtimeExtensions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcStartHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcCallbackHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcAccountLinker.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcIdTokenValidator.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcOptions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcProviderOptions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Oidc/OidcPkce.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/ApiKeys/ApiKeyHasher.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/ApiKeys/ApiKeyIssuer.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Options/ApiKeyOptions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/ApiKeys/ApiKeyToken.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Users/InviteUserHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Application/Sessions/LoginHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/ApiKeys/ApiKeyAuthenticationHandler.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Authorization/RequiresPermissionFilter.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Cookies/UserAuthenticationService.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/Security/Cookies/CookieAuthOptions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Infrastructure/IdentityAuthExtensions.cs`
- `platform/src/modules/Identity/Comuki.Modules.Identity.Domain/Roles/RoleMatrix.cs`
- `platform/src/shared/Comuki.Shared.Kernel/Scoping/SubjectScope.cs`

### Intake (1)
- `platform/src/modules/Intake/Comuki.Modules.Intake.Application/Tickets/WebhookIntakeService.cs`
- `platform/src/modules/Intake/Comuki.Modules.Intake.Domain/Tickets/TicketProvider.cs`

### Artifacts (1)
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackagerService.cs`
- `platform/src/modules/Artifacts/Comuki.Modules.Artifacts.Application/Packaging/RunArtifactPackager.cs`

### Proxy (3)
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyConfigProvider.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Yarp/ProxyTransforms.cs`
- `platform/src/modules/Proxy/Comuki.Modules.Proxy.Infrastructure/Auth/VirtualKeyAuthenticationHandler.cs`

### MCP (1)
- `platform/src/host/Comuki.Host/Mcp/McpServer.cs`

### Compute / worker (1)
- `platform/src/engine/Comuki.Engine.Compute/Security/WorkerTokenIssuer.cs`
- `platform/src/host/Comuki.Host.Translator/Runtime/PiRunner.cs`

### Config / deps (4)
- `deploy/.env.example`
- `platform/src/host/Comuki.Migrator/appsettings.json`
- `platform/src/host/Comuki.Host.Brain/appsettings.json`
- `Directory.Packages.props`
- `dashboard/package.json`
- `comuki.slnx`

### Reference docs (3)
- `audit-product-report.md` (the prior business-logic audit)
- `product-decisions.md` (Q1–Q43 owner decisions)
- `.agents/STATE.md` (the state file)

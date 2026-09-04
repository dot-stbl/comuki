# OIDC / OAuth login

Comuki's Identity module fronts browser and bearer traffic with **cookie**
sessions by default; OIDC is opt-in per provider (Keycloak, Auth0,
Azure AD, anything OIDC-compliant). The browser-driven flow is the
"Continue with …" button the SPA renders in real mode.

> Issue: S4 — Identity (#12). Modules touched: `Identity`, `Host`.

## Surface

```
browser → GET /api/v1/auth/oidc/{provider}/start[?returnTo=...]
        ↳ 302 → {provider} authorize endpoint (PKCE S256)
        ↳ browser authenticates, consent
        ↳ 302 → GET /api/v1/auth/oidc/callback
        ↳ OidcCallbackHandler validates state (single-use DB row),
          exchanges the code at the IdP's token endpoint (form-encoded
          POST + Basic auth + PKCE verifier), verifies the id_token
          against the JWKS, runs OidcAccountLinker, signs the cookie
          via IdentityPrincipalBuilder, and 302s to `returnTo` or `/`.
        ↳ SPA re-reads /api/v1/auth/me, picks up the cookie
```

The browser does the whole dance — `dashboard/src/domains/auth/api/oidc-start.ts`
hands the navigation to `window.location.assign` instead of going through
`fetch`. Why: kubb's generated fetch follows the 302 to the IdP and
`response.json()`-s an HTML page (it throws). `redirect: 'manual'` is the
alternative but means two round-trips where one will do.

## Code-flow — manual, no framework handler

The OIDC code-flow runs as a manual handler pair in `Identity.Application`:

| Concern | File |
|---------|------|
| PKCE generation (verifier + S256 challenge per RFC 7636) | `OidcPkce` |
| Authorize URL with all OAuth2 params | `OidcAuthorizationUrlBuilder` |
| State row (id = URL-safe UUIDv7; binds verifier + returnTo + redirectUri) | `OidcState` / `IOidcStateStore` / `OidcStateStore` |
| Discovery doc fetch + cache | `IOidcDiscovery` / `OidcDiscoveryCache` |
| Start (issues state row, returns authorize URL) | `OidcStartHandler` |
| Token exchange (form-encoded POST, Basic auth) | `IOidcTokenExchange` / `OidcTokenExchange` |
| id_token signature/issuer/audience/lifetime validation | `IOidcIdTokenValidator` / `OidcIdTokenValidator` |
| Callback (state consume → exchange → verify → link → cookie sign-in) | `OidcCallbackHandler` |
| Cookie sign-in (host-side adapter — owns ASP.NET plumbing) | `ICookieSigner` / `CookieSignerAdapter` |

The state store is **DB-backed** (`identity.oidc_states` table), not
cookie-based: the row id is the state token (UUIDv7, unguessable, single-use).
TTL is 5 minutes; a background sweep deletes expired rows. The store
exposes `SaveAsync`, `ConsumeAsync` (atomic delete + return), and
`DeleteExpiredAsync`.

The token exchange uses **Basic auth** for the client secret — never
the secret in the body. The PKCE verifier travels in the body as
`code_verifier`. The token endpoint answer is parsed and the id_token
verified against the discovery doc's JWKS, with issuer + audience
(expectedAudience = client id) + lifetime checks.

## Provider model

Each provider lives in `auth:oidc:providers[]`:

| Setting           | Type     | Notes |
|-------------------|----------|-------|
| `Name`            | required | Used as the state row's `provider` column and in the authorize URL. Must match `VITE_OIDC_PROVIDER`. |
| `Authority`       | required | OIDC discovery base — e.g. `https://kc.example.com/realms/comuki`. |
| `ClientId`        | required | The client registered at the provider. |
| `ClientSecretEnv` | required | **Name of** the env var holding the secret. The secret is read once at startup; the env var name is the only thing committed. |
| `RequireHttps`    | optional | Defaults to `true`. Set to `false` for an `http://` dev authority (local containers only — never a production deployment). |

Empty `auth:oidc` is legitimate — local login works without any IdP;
providers are opt-in per deployment.

Sources:

- `platform/src/modules/Identity/.../Oidc/OidcOptions.cs`
- `platform/src/modules/Identity/.../Oidc/OidcProviderOptions.cs`
- `platform/src/host/Comuki.Host/HostComposer.cs` — `AddIdentityApplication`
  + `AddIdentityAuth` + manual OIDC handler registrations

## Default scheme — Cookie, not OIDC

The cookie scheme is hardcoded as the default (`AuthSchemes.Cookie` =
`"Comuki.Cookie"`) in `IdentityAuthExtensions.AddIdentityAuth`. The
reasoning:

- SignIn/SignOut/challenge on every endpoint map to the browser session
  regardless of whether the user arrived via password or OIDC.
- Bearer requests forward to the API-key scheme (`Authorization: Bearer ck_…`),
  not to OIDC — workers don't authenticate via OIDC.

OIDC is **one path into** the cookie session, not a replacement for it.
Once the IdP ticket lands, the host replaces the principal with the
module's cookie grammar (`IdentityPrincipalBuilder.BuildForCookie(account)`)
and signs the cookie in via the standard `SignInAsync(CookieScheme, …)` path.
From that point on, every request looks identical to a password login.

## Callback path — unified, no provider in the path

The unified callback is `/api/v1/auth/oidc/callback` (no provider in the
URL). The provider name is recovered from the state row the IdP's
`state` query parameter resolves to. This is one fewer redirect-URI
per provider to register at the IdP, and it lets the IdP send back
multiple providers without the host route changing.

```
http://localhost:17025/api/v1/auth/oidc/callback
https://app.example.com/api/v1/auth/oidc/callback
```

The start endpoint still namespaces by provider (the SPA's button knows
the provider) — only the callback path is unified.

## EF model — `OidcLink` and `OidcState`

The link between an IdP subject and a local account lives in
`OidcLinkStore` (`identity.oidc_links` table). The state row lives in
`identity.oidc_states`:

| Column                  | Notes |
|-------------------------|-------|
| `id`                    | UUIDv7 — the URL-safe state token the IdP echoes back |
| `provider`              | matches `auth:oidc:providers[]:Name` |
| `code_verifier`         | PKCE verifier paired with the `code_challenge` sent at authorize time |
| `code_challenge_method` | always `S256` |
| `redirect_uri`          | the URI the IdP will call back to; verified at token-exchange time |
| `return_to`             | the in-app path the operator was bounced from |
| `created_at`            | UTC timestamp |
| `expires_at`            | UTC timestamp; rows past this are dead |

`OidcAccountLinker.HandleAsync(OidcLinkRequest)` (in `Identity.Application`)
resolves the link:

- existing `(provider, subject)` → reuse `user_id`
- no link yet, email matches an existing user → provision the link, no
  new account
- no link yet, no email match → provision a new account, then the link

`OidcCallbackHandler` logs `"Oidc provider {Provider} provisioned
local account {Email}"` when a fresh account was created.

## Frontend flow

`dashboard/src/domains/auth/api/oidc-start.ts` is the one place that
initiates the dance:

```ts
const OIDC_PROVIDER_START_PATH = (provider: string): string =>
  `/api/v1/auth/oidc/${provider}/start`

export function startOidcFlow(
  baseUrl: string,
  provider: string,
  returnTo?: string,
): void {
  const path = OIDC_PROVIDER_START_PATH(provider)
  const target =
    returnTo && returnTo.startsWith("/") && !returnTo.startsWith("//") && !returnTo.startsWith("/\\")
      ? `${baseUrl}${path}?returnTo=${encodeURIComponent(returnTo)}`
      : `${baseUrl}${path}`

  window.location.assign(target)
}
```

`returnTo` round-trips through the IdP and lands the operator back where
they were trying to go (same protocol as the password sign-in).

Why hand the navigation to the browser instead of going through kubb:

1. **kubb's fetch follows 302s by default and tries `response.json()` on
   the IdP's HTML page.** Two wrong paths we ruled out:
   - `useStartOidcQuery` through kubb-client — throws on the IdP HTML.
   - `fetch(..., { redirect: 'manual' })` — read the 302's `Location`
     header and `window.location.assign` it; works, but two round-trips.
2. **`window.location.assign` is one helper call**, the browser does
   the rest.

The backend handles the callback directly — the SPA never sees it.
No FE callback route is needed.

`VITE_OIDC_PROVIDER` (see [fesettings.md](./fesettings.md)) decides
which provider the SPA names in the call. Mock mode ignores it
(`auth.store` owns the button instead).

## Configuration

```toml
# appsettings.json (or env: AUTH_OIDC_PROVIDERS__0__NAME=keycloak)
[auth.oidc.providers]
# "providers" is a list, not a dict — multiple IdPs coexist.
# Index 0 here is illustrative.
[[auth.oidc.providers]]
name             = "keycloak"
authority        = "https://kc.example.com/realms/comuki"
clientId         = "comuki-dashboard"
clientSecretEnv  = "COMUKI_OIDC_CLIENT_SECRET"
requireHttps     = true   # default; set false only for local containers

# Then in the host env / .env (compose or systemd):
COMUKI_OIDC_CLIENT_SECRET=<secret-from-the-keycloak-client>
```

### Provider-specific setup snippets

#### Keycloak

In the realm JSON (or via the admin UI), under **Clients →
comuki-dashboard**:

- **Root URL**: `https://app.example.com`
- **Valid redirect URIs**: `https://app.example.com/api/v1/auth/oidc/callback`
- **Web origins**: `https://app.example.com` (for local dev:
  `http://localhost:17073` — port pool reservation 17173 is the
  dashboard, 17025 is the host)
- **Access settings → Confidential client**: off (public client
  recommended; PKCE S256 is mandatory regardless).
- **Capability config → Client authentication**: off.
- **Capability config → Standard flow**: enabled.
- **Capability config → Direct access grants**: off.

The Keycloak realm import at `deploy/keycloak/comuki-realm.json`
ships the `comuki-dashboard` client and a `test-user@comuki.test` /
`test-pass-123` test user. Start Keycloak with
`podman compose --profile keycloak up -d`.

#### Google

In **Google Cloud Console → APIs & Services → Credentials → OAuth 2.0
Client IDs**:

- **Application type**: Web application.
- **Name**: `comuki-dashboard`.
- **Authorized JavaScript origins**: `https://app.example.com`.
- **Authorized redirect URIs**:
  `https://app.example.com/api/v1/auth/oidc/callback`.
- Configure the consent screen with the support email and the
  scopes the application requests (`openid`, `profile`, `email`).
- Use the **Client ID** and **Client secret** values in
  `OidcProviderOptions.ClientId` and `COMUKI_OIDC_CLIENT_SECRET`.

```toml
[[auth.oidc.providers]]
name             = "google"
authority        = "https://accounts.google.com"
clientId         = "1234567890-abc.apps.googleusercontent.com"
clientSecretEnv  = "COMUKI_OIDC_CLIENT_SECRET"
```

#### Azure AD / Entra ID

In **Microsoft Entra admin center → App registrations**:

- **Name**: `comuki-dashboard`.
- **Supported account types**: single tenant (or multi-tenant if needed).
- **Redirect URI (Web)**: `https://app.example.com/api/v1/auth/oidc/callback`.
- Under **Certificates & secrets → Client secrets**: add a secret,
  copy its value into `COMUKI_OIDC_CLIENT_SECRET`.
- Under **API permissions → Microsoft Graph → Delegated**: ensure
  `openid`, `profile`, `email` are granted.

```toml
[[auth.oidc.providers]]
name             = "azuread"
authority        = "https://login.microsoftonline.com/<tenant-id>/v2.0"
clientId         = "00000000-0000-0000-0000-000000000000"
clientSecretEnv  = "COMUKI_OIDC_CLIENT_SECRET"
```

The authority URL must include the tenant id (or `common` / `organizations`
for multi-tenant); the discovery endpoint is appended automatically by
the OidcDiscoveryCache.

## Security posture

- **PKCE S256** — every flow issues a 32-byte random verifier + SHA-256
  challenge per RFC 7636. Public clients (Keycloak dev profile)
  require PKCE; confidential clients lose nothing by it.
- **Single-use state** — the DB row is deleted on read (atomic
  ConsumeAsync); a replayed `state` returns null and the handler
  surfaces `oidc.state_mismatch`.
- **`code_verifier` in the body, client_secret in Basic auth** —
  the secret never appears in the body, never in the URL, never in
  a log line.
- **id_token signature verification** — the handler validates
  issuer + audience (expectedAudience = client id) + lifetime +
  signing key (against the discovery doc's JWKS). Clock skew is
  two minutes.
- **`SaveTokens = false` semantics** — we do not persist the IdP's
  access/refresh tokens; the cookie is the only thing that survives.
- **returnTo path safety** — the callback only follows an in-app
  return path that starts with `/` and does not start with `//` or
  `/\` (off-site / protocol-relative). Anything else falls back to `/`.
- **Secrets never in config** — only `ClientSecretEnv` is committed;
  the secret itself lives in the deployment's secret store.

## Anti-patterns

- ❌ Putting the OIDC client secret in `appsettings.json` or
  `auth:oidc:providers[]` directly — only the env var **name** is
  configured.
- ❌ Building the OIDC start URL by string-concatenating
  `https://...comuki...`/`api/v1/auth/oidc/${name}/start` outside
  `startOidcFlow` — the SPA is mock-first and the host is
  URL-relative; let `startOidcFlow(baseUrl, provider, returnTo)` own it.
- ❌ Using a per-provider callback path — `/api/v1/auth/oidc/{provider}/callback`
  forces an extra redirect-URI registration per provider at the IdP.
  The unified `/api/v1/auth/oidc/callback` is the only path.
- ❌ Returning the id_token claims without `ValidateIssuerSigningKey = true`
  on the `TokenValidationParameters` — a forged id_token would then
  silently produce a local account.
- ❌ Trusting a `state` parameter that the cookie / session has not
  validated — OIDC's `state` is the CSRF protection, not a hint.

## Related

- [install.md](./install.md) — Migrator DB credentials; same machinery
  for OIDC client secrets.
- [fesettings.md](./fesettings.md) — `VITE_OIDC_PROVIDER` and the
  mock/real switch.
- [openapi-codegen.md](./openapi-codegen.md) — kubb regenerates
  client/hooks from the OIDC endpoints; FE keeps kubb's `oidcStart`
  URL in sync automatically.
- [../../STATE.md](../../STATE.md) — S4 #12 closed; Identity module
  layout and decisions.
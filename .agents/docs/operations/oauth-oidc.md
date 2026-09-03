# OIDC / OAuth login

Comuki's Identity module fronts browser and bearer traffic with **cookie**
sessions by default; OIDC is opt-in per provider (Keycloak, Auth0,
Azure AD, anything OIDC-compliant). The browser-driven flow is the
"Continue with …" button the SPA renders in real mode.

> Issue: S4 — Identity (#12). Modules touched: `Identity`, `Host`.

## Surface

```
browser → GET /api/v1/auth/oidc/{provider}/start
        ↳ 302 → {provider} authorize endpoint (PKCE S256)
        ↳ browser authenticates, consent
        ↳ 302 → GET /api/v1/auth/oidc/{provider}/callback
        ↳ OidcLoginPostConfigure turns the IdP ticket into a local
          cookie via OidcAccountLinker + IdentityPrincipalBuilder
        ↳ 302 → /
        ↳ SPA re-reads /api/v1/auth/me, picks up the cookie
```

The browser does the whole dance — `dashboard/src/domains/auth/api/oidc-start.ts`
hands the navigation to `window.location.assign` instead of going through
`fetch`. Why: kubb's generated fetch follows the 302 to the IdP and
`response.json()`-s an HTML page (it throws). `redirect: 'manual'` is the
alternative but means two round-trips where one will do.

## Provider model

Each provider lives in `auth:oidc:providers[]` and becomes one
`AddOpenIdConnect` registration under the scheme name
`Comuki.Oidc.{Name}` (`AuthSchemes.Oidc(provider)`).

| Setting           | Type     | Notes |
|-------------------|----------|-------|
| `Name`            | required | Used in the scheme name and the callback path. Must match `VITE_OIDC_PROVIDER`. |
| `Authority`       | required | OIDC discovery base — e.g. `https://kc.example.com/realms/comuki`. |
| `ClientId`        | required | The client registered at the provider. |
| `ClientSecretEnv` | required | **Name of** the env var holding the secret. The secret is read once at startup; the env var name is the only thing committed. |
| `RequireHttps`    | optional | Defaults to `true`. Set to `false` for an `http://` dev authority (local containers only — never a production deployment). |

The validation runs in `IdentityAuthExtensions.AddOidcProviders`:
- empty / missing required fields → `InvalidOperationException` at startup
- missing env var for `ClientSecretEnv` → same
- `RequireHttps` defaults to `true` even when omitted — explicit `false`
  is the only way to opt into an HTTP authority

Sources:

- `platform/src/modules/Identity/.../Oidc/OidcOptions.cs`
- `platform/src/modules/Identity/.../Oidc/OidcProviderOptions.cs`
- `platform/src/modules/Identity/.../Infrastructure/IdentityAuthExtensions.cs` — `AddOidcProviders`
- `platform/src/host/Comuki.Host/Auth/Security/OidcLoginPostConfigure.cs` — scheme finish + callback wiring
- `platform/src/host/Comuki.Host/ApiRoutes.cs` — `AuthRoot = "api/v1/auth"`, `AuthOidcRoot = "api/v1/auth/oidc"`

## Default scheme — Cookie, not OIDC

The cookie scheme is hardcoded as the default (`AuthSchemes.Cookie` =
`"Comuki.Cookie"`) in `IdentityAuthExtensions.AddIdentityAuth`. The
reasoning:

- SignIn/SignOut/Challenge on every endpoint map to the browser session
  regardless of whether the user arrived via password or OIDC.
- Bearer requests forward to the API-key scheme (`Authorization: Bearer ck_…`),
  not to OIDC — workers don't authenticate via OIDC.

OIDC is **one path into** the cookie session, not a replacement for it.
Once the IdP ticket lands, the host replaces the principal with the
module's cookie grammar (`IdentityPrincipalBuilder.BuildForCookie(account)`)
and signs the cookie in via the standard `SignInAsync(CookieScheme, …)` path.
From that point on, every request looks identical to a password login.

## EF model — `OidcLink`

The link between an IdP subject and a local account lives in
`OidcLinkStore` (`identity.oidc_links` table):

| Column        | Notes |
|---------------|-------|
| `id`          | UUIDv7 |
| `user_id`     | FK → `identity.users` |
| `provider`    | matches `auth:oidc:providers[]:Name` |
| `subject`     | IdP `sub` claim — stable per provider per user |
| `email`       | The IdP's email at link time (best-effort; user can rename it locally) |
| `created`     | UTC timestamp |

`OidcAccountLinker.HandleAsync(OidcLinkRequest)` (in `Identity.Application`)
resolves the link:

- existing `(provider, subject)` → reuse `user_id`
- no link yet, email matches an existing user → provision the link, no
  new account
- no link yet, no email match → provision a new account, then the link

`OidcLoginPostConfigure` logs `"Oidc provider {Provider} provisioned
local account {Email}"` when a fresh account was created.

## Frontend flow

`dashboard/src/domains/auth/api/oidc-start.ts` is the one place that
initiates the dance:

```ts
const OIDC_PROVIDER_START_PATH = (provider: string): string =>
  `/api/v1/auth/oidc/${provider}/start`

export function startOidcFlow(baseUrl: string, provider: string): void {
  window.location.assign(`${baseUrl}${OIDC_PROVIDER_START_PATH(provider)}`)
}
```

Why hand the navigation to the browser instead of going through kubb:

1. **kubb's fetch follows 302s by default and tries `response.json()` on
   the IdP's HTML page.** Two wrong paths we ruled out:
   - `useStartOidcQuery` through kubb-client — throws on the IdP HTML.
   - `fetch(..., { redirect: 'manual' })` — read the 302's `Location`
     header and `window.location.assign` it; works, but two round-trips.
2. **`window.location.assign` is one helper call**, the browser does
   the rest.

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

Compose dev profile (`deploy/.env.example`):

```bash
# Dev Keycloak — http authority, public client, fake secret value
COMUKI_KEYCLOAK_ADMIN_USER=comuki
COMUKI_KEYCLOAK_ADMIN_PASSWORD=comuki_dev
# The Comuki-side env name lives in OidcProviderOptions.ClientSecretEnv:
#   auth:oidc:providers:0:ClientSecretEnv=COMUKI_OIDC_CLIENT_SECRET
COMUKI_OIDC_CLIENT_SECRET=anything-non-empty   # public client
```

The Keycloak realm import at `deploy/keycloak/comuki-realm.json` ships
the `comuki-dashboard` client and a `test-user@comuki.test` /
`test-pass-123` test user. Start Keycloak with
`podman compose --profile keycloak up -d`.

### Redirect URIs at the IdP

The host registers the callback at
`/api/v1/auth/oidc/{provider}/callback` (rewritten from the default
`/signin-oidc` by `OidcLoginPostConfigure`). Register the absolute URL
at the IdP, e.g.:

```
http://localhost:17025/api/v1/auth/oidc/keycloak/callback
https://app.example.com/api/v1/auth/oidc/keycloak/callback
```

Mis-registered callback = 400 from the IdP with no useful error;
check the redirect URI list first when debugging.

## Security posture

- **PKCE S256** — every flow is `UsePkce = true`. Public clients
  (Keycloak dev profile) require PKCE; confidential clients lose
  nothing.
- **`SaveTokens = false`** — the IdP access/refresh tokens do not
  persist in the cookie. The host only uses the IdP ticket once
  (account linking) and the cookie is the only thing that survives.
- **`SignInScheme = AuthSchemes.Cookie`** — the OIDC ticket is replaced
  by the cookie principal before the framework's cookie signing fires;
  the cookie security-stamp validation (`OnValidatePrincipal` in
  `IdentityAuthExtensions.ConfigureCookie`) catches bumped
  `tokens_version` or disabled accounts.
- **Secrets never in config** — only `ClientSecretEnv` is committed;
  the secret itself lives in the deployment's secret store.

## Anti-patterns

- ❌ Putting the OIDC client secret in `appsettings.json` or `auth:oidc:providers[]`
  directly — only the env var **name** is configured.
- ❌ Trying to chain providers in the cookie's `OnValidatePrincipal`
  to call back into the IdP — the cookie is local; if it has to phone
  home, that's a refresh-token problem (deferred).
- ❌ Treating OIDC as a replacement for the cookie scheme — OIDC is the
  *provisioning* path; the cookie is the *session* path.
- ❌ Building the OIDC start URL by string-concatenating
  `https://...comuki...`/`api/v1/auth/oidc/${name}/start` outside
  `startOidcFlow` — the SPA is mock-first and the host is
  URL-relative; let `startOidcFlow(baseUrl, provider)` own it.

## Related

- [install.md](./install.md) — Migrator DB credentials; same machinery
  for OIDC client secrets.
- [fesettings.md](./fesettings.md) — `VITE_OIDC_PROVIDER` and the
  mock/real switch.
- [openapi-codegen.md](./openapi-codegen.md) — kubb regenerates
  client/hooks from the OIDC endpoints; FE keeps kubb's `oidcStart` URL
  in sync automatically.
- [../../STATE.md](../../STATE.md) — S4 #12 closed; Identity module
  layout and decisions.
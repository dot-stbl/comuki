# Operations notes — security

> Operational security posture for Comuki. New here? Read
> [`install.md`](./install.md) for the deployment-time secrets
> discipline, [`oauth-oidc.md`](./oauth-oidc.md) for the OIDC dance,
> and [`runbook.md`](./runbook.md) for the operational response
> playbook. This file is the single page a compliance reviewer can be
> pointed at for "where does the token live, who can read the audit
> log, what stops an open redirect".

## Token storage — server-side signed cookie, `httpOnly`

The cookie auth scheme is server-side, signed, `httpOnly`. The
plaintext session token never reaches the browser's JavaScript heap:

- Server-side signing — `CookieSignerAdapter` derives the cookie value
  from a server-only secret (`Host:Cookie:SigningKey`); a browser
  holding the cookie cannot reconstruct or extend it.
- `httpOnly` — the browser exposes the cookie to `fetch` / XHR but
  not to `document.cookie`, so any XSS that lands in the SPA cannot
  exfiltrate it.
- Server-side expiry — the cookie carries a `Max-Age` matching the
  cookie session TTL; rotation is server-driven, not browser-driven.

The dashboard's `kubb-client` transport sets
`credentials: 'include'` on every fetch, so the cookie travels
cross-origin safely between the Vite dev server (`localhost:17173`)
and the host (`localhost:NNNN`). A future change to `localStorage`
for the session token would break the XSS-resistance story — it is
explicitly off the table.

## Cookie attributes

The host's `AddComukiAuthentication` configures the auth cookie with:

- `SameSite=Lax` — the cookie rides top-level navigations but not
  cross-site `<form>` POSTs or embedded `<iframe>` requests. The OIDC
  callback (`302 → /api/v1/auth/oidc/callback`) is a top-level GET
  and therefore survives; a third-party iframe cannot trigger it.
- `Secure` in `Production` deploys — the cookie only rides over HTTPS.
  Local dev runs HTTP and accepts the trade-off; `Production`
  refuses to start when a `Secure`-required cookie would ride over
  plain HTTP. The check lives in
  `ProductionSecretValidator` and the cookie configuration in
  `HostComposer`.
- `Path=/` — the cookie is sent to every host endpoint, including the
  OIDC start/callback paths.
- `Domain` unset — the cookie is host-scoped, not shared across
  subdomains. A multi-tenant deploy with `*.example.com` should set
  `Domain=.example.com` explicitly.

## Rate limits

Per-endpoint rate limiting is composed in `Security/RateLimit/`:

- Default — **600 requests per minute per API key**, sliding window.
  The default is set in `RateLimitOptions`; override per deployment
  via `RateLimit:RequestsPerMinute` in `appsettings.json` or env.
- Per-endpoint overrides — the host attaches named policies via
  `RateLimitAttribute`. The OIDC start/callback endpoints are
  exempted: an IdP callback arriving at any reasonable rate is a
  legitimate flow, and a tighter cap would lock out operators who
  hit "Continue with …" twice in quick succession.
- Identity endpoints (`/api/v1/keys`, `/api/v1/users`,
  `/api/v1/auth/oidc/*`) — additional per-subject limits live
  alongside the per-API-key window; see `RateLimitInstaller` for the
  policy table.

When a caller exceeds the limit, the host returns `429 Too Many
Requests` with a `Retry-After` header. The dashboard renders the
error as a "too many requests" inline message and a retry button.

## Bootstrap admin rotation (manual)

The bootstrap admin's password is **not** rotated automatically. The
v1 contract is a manual rotation; the procedure lives in
[`runbook.md` §"Bootstrap admin password rotation"](./runbook.md).

Rotation steps in short:

1. Stop the host.
2. Generate a fresh password (`openssl rand -base64 32`).
3. Set `Host:BootstrapAdmin:Password` to the new value via env or
   secrets store.
4. Restart the host. The `BootstrapAdminSeeder` sees a new value
   and updates the existing admin's `PasswordHash`.
5. Sign in with the new password and verify the old one is rejected.

There is no `PasswordRotatedAt` column, no policy that says "rotate
within 90 days", and no automated reminder. A future PR adds
rotation policy enforcement; the audit log will then carry a
"rotation overdue" event.

## XSS resistance

The dashboard is built with React + shadcn-style primitives that
render text content through the JSX `{}` escape, so user-supplied
strings never reach `innerHTML` or `eval`. Three further guarantees:

- **No `fetch` in components** — the SPA's only HTTP transport is
  the kubb-generated client, which goes through
  `kubb-client.ts` and never accepts arbitrary HTML.
- **No inline JS** — the build emits a single bundled JS file with a
  strict CSP that disallows `'unsafe-inline'` and `'unsafe-eval'`.
  See `deploy/nginx.conf` for the emitted header set.
- **Content-Security-Policy** — the host emits a CSP that whitelists
  the dashboard's bundle and forbids third-party script loads.
  Operators who add a third-party analytics script must extend the
  CSP allowlist — there is no implicit `*` in the policy.

The `kubb-client` `Content-Type: application/json` header means the
host treats every JSON response as untrusted text; no
auto-evaluation, no JSONP, no inline-script path.

## CSRF

CSRF defence rests on the cookie attribute set rather than a
synchronizer token:

- `SameSite=Lax` — third-party `<form>` POSTs cannot ride the cookie;
  only top-level navigations and same-origin `fetch` carry it.
- `credentials: 'include'` in the kubb transport — the cookie rides
  every same-origin `fetch` automatically; no per-call opt-in.
- No cross-origin POST endpoints — the host's API surface is
  same-origin from the SPA's perspective; cross-origin POSTs do not
  reach the auth cookie.

For the OIDC flow specifically: the callback is a top-level GET,
which `SameSite=Lax` permits. A third-party site cannot forge the
state parameter (single-use DB row) or the PKCE verifier (server-side
random, never logged).

## Open redirects

The OIDC start handler carries a `returnTo` query parameter that
determines where the SPA lands after the IdP callback. Two guards
prevent an attacker from weaponising it as an open redirect:

1. **Path-only enforcement** — `OidcCallbackHandler` accepts
   `returnTo` only when it begins with a single `/` and does NOT
   begin with `//`, `/\`, or any other scheme prefix. A `returnTo`
   of `https://evil.example.com/x` is rejected and falls back to the
   default (`/`).
2. **Server-side state row** — the `state` parameter is a single-use
   row in `identity.oidc_states`; the callback's lookup is by
   primary key, not by `returnTo`. A forged state never lands at
   the callback at all, but if it did, the row's missing `state`
   token would be rejected and the flow would 302 to the login
   page with `?reason=oidc-failed`.

The dashboard's own navigation uses TanStack Router's typed
`<Link to="/x">` rather than `<a href="/x">`; an in-app link cannot
be coerced to an off-origin destination.

## Audit log

Comuki does not carry a dedicated `audit_events` table in v1. The
"audit log" is reconstructed from three existing log streams:

- **`Microsoft.Extensions.Logging` structured logs** — every state
  change is logged with `Information` level and a structured payload
  (`UserId`, `Prefix`, `RunId`, etc.). The dashboard's
  `/api/v1/users/{id}/oidc-link` handler logs the actor's
  `SubjectId`, the link's `Provider` and `Subject`; the
  `SetUserDisabledHandler` logs `UserId` and the new `Disabled`
  state; the `BootstrapAdminSeeder` logs the admin's email on first
  creation.
- **OIDC journal (`identity.oidc_states`)** — every flow's
  `state` token, PKCE verifier, `redirect_uri`, and `return_to`
  lives in this table for the TTL window. After expiry the
  `OidcStateSweeper` deletes the row, so the journal is a *bounded*
  audit of the recent past, not a permanent record.
- **API-key audit (no row)** — `ApiKeyAuthenticationHandler` logs
  every successful and failed authentication with `KeyPrefix`,
  `UserId`, and the request path. There is no per-key rotation
  history today; a key's `created_at` and `last_used_at` columns
  are the only on-disk record.

What is **not** logged today:

- The actor's `SubjectId` for the `SetUserDisabledHandler` —
  `UserId` is logged but not who acted. A compliance audit must
  join the journal with the bootstrap admin's user record.
- API-key tenant-scope changes (`PUT /api/v1/keys/{id}` style) —
  no event row, no log line. A `last_modified_by` audit column is a
  v2 item.
- `OidcLink` cascades (a project-level OIDC disable) — no event.

An operator grepping for a compliance trail starts with the host's
structured log sink (default: console + OTel collector), joined with
the `oidc_states` table for the recent window.

## Related

- [`install.md`](./install.md) — deployment-time secrets, env-var
  discipline, migrator credentials.
- [`oauth-oidc.md`](./oauth-oidc.md) — the OIDC dance, the
  `returnTo` guard, the state-row lifecycle.
- [`runbook.md`](./runbook.md) — bootstrap admin rotation,
  incident response, key revocation.
- [`proxy.md`](./proxy.md) — virtual-key authentication, the
  `X-Comuki-Tenant` header, the disabled-owner check.

# Dashboard environment — `VITE_*` variables

The dashboard reads a small, deliberate set of `VITE_*` variables at
build/start time. Schema lives in
`dashboard/src/shared/config/env.ts`; the contract is enforced by
zod. There is no "config file"; the variables come from
`dashboard/.env.local` (gitignored) or the shell that runs `bun run
dev`/`vite build`/`bun run generate-api`.

## The full list

| Variable             | Type              | Default                                                       | What |
|----------------------|-------------------|---------------------------------------------------------------|------|
| `VITE_USE_MOCK`      | `true` / `false`  | unset ⇒ `true` (mock-first). `false` switches every read over to kubb. | Mock-first vs real-backend switch. |
| `VITE_API_BASE_URL`  | URL string        | `""`                                                          | Where kubb-generated clients route. Empty → kubb-client throws. |
| `VITE_OIDC_PROVIDER` | string            | unset                                                         | Name of the OIDC provider configured at the host (`auth:oidc:providers[]:Name`). |
| `VITE_REPO_URL`      | URL string        | `https://github.com/dot-stbl/comuki` (this repo)              | Topbar repository link. Empty string takes the link out of the bar. |
| `VITE_COMMIT_SHA`    | string            | `""`                                                          | Build provenance footer; CI writes the real SHA, local reads empty. |
| `VITE_DEPLOY_ENV`    | `local` / `staging` / `production` | unset ⇒ `local`                | Where this build is meant to run. Rendered in the login footer. |

The schema parses every variable once at import time; `env` is the
singleton the rest of the dashboard imports. Wrong types or unknown
values fail the build, not the first request.

## Mock-first vs real-backend

`VITE_USE_MOCK` is the master switch.

### Mock mode (`VITE_USE_MOCK=true`, default)

- `dashboard/src/shared/api/mock/*` seeds serve every domain.
- `VITE_API_BASE_URL` is not read.
- The kubb-client adapter is bypassed; kubb's `importPath` is
  imported but never called.
- Useful for: fresh clone walkthrough, `bun run dev:mock`,
  Storybook (`bun run storybook`), screen development without a
  live host.

### Real mode (`VITE_USE_MOCK=false`)

- Every domain's queries/mutations call kubb-generated clients.
- `VITE_API_BASE_URL` **must** be set; otherwise the kubb-client
  adapter throws on first call:

  ```
  [kubb-client] VITE_API_BASE_URL is not set.
    Generated hooks call the real backend; the dashboard mock layer
    (src/shared/api/mock/*) is hand-written and not visible to kubb.
    Set VITE_API_BASE_URL=http://localhost:17173 (or your host port)
    in .env.local and restart vite.
  ```

- Useful for: e2e against a real host, dashboards that depend on
  real data, observability validation.

`VITE_USE_MOCK` is also a `bun run dev` vs `bun run dev:mock`
distinction — the script in `package.json` is:

```json
"scripts": {
  "dev":         "vite",
  "dev:mock":    "vite",
  "predev":      "bun run generate-api"
}
```

`predev` runs `generate-api` (which builds the BE then runs kubb)
before `vite`. With real mode you want that predev to keep the
client in sync. With mock mode you'd rather skip it and run
`bun run dev:mock` directly — the mock seeds don't depend on the
generated client.

## Per-domain switch

Some domains still expose mock-first regardless of `VITE_USE_MOCK`:
their queries throw when called in real mode with a message that
points to the kubb-generated client when it exists:

```ts
// dashboard/src/domains/tasks/api/queries.ts
throw new Error("tasks API not implemented — set VITE_USE_MOCK=true")
```

The runs / identity / projects / inbox / OIDC domains are wired to
real backends (FE wire-up slices 1–5, see
[STATE.md](../../STATE.md)). Tasks / chat / cost / compute /
knowledge / models / sources / queue / verify / observability /
home are still on mocks; tracked under S7 (#7).

## `.env.local` workflow

```bash
# 1. Copy the template.
cp dashboard/.env.example dashboard/.env.local

# 2. Edit values for your dev setup.
$EDITOR dashboard/.env.local

# 3. Verify env.ts parsed without throwing.
cd dashboard && bun run typecheck

# 4. Run with the right script.
bun run dev:mock    # no API_BASE_URL needed; mock seeds
# OR
bun run dev         # predev rebuilds API client; real backend
```

`.env.local` is gitignored. `.env.example` is committed and ships
the comments explaining each variable.

## Auth — `VITE_OIDC_PROVIDER`

In real mode the SPA renders a "Continue with …" button whose target
is `/api/v1/auth/oidc/{VITE_OIDC_PROVIDER}/start`. The value must
match a configured `auth:oidc:providers[]:Name` at the host:

```bash
# Host config (appsettings.json / env)
auth.oidc.providers[0].Name=keycloak
auth.oidc.providers[0].Authority=https://kc.example.com/realms/comuki
auth.oidc.providers[0].ClientId=comuki-dashboard
auth.oidc.providers[0].ClientSecretEnv=COMUKI_OIDC_CLIENT_SECRET

# Dashboard
VITE_OIDC_PROVIDER=keycloak
```

Mock mode ignores `VITE_OIDC_PROVIDER` entirely — `auth.store` is
the source of truth for the mock button. Set the variable only when
you're talking to a real host.

See [oauth-oidc.md](./oauth-oidc.md) for the full OIDC flow,
including browser-driven start (`window.location.assign`) and the
cookie-vs-OIDC boundary.

## Deployment env

`VITE_DEPLOY_ENV` is informational — it's rendered in the login
footer alongside `VITE_COMMIT_SHA` so operators see which build
they're looking at. It does **not** change behavior; the bundler
treats staging and production the same.

`VITE_COMMIT_SHA` should be written by CI (`git rev-parse --short
HEAD | tr -d '\n'` → `VITE_COMMIT_SHA=…`). Locally it stays empty
and the footer renders the empty slot gracefully.

`VITE_REPO_URL` defaults to this repo's URL. A fork sets it to its
own address; a deployment with no public source sets it to the
empty string (`VITE_REPO_URL=`) — the topbar link is removed rather
than leaving an icon that goes nowhere. Anything that isn't a
`http(s)://` URL parses to `null` for the same reason
(`javascript:` is rejected).

## Sources

- `dashboard/src/shared/config/env.ts` — zod schema, parser, exported
  `env` singleton.
- `dashboard/src/shared/api/kubb-client.ts` — adapter that
  enforces `VITE_API_BASE_URL` and forces `credentials:'include'`.
- `dashboard/.env.example` — annotated template, committed.
- `dashboard/.env.local` — gitignored, per-developer.
- `dashboard/package.json` — `dev` / `dev:mock` / `generate-api`
  scripts.

## Related

- [oauth-oidc.md](./oauth-oidc.md) — how `VITE_OIDC_PROVIDER`
  participates in the start URL.
- [openapi-codegen.md](./openapi-codegen.md) — the kubb-client
  adapter that's gated by `VITE_API_BASE_URL` here.

## Anti-patterns

- ❌ Reading `import.meta.env.VITE_*` at call sites instead of going
  through the parsed `env` singleton — bypassing the zod schema
  reintroduces untyped access.
- ❌ `VITE_API_BASE_URL=""` and `VITE_USE_MOCK=false` — the adapter
  throws, but the throw is at first call, not at import; tests catch
  it but operator eyes don't. Set `VITE_USE_MOCK=true` until the host
  is reachable.
- ❌ `VITE_OIDC_PROVIDER=anything` — it has to match a real
  `auth:oidc:providers[]:Name` at the host, or the IdP authorize call
  404s.
- ❌ Setting `VITE_REPO_URL` to a non-http(s) URL — `javascript:`,
  `file:`, `data:`, etc. parse to `null` and the link is removed
  silently. Misleading to debug from the rendered DOM.
- ❌ Using `VITE_*` for anything secret — these are inlined into the
  client bundle. The OIDC client secret is `ClientSecretEnv` (env
  var name) at the host; the SPA only ever sees the start URL.
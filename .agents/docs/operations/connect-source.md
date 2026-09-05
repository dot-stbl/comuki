# Connect a source — operator guide

How to wire a tracker webhook into a running Comuki host, end-to-end:

1. **Mint the secret** on the tracker side. Every supported provider
   (GitHub, GitLab, Yandex Tracker, Jira) issues a per-webhook secret
   the host reuses on every delivery — the tracker stamps it into the
   `X-Hub-Signature-256` / `X-Gitlab-Token` header, the host re-derives
   the HMAC. The operator copies the secret, the host never sees it
   again.
2. **Put the secret on the host** as an env var, exactly once.
   `EnvSecretResolver` (`platform/.../EnvSecretResolver.cs`) is the only
   code path that reads the value, and it reads `Environment.GetEnvironmentVariable(name)`.
   The name itself (`COMUKI_GITHUB_TOKEN`, `COMUKI_GITLAB_TOKEN`, etc.)
   is what the host's `secret_env_ref` column stores; the value never
   touches the database.
3. **Wire it into the connect form** at `/sources/new`. The form
   collects the env-var NAME (not the value) under
   `secret env var`; the operator pastes the same name they set on
   the host. The settings json holds the per-provider config
   (`auth`, `account`, `baseUrl`); the form previews the literal
   blob the host will persist.
4. **Submit, copy the webhook path** the response carries, paste it
   into the tracker's webhook settings. The path is unguessable
   (`/api/hooks/{provider}/{16-char-key}`); do not let it leak.

The host refuses the create when the named env var is unset (HTTP
400 with `code = intake.secret_env_ref_unset`). The error names the
var; the operator knows to fix the host env, not the form.

## Replacing a secret

A connection's credential is written once. Replacing it is reconnecting
under a new id: create a new connection with a new env var, copy the
new webhook path, repoint the tracker, disconnect the old row.

The `secretEnvRef` field on an existing connection is editable so an
operator can rename the env var on the host side and patch the row
(without reconnecting), but the value is never sent — the host reads
the new env var, never the literal.

## Per-provider env-var names

The host does not constrain the env-var name. Convention (used by the
mock seed and by the integration test fixtures):

| Provider          | Suggested env var             |
|-------------------|-------------------------------|
| `github`          | `COMUKI_GITHUB_TOKEN`         |
| `gitlab`          | `COMUKI_GITLAB_TOKEN`         |
| `yandex-tracker`  | `COMUKI_TRACKER_TOKEN`        |
| `jira`            | `COMUKI_JIRA_TOKEN`           |
| `native`          | (none — no remote end)        |

The mock store stamps `MOCK_*` variants for tests; production runs
read whatever the operator sets.

## What the host never sees

- The literal credential value (the dashboard holds the env-var name,
  not the value).
- The tracker-side webhook path until the operator pastes it into the
  tracker's webhook UI.
- The `secretStoredAt` date on real connections (mock seed only).
  Real connections name an env var; the host stores no timestamp.

## Admission rules

A connection's watch (filter, mode, enabled) lives in a sibling row
on `admission_rules`, keyed by `(project_id, mode)`. The wire path
is `/api/v1/admission-rules`. The dashboard joins the sibling
collection on the client side by project id; the host exposes
`PUT /api/v1/sources/{sourceId}/rules/{ruleId}` as a routing alias
so the watch form's nested POST lands on the same handler as the
flat one.

## Related

- [`openspec/specs/intake/spec.md`](../../../openspec/specs/intake/spec.md) —
  `Source connection schema` and `Secret env var must resolve at
  write time` requirements.
- [`storage.md`](./storage.md) — Postgres schema layout.
- [`oauth-oidc.md`](./oauth-oidc.md) — same env-only-secret discipline
  applied to `COMUKI_OIDC_CLIENT_SECRET`.
- [`runbook.md`](./runbook.md) — on-call procedures (rotating secrets,
  re-keying an env var, invalidating webhook URLs).
# Optional Proxy (OpenAI / Anthropic passthrough)

Issue #8 / S9 T9.6 ships a thin, in-process reverse proxy for
OpenAI- and Anthropic-compatible HTTP endpoints. The proxy runs
inside the orchestrator host — there is no separate container.

## When to enable it

The proxy is **optional**. Turn it on when:

- The brain host (Comuki.Host.Brain) or a worker needs to reach an
  OpenAI- / Anthropic-compatible endpoint without each carrying its
  own per-call API key.
- Operators want to meter LLM spend against a project (every
  forwarded call writes a `usage_events` row with `source = 'proxy'`)
  and enforce a monthly USD cap per virtual key.
- A team needs an audit trail of who called which model with which
  project attribution.

Leave it **off** when the only LLM consumer is the brain host and
it already holds its own API key — the proxy adds an extra hop.

## Enabling the proxy

The proxy is configured under `Proxy:*` in the host's configuration.
Defaults come from `deploy/.env.example`; copy to `.env` and uncomment
the lines you need.

```bash
# Turn the module on
PROXY_ENABLED=true

# Tell the dashboard what to advertise on GET /v1/models
PROXY_KNOWN_MODELS=gpt-4o-mini,claude-sonnet-4

# Default pricing fallback (USD per million tokens)
PROXY_PRICING_DEFAULT_INPUT_USD_PER_MILLION=3
PROXY_PRICING_DEFAULT_OUTPUT_USD_PER_MILLION=15

# Per-model override (the key is the model id you pass to OpenAI / Anthropic)
PROXY_PRICING_GPT-4O__INPUT_USD_PER_MILLION=5
PROXY_PRICING_GPT-4O__OUTPUT_USD_PER_MILLION=15
```

Then one `Proxy:VirtualKeys:N:*` block per key. The token and the
upstream API key never live in the config file — both come from env.

```bash
# A "demo" OpenAI key bound to project 00000000-...-001, $10 / month cap
PROXY_VIRTUALKEYS_0__TOKEN=vkey_demo_replace_me
PROXY_VIRTUALKEYS_0__PROJECT_ID=00000000-0000-0000-0000-000000000001
PROXY_VIRTUALKEYS_0__PROVIDER=openai
PROXY_VIRTUALKEYS_0__BASE_URL=https://api.openai.com
PROXY_VIRTUALKEYS_0__APIKEY_ENV_REF=OPENAI_API_KEY
PROXY_VIRTUALKEYS_0__BUDGET_USD=10

# An "anthropic" key with no budget, model allow-list
PROXY_VIRTUALKEYS_1__TOKEN=vkey_anthropic_replace_me
PROXY_VIRTUALKEYS_1__PROJECT_ID=00000000-0000-0000-0000-000000000002
PROXY_VIRTUALKEYS_1__PROVIDER=anthropic
PROXY_VIRTUALKEYS_1__BASE_URL=https://api.anthropic.com
PROXY_VIRTUALKEYS_1__APIKEY_ENV_REF=ANTHROPIC_API_KEY
PROXY_VIRTUALKEYS_1__ALLOWED_MODELS_0=claude-sonnet-4

# The upstream API keys themselves
OPENAI_API_KEY=sk-...
ANTHROPIC_API_KEY=sk-ant-...
```

After editing `.env`, restart the host. **There is no hot reload of
virtual keys** in v1 — that's a follow-up.

## Verifying it works

With the host up:

```bash
# Anonymous — expect 401
curl -i http://localhost:17000/v1/chat/completions \
    -H 'content-type: application/json' \
    -d '{"model":"gpt-4o-mini","messages":[]}'

# Unknown bearer — expect 401
curl -i http://localhost:17000/v1/chat/completions \
    -H 'authorization: Bearer vkey_unknown' \
    -H 'content-type: application/json' \
    -d '{"model":"gpt-4o-mini","messages":[]}'

# Valid bearer — expect 200 with the upstream body
curl -i http://localhost:17000/v1/chat/completions \
    -H 'authorization: Bearer vkey_demo_replace_me' \
    -H 'content-type: application/json' \
    -d '{"model":"gpt-4o-mini","messages":[{"role":"user","content":"hi"}]}'

# Static catalogue
curl -i http://localhost:17000/v1/models \
    -H 'authorization: Bearer vkey_demo_replace_me'
```

The dashboard reads cost from `usage_events` with `source = 'proxy'`,
summed per project. The Costs API endpoint is
`GET /api/v1/projects/{projectId}/costs` — same call shape as for
brain / worker rows.

## Budget enforcement

When `BudgetUsd` is set on a virtual key, the proxy sums
`usage_events.cost_usd_micros` over the project's `proxy`-source
spend **for the current calendar month** before forwarding each
request. When the sum meets or exceeds the cap:

- The host answers `429 Too Many Requests`.
- The response carries a `Retry-After` header equal to the seconds
  until the start of the next calendar month.

Brain and worker spend are excluded — only `proxy`-source rows are
summed. Setting `BudgetUsd = null` disables the cap (the key forwards
forever).

To see live spend:

```sql
SELECT project_id, SUM(cost_usd_micros) AS spent_micros
FROM costs.usage_events
WHERE source = 'proxy'
  AND occurred_at >= date_trunc('month', now())
GROUP BY project_id;
```

## Authentication model

- The proxy uses the `VirtualKey` authentication scheme (no cookie,
  no API key).
- The challenge on a missing or unknown bearer is `401 Unauthorized`
  with `WWW-Authenticate: Bearer realm="comuki-proxy"`. There is no
  redirect — the proxy is an API surface, not a browser surface.
- The Identity module's cookie / API-key schemes are unaffected.
  Requests with a `ck_…` API-key bearer continue to authenticate
  against `Comuki.ApiKey`; requests with a `vkey_…` bearer
  authenticate against `Comuki.VirtualKey`.

## Failure modes

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Every call returns `401` | `Proxy:Enabled` is `false`; or `VirtualKeys` is empty; or the bearer token doesn't match any row | Check the host's startup logs and `PROXY_VIRTUALKEYS_*__TOKEN` |
| Calls return `429` immediately | The configured `BudgetUsd` is exhausted for this calendar month | Wait until next month, raise the budget, or sum the spend and reset |
| Upstream returns its own error to the caller | YARP proxies the upstream status unchanged — 4xx / 5xx from OpenAI / Anthropic reach the caller unchanged | Inspect the upstream's dashboard / rate limits |
| `OPENAI_API_KEY` env var unset | The startup log warns at key construction; the proxy returns 502 because the outbound `Authorization` header can't be set | Set the env var the `ApiKeyEnvRef` names and restart |
| `/health/ready` returns `503` with `comuki.proxy.keys` unhealthy | `Proxy:Enabled = true` but `VirtualKeys` is empty | Either turn the proxy off (`Proxy:Enabled = false`) or add at least one virtual key |

## Adding a virtual key

1. Mint a fresh token — anything the bearer pattern accepts is fine,
   but `vkey_*` is the convention. Tokens longer than 16 characters
   are validated by `ProxyOptionsValidator`.
2. Allocate a `ProjectId` (UUIDv7) to attribute spend.
3. Pick a provider (`openai`, `anthropic`, or a custom string — custom
   providers become their own route / cluster).
4. Pick the upstream's `BaseUrl` (without trailing slash — the host
   adds it).
5. Pick the env var that holds the upstream API key. The host reads
   that env var at startup; the var is resolved by the auth-time
   transformer for every request.
6. Optional: set `BudgetUsd` (a positive decimal), `ExpiresAt` (UTC),
   or `AllowedModels` (string array).
7. Add the new `Proxy__VirtualKeys__N__*` block to `.env` and restart
   the host. Verify with `curl -i /v1/models -H 'authorization: Bearer
   <new-token>'` — expect `200`.

## Removing a virtual key

Delete the `Proxy__VirtualKeys__N__*` block from `.env` and restart.
The proxy refuses the token immediately on next request (the catalogue
is rebuilt on startup; no `Authorization`-side caching).

If the deleted key was used by a worker SDK or the brain host, those
consumers fall back to the upstream error path — either they retry
through a different key, or the call fails. There is no per-key
"soft-disable" today.

## Versioning

The proxy follows the host's release cadence. There is no separate
version table for virtual keys. Tokens are rotated by editing
`Proxy:VirtualKeys:N:Token` in `.env` and restarting; the old token
stops working immediately on restart, the new one starts.

## Future work (out of scope for v1)

- **Body-based usage metering.** The YARP `TransformResponseAsync`
  contract forbids body reads; today's proxy records no
  `usage_events` row on a successful upstream call. The follow-up
  uses a buffered-response middleware or a streaming-aware reader
  to extract OpenAI / Anthropic usage JSON.
- **Postgres-backed virtual keys.** Today virtual keys live in
  `appsettings.json` / env. A future slice moves them to a
  `proxy_virtual_keys` table so operators can mint / revoke keys
  without a host restart.
- **Per-key rate limiting.** Today the proxy enforces only the
  monthly budget. A future slice adds a per-minute request cap.
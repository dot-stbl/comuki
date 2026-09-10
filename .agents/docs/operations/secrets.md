# Secrets resolution

Comuki references secrets as **ref-names** (the env-var name, file path,
or KV v2 path#field) — values are never in the DB or in `appsettings`.
A reference is resolved at call time by a pluggable
[`ISecretProvider`][secrets-folder] — env, file, or a remote KV (Vault /
Consul, future slices).

This doc covers operator setup for Vault (slice 2). For the env + file
defaults set in slice 1 see the inline `<summary>` blocks in
[`platform/src/shared/Comuki.Shared.Kernel/Secrets/`][secrets-folder].

## Reference format

```
GH_TOKEN                     # bare env name → env scheme (backward compatible)
env:GH_TOKEN                 # explicit env scheme
file:/etc/comuki/db-pass     # file scheme (path on disk)
vault:prod/db#password       # K/V v2: <path-under-KvMount>#<field> (mount comes from KvMount, not the ref)
consul:comuki/prod/db-pass   # K/V (slice 3, not yet wired)
```

Unknown scheme → `SecretRefFormatException` at write-time validation
(fail-fast); the ref never reaches the resolver. Bare names default to
`env` so every pre-existing `SecretEnvRef` row keeps working.

## Vault provider (slice 2)

### Prerequisites

- A reachable HashiCorp Vault server (1.10+; tested against `vault:1.15`).
- K/V v2 secrets engine enabled at the mount the provider reads from
  (the dev-mode `vault server -dev` auto-mounts `secret/` as K/V v2 —
  sufficient for local dev; production clusters may use a different
  mount).
- A bootstrap **token** with read access to the K/V v2 paths the host
  will resolve. The token's policy must allow
  `read` on `secret/data/<path>` (or whichever mount is configured).
  The token is referenced by **env-var name**, not stored in
  `appsettings` — `configuration-toml-env.md` §4 forbids committed
  secrets.

### Configuration

```toml
[Secrets.Vault]
Enabled = true
Address = "https://vault.svc.cluster.local:8200"
TokenEnvRef = "COMUKI_VAULT_TOKEN"   # env-var NAME, not value
KvMount = "secret"
CacheTtl = "00:01:00"                # in-process TTL (60s default)
```

`appsettings.json` equivalent:

```json
{
  "Secrets": {
    "Vault": {
      "Enabled": true,
      "Address": "https://vault.svc.cluster.local:8200",
      "TokenEnvRef": "COMUKI_VAULT_TOKEN",
      "KvMount": "secret",
      "CacheTtl": "00:01:00"
    }
  }
}
```

| Field        | Required        | Default                  | Notes |
|--------------|-----------------|--------------------------|-------|
| `Enabled`    | yes             | `false`                  | Master switch; when off the provider short-circuits to `null`. |
| `Address`    | when `Enabled`  | empty                    | `http(s)://host:port` of the Vault server. Validated by `VaultSecretOptionsValidator` only when `Enabled = true` (so a disabled deployment can ship with an empty address). |
| `TokenEnvRef`| when `Enabled`  | `COMUKI_VAULT_TOKEN`     | Env-var **name** holding the bootstrap token. The production-secret gate validates the env var with this name (`ProductionSecretValidator.ValidateSecretsProviders`) — overriding it gates on the custom name. |
| `KvMount`    | when `Enabled`  | `secret`                 | K/V v2 mount point. |
| `CacheTtl`   | no              | `00:01:00`               | In-process TTL (remote providers default to 60s per issue #52 §Design). Set to `00:00:00` to disable the cache (every resolve hits Vault). |

### Production gate

`ProductionSecretValidator.ValidateSecretsProviders` (issue #10 T11.4)
fails the boot in `Production` when `[Secrets:Vault]:Enabled = true` but
the env var named in `TokenEnvRef` is unset on the host. The gate reads
the **configured** `TokenEnvRef` — the same env var the client factory
consumes — so a deployment overriding it (e.g. `MY_VAULT_TOKEN`) is
gated on that variable, not on the default:

> refusing to start the host in Production: the [Secrets:Vault]
> provider is enabled but its bootstrap token env var
> `'COMUKI_VAULT_TOKEN'` is unset; set the `COMUKI_VAULT_TOKEN` env var
> to a real token

In Development the production gate is off; the
`VaultSecretClientFactory` still throws `SecretRefUnsetException` if the
env var is missing, but only when the `Enabled` section is active.

### Token rotation (v1)

Slice 2 reads the bootstrap token **once at startup** and bakes it into
the `VaultSharp.VaultClient`. Token rotation requires a host
restart — there is no in-process refresh in v1. If your deployment
needs minute-scale rotation, run the host behind a sidecar that
mutates the env var and SIGHUPs the process (the provider rebuilds on
restart via the standard `IHostApplicationLifetime` shutdown hooks).

The 60-second `CacheTtl` covers the rotated-value path: after the host
restarts with a fresh token, in-flight Vault resolves within the
previous process continue to return cached values for up to 60s.
New processes get the fresh value on first resolve.

### Wire-format example

Suppose Vault holds:

```bash
vault kv put secret/prod/db password=s3cr3t
```

Then in `appsettings.json`:

```json
"SourceConnection:ApiKeyEnvRef": "vault:prod/db#password"
```

…and the production validator, when `Enabled = true`, requires the env
var named in `TokenEnvRef` (default `COMUKI_VAULT_TOKEN`) to be set in
the environment. The `VaultSecretProvider` reads the path `prod/db`
under the configured `KvMount` from the K/V v2 engine and returns
`s3cr3t`.

The composite resolver turns a `null` provider result into
`SecretRefUnsetException` at resolve time. On the HTTP surface
`ProviderExceptionHandler` currently has **no dedicated arm** for that
exception, so it falls through to the catch-all 500 `internal.error`
response — adding an explicit 502 `secret_ref_unset` mapping is a known
follow-up (the intake endpoint maps it to a 400 with an intake-specific
code). Until that mapping lands, watch the host logs for
`SecretRefUnsetException` when debugging an unset reference; the 500
`internal.error` you see on the wire is the symptom, not the diagnosis.

### Telemetry

The provider emits the `comuki.secrets.vault.resolved` counter tagged
with `outcome` (`hit` / `miss` / `disabled`). Cache-hit ratio is the
primary signal for cache tuning: a hot path that always hits means the
60s TTL is enough; a path that always misses means either the TTL is
too short or Vault is being hot-resolved (consider whether a hot
config in DB should mirror the secret instead of resolving on every
read).

The provider's ActivitySource is `Comuki.Shared.Kernel`; span name is
`comuki.secrets.vault.resolve` with tags `vault.address`,
`vault.mount`, `vault.path`, `vault.key` (bounded cardinality — no
secret values, no PIDs).

### Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `refusing to start the host in Production: ... COMUKI_VAULT_TOKEN is unset` | bootstrap env var not propagated to the deployment | Set `COMUKI_VAULT_TOKEN` in the deployment spec (Deployment env / ConfigMap / Vault Agent template). |
| 403 from Vault on first resolve | Token policy does not allow read on `secret/data/<path>` | Update the Vault policy (`vault policy write`) to grant `read` on the relevant path. |
| `SecretRefUnsetException` on every `vault:` ref | `Enabled = false` or `[Secrets:Vault]` section not bound | Set `Enabled = true` in `[Secrets:Vault]`, or use a different scheme for that ref. |
| Stale values after rotation | TTL cache or restart dependency | Lower `CacheTtl` for hot refs, restart the host for a clean rotation. |
| OpenAPI build capture fails on `VaultSecretOptions.Address` validation | The OpenAPI capture runs without `[Secrets:Vault]` configuration (it builds the host in stub mode) | The validator already short-circuits when `Enabled = false`. If you need `[Secrets:Vault]:Enabled = true` in the OpenAPI capture environment, ensure `Address` is set there too. |

[secrets-folder]: ../../../../platform/src/shared/Comuki.Shared.Kernel/Secrets/

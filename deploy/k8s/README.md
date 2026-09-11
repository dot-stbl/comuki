# Comuki — raw Kubernetes manifests (no Helm)

Self-host Comuki on any Kubernetes >= 1.28 with plain `kubectl apply`.
For a parameterised install prefer the Helm chart at [`../helm/`](../helm/).

## Prerequisites

- Images: `ghcr.io/dot-stbl/comuki` (host + migrator + brain),
  `ghcr.io/dot-stbl/comuki-worker`, `ghcr.io/dot-stbl/comuki-dashboard`.
  Build them yourself from a checkout:
  ```bash
  docker build -f deploy/compose/docker/host.Dockerfile -t ghcr.io/dot-stbl/comuki:latest .
  docker build -f deploy/compose/docker/worker.Dockerfile -t ghcr.io/dot-stbl/comuki-worker:latest .
  docker build -f deploy/compose/docker/dashboard.Dockerfile \
    --build-arg VITE_API_BASE_URL=http://comuki.example.com \
    -t ghcr.io/dot-stbl/comuki-dashboard:latest .
  ```
  The dashboard bakes its API base URL at build time — build it with the
  public URL of this deployment.
- A default StorageClass with ReadWriteOnce volumes.

## Apply order

```bash
kubectl apply -f namespace.yaml

# 1. Secrets — copy the template, fill the ${PLACEHOLDER}s, apply the COPY
cp secret.example.yaml secret.yaml
$EDITOR secret.yaml                       # openssl rand -hex 32 for peppers
kubectl apply -f secret.yaml
rm secret.yaml                            # keep secrets out of the tree

# 2. Storage + config + RBAC
kubectl apply -f postgres.yaml
kubectl apply -f minio.yaml
kubectl apply -f configmap.yaml           # EDIT COMUKI_PUBLIC_HOST_URL first!
kubectl apply -f worker-role.yaml

# 3. Migrations — wait for completion
kubectl apply -f migrator-job.yaml
kubectl wait --for=condition=complete job/comuki-migrator -n comuki --timeout=300s

# 4. Host + dashboard
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml
kubectl apply -f dashboard.yaml
```

## Access

```bash
kubectl -n comuki port-forward svc/comuki-dashboard 8080:80
open http://localhost:8080
```

Log in with the bootstrap admin email from `deployment.yaml`
(`admin@example.com` — edit before applying) and the password you put in
the Secret.

Put a real Ingress in front of `svc/comuki-dashboard` (SPA) with
`svc/comuki` (API) for `/api`, `/realtime`, `/openapi` — the Helm chart
ships a ready-made Ingress template with the right annotations
(`proxy-body-size 150m`, long read timeouts for SignalR).

## Upgrades

```bash
kubectl delete job comuki-migrator -n comuki --ignore-not-found
kubectl apply -f migrator-job.yaml
kubectl wait --for=condition=complete job/comuki-migrator -n comuki --timeout=300s
kubectl rollout restart deployment/comuki-host -n comuki
```

Always run the migrator before restarting the host — the host expects
the schema at the image's migration level.

## Notes

- The host Deployment is pinned to `replicas: 1` — the worker-token
  store and SignalR connection map are in-process state.
- Workers run as batch/v1 Jobs created by the host (Role
  `comuki-worker-spawn`); nothing to apply per worker.
- Bring your own Postgres/MinIO: delete `postgres.yaml` / `minio.yaml`
  and edit `configmap.yaml` (`COMUKI_ARTIFACTS_ENDPOINT`) + the `COMUKI_DB`
  connection strings in `migrator-job.yaml` / `deployment.yaml`.
- Direct TLS on the host (no Ingress): see
  [`host-tls.example.yaml`](host-tls.example.yaml) — a TLS secret
  mounted at `/certs` plus the `COMUKI_HOST_TLS_*` env; HTTPS listens
  on container `:8081` next to HTTP `:8080` with plain-HTTP redirect.

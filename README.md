[![Comuki — leading model directs a swarm of ephemeral workers](assets/banner.png)](https://github.com/dot-stbl/comuki)

[![ci](https://github.com/dot-stbl/comuki/actions/workflows/ci.yml/badge.svg)](https://github.com/dot-stbl/comuki/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![React 19](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react&logoColor=black)](https://react.dev)
[![status](https://img.shields.io/badge/status-v1%20·%20self--hostable-slate?style=flat-square)](#status--limits)

**A leading model decomposes tickets into a plan and directs a swarm of ephemeral workers — each a container on your infrastructure — with deterministic verification and a human closing the loop.** Self-hosted: your Postgres, your model keys, your network. The bar for v1.0: any other product can be built from scratch on Comuki.

```text
→ brain proposes · control plane decides
→ workers are ephemeral · state lives outside them
→ verify deterministically · then ask a model
→ skills + analyzers + design system · not vibes
→ human closes the loop · no auto-self-mutation
```

> [!TIP]
> **Start here** — one Docker command, ~15 minutes, dashboard on
> `http://localhost:17173`:
>
> ```bash
> git clone https://github.com/dot-stbl/comuki.git
> cd comuki/deploy/compose
> cp .env.example .env
> docker compose up -d --build
> ```
>
> Kubernetes instead? Two more paths below in [Quick Start](#quick-start).

## The control loop

Schematic of the runtime path — this is what runs today, end to end:

```text
# schematic — the runtime path (live in v1)

  ticket     GitHub · GitLab · Jira · Yandex Tracker · chat
    │
    ▼
  brain      leading model decomposes the goal → plan + per-task briefs
    │
    ▼
  workers    ephemeral containers claim tasks (FOR UPDATE SKIP LOCKED),
    │        stream StageReports back over gRPC, upload artifacts, exit
    ▼
  verify     deterministic checks first — build · tests · analyzers —
    │        model review second, never instead
    ▼
  you        approve or cancel; the append-only journal keeps the replay
```

- **Ephemeral by design** — a container lives for one claim, then dies. Fresh context per task; no long-session drift, no state to lose.
- **State outside the worker** — the DAG, the journal, knowledge, budgets: all in Postgres + MinIO, none in a container.
- **Cheap models hold the line** — because the gate is deterministic, the worker model can be cheap; the brief and the verifier carry the quality.

<details>
<summary><strong>Under the hood — stack and layout</strong></summary>

Polyglot monorepo, top level by stack:

| Path | Stack | Role |
|------|-------|------|
| `platform/` | C# / .NET 10 | Orchestrator host, 11 modules (Identity, Projects, Chat, Memory, Intake, Costs, Artifacts, Proxy, Knowledge, Verify, Scheduler), compute engine, YARP proxy, Translator |
| `agents/` | TypeScript (bun) | `comuki-agent-core` · `comuki-worker-sdk` (pi runtime) · `comuki-dev-sdk` (Claude Code) |
| `dashboard/` | React 19 + Vite + shadcn | Operational UI — runs, inbox, projects, live journal over SignalR |
| `control-plane/` | markdown / configs | Swarm rules and skills (not product code) |
| `deploy/` | Compose · Helm · raw manifests | postgres+pgvector, MinIO, migrator, Grafana dashboards |
| `tests/` | C# | Unit · integration · architecture · k6 load |
| `.agents/` | markdown | Rules, design docs, STATE — the agent contour of the repo itself |

Storage and seams:

- **Postgres** — 10 schemas (one per module context) + pgvector for knowledge/memory.
- **MinIO (S3)** — run bundles: `{projectId}/{runId}/{brief,result,pins}.json`.
- **gRPC** (Translator ↔ orchestrator) · **MCP JSON-RPC** at `/api/v1/mcp` (tools: `search_knowledge`, `list_runs`) · **SignalR** `/realtime/runs` for the dashboard.
- **Model proxy** (optional) — YARP OpenAI/Anthropic passthrough with HMAC virtual keys: model, budget, expiry per key. Workers stay provider-agnostic.
- **Observability** — OpenTelemetry → VictoriaMetrics/VictoriaLogs; Grafana dashboards as code (runs, workers, costs).

Three images on `ghcr.io/dot-stbl`: `comuki` (host/migrator/brain entrypoints), `comuki-worker` (Translator + agent runtime), `comuki-dashboard`.

Comuki does **not** write its own product code — it is the tool that builds *other* projects. In this repo: platform, SDKs, and operational UI only.

</details>

## Why Comuki

- **You own the infrastructure** — workers run as your Docker containers or your Kubernetes Jobs. Nothing routes through someone else's cloud.
- **Anti-slop is structural** — workers get skills (recipes), hard locks (no editing tests, no installs, no push to main), and deterministic analyzers. Quality is enforced by the gate, not hoped from the model.
- **Costs are a first-class citizen** — per-project budgets, per-virtual-key metering, usage events in Postgres. The swarm stops when the budget does.
- **Replayable by construction** — every step is an event in an append-only journal. Debug and audit by replay, not by archaeology.

## Quick Start

**Requirements:** Docker (compose path) or Kubernetes ≥ 1.28 with Helm (helm path). Nothing else — images are prebuilt.

| Path | Use it when | Guide |
|---|---|---|
| Docker Compose | single machine, laptop server, homelab | [`deploy/compose/`](deploy/compose/) |
| Helm chart | any Kubernetes ≥ 1.28 | [`deploy/helm/`](deploy/helm/) |
| Raw manifests | Kubernetes without Helm | [`deploy/k8s/`](deploy/k8s/) |

**Docker Compose** (the TIP block above, from scratch):

```bash
cd deploy/compose
cp .env.example .env           # defaults boot a localhost stack
docker compose up -d --build   # first build: dotnet + bun, grab coffee
# dashboard → http://localhost:17173
```

Log in with `COMUKI_BOOTSTRAP_ADMIN_EMAIL` / `COMUKI_BOOTSTRAP_ADMIN_PASSWORD` from your `.env` (defaults `admin@example.com` / `comuki_dev` — change them).

**Helm:**

```bash
helm install comuki ./deploy/helm \
  --set secrets.postgresPassword="$(openssl rand -hex 16)" \
  --set secrets.apiKeyPepper="$(openssl rand -hex 32)" \
  --set secrets.tokenPepper="$(openssl rand -hex 32)" \
  --set secrets.bootstrapAdminPassword='ChangeMe-2026!' \
  --set secrets.artifactsAccessKey=comuki-minio \
  --set secrets.artifactsSecretKey="$(openssl rand -hex 16)" \
  --set publicUrl=http://comuki.localhost

kubectl -n default port-forward svc/comuki-dashboard 8080:80
```

**kubectl (raw manifests):**

```bash
cd deploy/k8s
kubectl apply -f namespace.yaml
cp secret.example.yaml secret.yaml && $EDITOR secret.yaml   # fill ${PLACEHOLDER}s
kubectl apply -f secret.yaml && rm secret.yaml
kubectl apply -f postgres.yaml -f minio.yaml -f configmap.yaml -f worker-role.yaml
kubectl apply -f migrator-job.yaml
kubectl wait --for=condition=complete job/comuki-migrator -n comuki --timeout=300s
kubectl apply -f deployment.yaml -f service.yaml -f dashboard.yaml
```

Env vars, worker compute providers, troubleshooting, and a hardening checklist: [deploy/oss/README.md](deploy/oss/README.md) — the full self-hosting guide.

## Docs

| Doc | What it answers |
|-----|-----------------|
| [Self-hosting guide](deploy/oss/README.md) | Deploy, configure, troubleshoot, harden |
| [Architecture](.agents/docs/architecture/comuki-architecture.md) | Principles, services, control loop |
| [Decisions](.agents/docs/architecture/comuki-decisions.md) | Why those choices (with alternatives) |
| [Project structure](.agents/docs/architecture/comuki-project-structure.md) | Repo layout and C# layers |
| [Architecture index](.agents/docs/architecture/README.md) | Map of the whole design set |
| [STATE](.agents/STATE.md) · [ROADMAP](.agents/ROADMAP.md) | Where we are / what is next |
| [AGENTS.md](AGENTS.md) | Entry point for agent harnesses working in this repo |

## Comparisons

| Alternative | Their strength | Comuki difference |
|---|---|---|
| Devin | Polished UX, zero setup, fast iteration | You own the infrastructure; workers are your containers, your model keys, your network — nothing leaves it |
| GitHub Copilot Workspace | Deep GitHub integration | Not locked to GitHub: intake from GitLab, Jira, Yandex Tracker, chat; self-hosted |
| AutoGPT / CrewAI | Easy start, flexible agents | Deterministic verification gate; workers are stateless and never self-mutate; state is Postgres, not a session |
| Temporal / Airflow | Battle-tested orchestration, retries | The plan is what the model produces, not YAML you write; retries and leases come for free underneath |

## Status & limits

**v1 is complete and self-hostable.** 24 slices shipped, 50/50 issues closed, 1,567 backend + 1,560 frontend tests green in CI.

| Shipped in v1 | Deferred to v2 |
|---|---|
| Runs · queue · claim-lease · journal · reaper | Generic-command runner isolation (`Process.Start` → container) |
| Compute: Docker + Kubernetes (batch/v1 Jobs) providers | Fleet runner host-agent for bare metal |
| Translator · worker image · gRPC end to end | Autonomy ratchet: confidence scoring, daily decay |
| Identity: users · API keys · RBAC · OIDC | Merge-queue multi-feature batching |
| Chat with brain · approve/cancel | |
| Intake: GH · GL · Jira · Tracker · PR-review · sync-back | |
| Proxy: virtual keys · budgets · metering | |
| Knowledge: pgvector · MCP endpoint · docs worker | |
| Artifacts: MinIO run bundles · scheduler · verify | |

Honest limits today:

- The **standalone brain host** is experimental; the brain runs in-process by default.
- **Dashboard**: runs, identity, projects, inbox, OIDC are wired to the real API; the remaining domains are mock-first (in real mode they show empty states and throw loud errors — no phantom success).
- **TS SDKs** (`comuki-agent-core`, `comuki-worker-sdk`, `comuki-dev-sdk`) live in this repo's bun workspace — not published to npm yet.
- Workers need **model access**: per-image env keys or the built-in proxy with virtual keys.
- Design docs under `.agents/docs/` are mostly **Russian**; the code, API, and this README are English.

## Contributing & development

Read [AGENTS.md](AGENTS.md) first — orientation for humans and agent harnesses alike (build gate, commit format, stack boundaries). Prefer small, reviewable changes against the current [ROADMAP](.agents/ROADMAP.md).

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [bun](https://bun.sh) 1.x. Docker for integration tests and local infra.

```bash
# Backend — warnings-as-errors + format gate are part of the build
dotnet build comuki.slnx -c Debug

# Backend tests — xUnit v3 on Microsoft Testing Platform: run, don't `dotnet test`
dotnet run --project tests/Comuki.Platform.Orchestration.Unit.Lease

# Frontend
cd dashboard && bun install
bun run typecheck && bun run lint && bun run test && bun run build

# Agent SDKs
cd agents && bun install && bun run typecheck && bun test
```

Issues and discussion: [github.com/dot-stbl/comuki](https://github.com/dot-stbl/comuki).

## License

[MIT](LICENSE) — free to use, modify, and deploy. © 2026 .stbl

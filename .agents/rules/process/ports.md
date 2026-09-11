---
description: comuki port pool 17000-17200 — fixed assignments, no random vite/dotnet ports
priority: high
always: true
---

# Port pool — Comuki

Все локальные сервисы Comuki сидят **только** в диапазоне **17000–17200**
(включительно). Случайные порты Vite/Kestrel/Storybook **запрещены**.

## Закреплено

| Port  | Service                         | How |
|-------|---------------------------------|-----|
| 17173 | **dashboard** (Vite)            | `dashboard/vite.config.ts` → `server.port`, `strictPort: true` |
| 17001 | Api.Public (Kestrel)            | reserved — wire in `launchSettings` / env when host runs |
| 17002 | Proxy (YARP)                    | reserved |
| 17003 | MCP / Knowledge                 | reserved |
| 17004 | Brain gRPC (Kestrel)            | `platform/src/host/Comuki.Host.Brain` → brain.grpcPort (config.toml) |
| 17010 | Storybook                       | reserved — `storybook -p 17010` when needed |
| 17020 | VictoriaMetrics                 | reserved — align `deploy/docker-compose.yml` |
| 17021 | VictoriaLogs                    | reserved |
| 17022 | MinIO API                       | reserved |
| 17023 | MinIO console                   | reserved |
| 17024 | Postgres                        | reserved (host-mapped) |
| 17025 | Nexus                           | reserved |
| 17026 | Keycloak (compose profile `keycloak`) | `deploy/docker-compose.yml` → keycloak service |
| 17027 | Grafana (compose profile `grafana`) | `deploy/docker-compose.yml` → grafana service |
| 17172 | host HTTP (compose host-mapping) | `deploy/compose/docker-compose.yml` → host service published port |
| 17171 | host HTTPS (compose host-mapping, optional TLS) | `deploy/compose/docker-compose.yml` → `COMUKI_TLS_HOST_PORT` |

Свободный диапазон для ad-hoc / экспериментов: **17180–17200**.
Новый постоянный сервис — **добавь строку в эту таблицу** в том же PR,
не занимай порт молча.

## Rules

1. Dev-сервер dashboard = **http://localhost:17173** — не 5173.
2. `strictPort: true` — если порт занят, падаем, не прыгаем на +1.
3. В доках, скриптах, `.env.example`, compose — только порты из таблицы.
4. Агент **не** стартует long-lived serve (см. `agent-runtime-safety`);
   пользователь запускает сам на закреплённом порту.

## Related

- `AGENTS.md` § Critical Non-Obvious Patterns / Команды
- `build-verification.md` — gates, не serve

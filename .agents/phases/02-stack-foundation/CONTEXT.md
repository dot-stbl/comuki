# Phase 2 — Stack Foundation

## Цель

Зафиксировать конкретный стек для `agents/` (TS), `dashboard/` (React),
`deploy/` (Docker) — то, что пользователь отложил под "потом определятся со
стеком". На выходе — реальные манифесты в каждой подпапке + CI, который
собирает все три стека на каждом коммите.

## Что входит

Заполняется в `soly plan 2`. Ожидаемый scope:

- `agents/` — TS workspace (bun/npm?), минимум `comuki-agent-core` (общее ядро:
  типы событий, MCP-клиент, чтение декларативных правил, формат брифа/отчёта).
- `dashboard/` — React + Vite + shadcn, минимальный landing "Comuki", дизайн-токены
  по `comuki-dashboard-designspec.md` (тёмная тема по умолчанию).
- `deploy/docker-compose.yml` — self-hosted инфра: postgres (с pgvector), minio,
  nexus, victoria (metrics + logs).
- `deploy/worker.Dockerfile` — заглушка (pi + Translator-скелет), без реального
  запуска — это Phase 3.
- CI workflow: сборка C# (`dotnet build`), TS (`tsc` + Vite build), docker-compose
  lint. По правилам `.agents/rules/process/build-verification.md` — extended analyzer
  rules на BE.

## Что НЕ входит

- Реальный Translator / pi интеграция — Phase 3.
- Реальные фичи дашборда — Phase 7.
- Реальные docker-образы для оркестратора/прокси — позже.

## Зависит от

Phase 1.

## Definition of Done

Заполняется в `soly plan 2`.

## Hard constraints

- **Один package manager** на TS-часть (зафиксировать в `packageManager`-поле).
- **Vite, не Next.js** — дашборд не продуктовый, SSR не нужен.
- **shadcn/ui, не MUI / Chakra** — дизайн-система по `comuki-dashboard-designspec.md`
  явно на shadcn.
- **Стиль кода** — `code-style.md` (TS strict, no `any`, named exports).
- **Скрипты** — bun/node, **не Python** (`.agents/rules/process/allowed-scripts.md`).

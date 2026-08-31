---
milestone: v1
status: s0-skeleton
last_updated: 2026-08-31
progress:
  total_slices: 15
  completed_slices: 0.5
  issues: https://github.com/dot-stbl/comuki/issues
  percent: 3
---

# Project State

## Current Position

**Ресет бекенда на новый каркас** (2026-08-31): `platform/src` = `shared` ·
`engine` · `host` (см. `docs/architecture/comuki-project-structure.md`).
Старая структура (`application/feature/database/models`) удалена; история и
рабочий код фазы 4 (Translator runner, gRPC contract, PiCli integration) —
на ветке `legacy/pre-new-structure`, вернутся по мере слайсов.

Планирование: `docs/product/comuki-task-breakdown.md` (v2.1) ↔ GitHub issues
S0–S14 + backlog (#1–#16). Текущий слайс: **S0 — скелет** (issue #1), частично
закрыт этим ресетом.

## Что есть (живое)

- `Comuki.Shared.Kernel` — id-VO (RunId/ProjectId/WorkerId, UUIDv7)
- `Comuki.Shared.Contracts` — `IComputeProvider` порт + labels + request/handles
- `Comuki.Engine.Orchestration` — RunStatus / WorkItemStatus enums (без stalled)
- `Comuki.Host` — /health (composition-only entry)
- `Comuki.Host.Translator` — **stream-json парсер pi + 9 unit-тестов** (перенос из фазы 4)
- Тесты: arch 4/4 (правила слоёв нового дерева) · translator 9/9
- Build: `dotnet build comuki.slnx -c Debug` → 0 warnings / 0 errors
- `nuget.config` — clear sources, только nuget.org (NU1507 закрыт)
- CI: GitHub Actions нет ещё — issue #16 (S14); `.gitlab-ci.yml` удалён

## Goal (v1.0)

На Comuki можно с нуля собрать любой другой продукт. Полный scope:
`docs/architecture/comuki-v1-scope-draft.md`; экраны:
`docs/product/comuki-fe-requirements.md`.

## Key decisions (актуальные)

| Decision | Rationale |
|----------|-----------|
| Каркас shared/modules/engine/host по образцу console.x.sdk | модульный монолит; hosts = composition only |
| Entity id = UUIDv7 (PG uuid) | time-ordered, native PG, API — string |
| API key `ck_` + HMAC store; worker token opaque+TTL | показать 1 раз; отзыв с lease |
| Роли только в коде (6 ролей), assignments в БД; permissions = константы + каталог + startup validate | RBAC как console.x ADR-0018, упрощён |
| Journal = `run_events` jsonb; тяжёлое → MinIO по uri | один SoT timeline; blob дёшево |
| Lease = колонки `work_items` (SKIP LOCKED) | один hot path, без join |
| Run 7 статусов; work item без `stalled` (stall → событие + failed/requeue) | меньше состояний, политика снаружи |
| gRPC: Host = server, Translator = client; k8s Start = batch/v1 Job | контейнер открывает канал наружу |
| Chat/Voluta в Comuki.Host; Brain = отдельный Host.Brain (gRPC) | изоляция agent-loop |
| Intake: `/api/hooks/{source}` + двойная идемпотентность (delivery_id + unique active run) | replay-safe, 1 issue = 1 run |
| Wire к моделям: OpenAI + Anthropic compatible; hapy не зависимость | OSS |
| FE-тесты: vitest+MSW; Playwright нет | решение scope |
| Verify → v1.1 (opt-in generic-command) | не ломает ядро |
| Конventional Commits `[hybrid] type(scope): subject` | см. rules/process/commit-format.md |
| xUnit v3 = MTP: `dotnet run --project` | VSTest не умеет v3 discovery |

## Осторожно (известные грабли)

- `dotnet format --severity hidden` вписывает `throw new NotImplementedException()`
  в switch-руки (IDE0010 fixer) → MA0025 errors. После format — проверять diff.
- NetArchTest `HaveDependencyOn("Comuki.Host")` префикс-матчит
  `Comuki.Host.Translator` — проверять реальные границы, не префиксы ns.
- `dotnet sln add --solution-folder` на Windows/.NET 10 схлопывает пути —
  править `.slnx` руками и сверять с физическими папками.

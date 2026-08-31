---
milestone: v1
status: wave-3-merged
last_updated: 2026-08-31
progress:
  total_slices: 15
  completed_slices: 9
  issues_closed: "1,2,3,15,16"
  issues_open: "4 (S3 in flight), 12 (tail), 14 (T12.4), 5-11"
  percent: 55
---

# Project State

## Current Position

Wave 3 слита (`a4296b5`): Host-auth + Projects/settings. **В работе: S3-runtime**
(gRPC server, Translator runner, worker image, e2e c TestFakePi — воркер майнит
`legacy/pre-new-structure`).

Issue-трекер = source of truth статуса: https://github.com/dot-stbl/comuki/issues
(закрыты S0/S1/S2/S13/S14; S4 — модуль+host готов, хвост: scope-filter 404 +
keycloak e2e).

## Что живёт (после a4296b5)

- **Каркас**: `platform/src/{shared,modules,engine,host}` + `platform/build` (format gate)
- **Модули**: Identity (RBAC: roles-in-code, ck_ keys, OIDC linker) · Projects (CRUD + settings live-reload)
- **Engine**: Orchestration (runs/queue/claim-lease SKIP LOCKED/journal/reaper) · Compute (Docker provider, tokens, scale v0)
- **Host**: /health · /api/v1/auth/* (cookie+ck_+OIDC start/callback) · /profiles /chat-commands (permissioned) · /api/v1/projects + settings · HostComposer (internal, IVT для тестов)
- **agents/**: bun workspace, agent-core (zod, парсер-зеркало), worker-sdk (locks, skills)
- **Тесты**: unit 278 · integration 52 (реальный PG) · TS 65 · CI GitHub Actions зелёный

## Гейты (Definition of Done)

1. `dotnet build comuki.slnx -c Debug` — 0/0 **+ `[VerifyFormatOnBuild] Format check passed`** (жёсткий формат-гейт в графе билда)
2. Все suite'ы зелёные (`dotnet run --project <test>` — MTP, не dotnet test)
3. FE (когда тронут): `cd dashboard && bun run typecheck && bun run lint && bun run test`
4. Agents TS: `cd agents && bun install && bun run typecheck && bun test`

## Ключевые решения (дельта от старых docs)

| Решение | Что |
|---|---|
| Анализаторы | ТОЛЬКО IDE code-style + CA Security; **MA/RCS/VSTHRD удалены** (шум) |
| Формат-гейт | `platform/build/Comuki.Build.Tools` — verify hidden, эскейпы `-p:DisableFormatOnBuild` / `FormatOnBuildTreatAsWarning`; формат-фиксер ≠ доверие: diff всегда ревьюить |
| IDE0010/IDE0072 off | вписывают NotImplementedException в switch |
| IDE0058 off | иначе фиксер вставляет `_ =` (запрещены глобальным правилом) |
| IDE0022 off (expr-bodied) | методы — только block body |
| Program.cs | top-level, **без** `public partial class Program` — тесты через `internal HostComposer.Compose` + IVT |
| Entity ids | UUIDv7 (PG uuid), строки в API |
| Ключи | `ck_` prefix + HMAC(pepper env); worker token opaque+TTL |
| EF | миграции per-module context (`__comuki_*` history), snake_case, tool-generated only |
| Тестcontainers | 4.14.0 (4.12 тянет уязвимый SSH.NET) |
| slnx | править руками (`dotnet sln add --solution-folder` ломает пути на Win) |

## Осторожно (грабли, уже стреляли)

- `dotnet format --severity hidden` — REVIEW DIFF после фикса (см. STATE истории: NotImplementedException-arms)
- Локальная ветка `master` удалена (указывала на старый dashboard) — integration = `preparation/translator-001`, push как `HEAD:master`
- NetArchTest prefix-match: `Comuki.Host.Translator` матчится на `Comuki.Host` — проверять реальные границы
- Merge-конфликты волны: props (дубли PackageVersion → NU1506), slnx (объединять руками), Host.csproj

## Дальше

S3 финиш → волна 4: S5 Chat/Brain (Host.Chat + Voluta + Host.Brain) · S6 Intake · T12.4 dev-sdk · Identity-хвост (#12) · keycloak compose.

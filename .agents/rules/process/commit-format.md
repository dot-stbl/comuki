---
description: Comuki repo uses [.stbl] prefix on Conventional Commits 1.0.0
priority: high
always: true
---

# Commit format — Comuki

В репозитории `comuki.orchestrator` коммиты используют префикс `[.stbl]`.
Тип и scope — по **Conventional Commits 1.0.0**. Применяется во всю ширину
— включая соло-разработчика (нет смысла в `WIP`/`wip`/`tmp`).

## Формат

Текущая форма (canonical, принята в `commit-lint.mjs`):

```
[.stbl](<feat-area>): <description>
```

`<feat-area>` — kebab-case путь, может быть вложенным (`feat/dashboard`, `fe/mocks`,
`meta`, `host/mtls`, `tests/architecture`). Всегда начинается с префикса `feat/`
(или `meta/`, `docs/` — см. «Top-level areas» ниже); выдумывать свои корневые
сегменты нельзя.

Legacy-форма (`<type>(<scope>)`) — также принимается линтером для backward
compatibility, но для новых коммитов используйте текущую форму.

```
[.stbl] <type>(<scope>): <description>
```

Опционально — тело и footer через пустую строку.

## Top-level areas

Вместо `type` в новой форме — `<feat-area>` (всегда `feat/...`, `meta/...`,
или `docs/...`):

| Area                 | Когда                                                                |
|----------------------|----------------------------------------------------------------------|
| `feat/<module>`      | код в `platform/src/modules/Comuki.Modules.X/`                       |
| `feat/<shared>`      | код в `platform/src/shared/Comuki.Shared.X/`                          |
| `feat/<provider>`    | код в `platform/src/providers/Comuki.Providers.X/`                   |
| `feat/host`          | `platform/src/host/Comuki.Host/` (composition root)                   |
| `feat/build-tools`   | `platform/src/host/Comuki.Build.Tools/`                              |
| `feat/fe`            | `dashboard/` (sub-area: `feat/fe/<sub-area>`)                         |
| `feat/tests`         | `tests/` (unit / integration)                                        |
| `meta`               | build, CI, deps, scripts, repo-level config, **rules themselves**      |
| `docs`               | documentation-only (`.agents/docs/`, ADRs, README)                    |

Вложенность: `feat/fe/dashboard`, `feat/tests/architecture`, `meta/format`,
`meta/deps`, `host/mtls`.

Legacy-форма использует `type` (`feat`/`fix`/`refactor`/`docs`/`test`/`perf`/
`build`/`ci`/`chore`/`style`/`revert`/`merge`) — Conventional Commits 1.0.0.

## Scope (опционально, рекомендуется)

Область, на которую влияет изменение. В этом репо логичные scope:

- **платформа (C#):** `bootstrap`, `orchestration`, `proxy`, `mcp`, `translator`,
  `database`, `routing`, `rules`, `artifacts`, `knowledge`
- **агенты (TS):** `agents`, `agent-core`, `worker-sdk`, `dev-sdk`
- **фронт:** `dashboard`
- **инфра:** `deploy`, `docker`
- **процесс:** `docs`, `rules`, `roadmap`, `state`, `ci`, `deps`

Не выдумывать новые scope без причины — повторное использование того же scope
помогает `git log --grep` и review tooling.

## Subject (description)

- **Императив** — "add", "fix", "bump", "wire" — не "added", "fixed", "bumped".
- **Без точки** в конце (Conventional Commits convention).
- **Длина:** **≤72 символа — рекомендация**, **>100 — гейт валит** коммит.
  Две разные цифры намеренно: 72 это про читаемость `git log --oneline`,
  100 это запас, за которым subject перестаёт быть subject.
- **Lowercase** для type/scope (Conventional Commits convention). На регистр
  description правило **не смотрит** и хук тоже: описание сплошь и рядом
  начинается с идентификатора или акронима — `SubjectScopeMiddleware wraps …`,
  `CVE-2026-49451 bump`, `OSS deployment artifacts`. Требование к description —
  императив, а не строчная буква.
- **Префикс `[.stbl]`** — обязателен, с пробелом перед type.
- **`!` перед `:`** — опциональный маркер breaking change:
  `[.stbl] feat(api)!: change /tasks response shape`.

## Body (опционально)

Через пустую строку после subject. Wrap ~72 символа. Объясняет **почему**, а
не **что** — diff показывает что.

## Footer (опционально)

Для breaking changes, ссылок на тикеты, и т.д.

```
[.stbl] feat(api): change /tasks response shape

BREAKING CHANGE: /tasks now returns { items, total } instead of array.
Migration: clients must read .items.

Refs: COM-142
```

## Good

```
[.stbl](feat/orchestration): add claim/lease loop for pull-queue
[.stbl](fix/database): correct cascade delete on runs table
[.stbl](docs/roadmap): clarify Slice 0 DoD with idempotency check
[.stbl](meta/deps): bump dotnet to 10.0.108
[.stbl](meta/rules): adopt [.stbl] prefix for comuki commits
[.stbl](refactor/translator): extract stream-json parser into separate file
[.stbl](test/orchestration): cover two-claimer race for FOR UPDATE SKIP LOCKED
[.stbl](meta/ci): enforce extended analyzer rules in build-verification
[.stbl](feat/fe/dashboard): wire cost page breakdowns and forecast
```

Legacy (still accepted by linter):

```
[.stbl] feat(orchestration): add claim/lease loop for pull-queue
[.stbl] fix(database): correct cascade delete on runs table
```

## Bad

```
feat(orchestration): add foo                 ← нет [.stbl] префикса
feat: add foo                                ← нет [.stbl] префикса
[stbl](feat): add foo                        ← старый префикс, запрещён
[.stbl](feat/Orchestration): add foo        ← path должен быть lowercase
[.stbl](feat): add foo                      ← пустой path (нет /area)
[.stbl] feat() add foo                      ← legacy: пустой scope
feat: Added new endpoint.                    ← прошедшее время + точка
WIP                                         ← без type/area
feat add foo                                 ← нет `:` после type
update stuff                                 ← не описательно
```

## Enforcement

Формат больше не «на честном слове» — его проверяет
[`scripts/commit-lint.mjs`](../../../scripts/commit-lint.mjs) через хук
`commit-msg` (см. [`pre-commit.md`](pre-commit.md) — установка и обход).

**Merge commits — exempt.** `Merge branch …` (от git) и ручной
`merge(<scope>): …` (legacy, без `[.stbl]` prefix) проходят мимо правила.
Current form `[.stbl] merge(<scope>): …` линтится обычным порядком
(`merge` есть в `COMMIT_TYPES`).

| Что | Поведение хука |
|-----|----------------|
| Кривой subject | **Hard fail** — коммит не проходит, в stderr subject, список проблем и шпаргалка по формату |
| AI-байлайн (`Co-Authored-By: Claude`, `🤖 Generated with …`) | **Вырезается на месте** с предупреждением, коммит проходит. См. [`no-ai-attribution.md`](no-ai-attribution.md) |
| `Merge …` / `Revert …` / `fixup!` / `squash!` | Пропускаются — эти subject пишет сам git |
| Строки `#` и diff ниже scissors при `--verbose` | Игнорируются |

Проверить, ничего не коммитя:

```bash
echo '[.stbl] feat(api): add the thing' | node scripts/commit-lint.mjs --stdin
node scripts/commit-lint.mjs --range master..HEAD   # ручной аудит диапазона
node --test scripts/commit-lint.test.mjs            # тесты самого линтера
```

Обойти на один коммит — `git commit --no-verify`. CI-джобы на это нет
намеренно: гейт локальный.

`--range` по старой истории будет шуметь — правило применяется **forward**
(см. ниже), а до хука subject никто не проверял. Гонять `--range` имеет смысл
по своей ветке (`master..HEAD`), не по всему `git log`.

## Overrides

Раньше глобальный `~/.claude/rules/git.md` требовал формат
`[stbl](<type>): <description>`. Этот файл удалён (см. `git log` для истории),
и теперь `.agents/rules/process/commit-format.md` — единственный source of truth
для формата коммитов в comuki.orchestrator.

Применяется **forward** — коммиты до этого правила не переписываются.
Если видишь в `git log` коммиты без `[.stbl]` префикса — это до принятия
текущего правила. Не правь историю ради единообразия.

## Commit body когда есть что сказать

Не пихай в subject "what" (видно в diff). Subject = **"why" в одной фразе**.
Body — контекст, риск, trade-off.

```
[.stbl] fix(orchestration): make claim transaction atomic with lease insert

Раньше claim читал task, потом отдельным UPDATE ставил lease —
между ними другой worker мог взять ту же задачу. Склеили в одну
транзакцию с FOR UPDATE SKIP LOCKED, как в stack.md §04.

Verified: 100-claimer race in tests/, lease loss = 0.
```

## Related

- [`no-ai-attribution.md`](no-ai-attribution.md) — байлайны модели в коммитах, PR, коде и доках
- [`pre-commit.md`](pre-commit.md) — установка git-хуков и обход
- `scripts/commit-lint.mjs` — реализация проверки, единственный source of truth
- `scripts/hooks/commit-msg` — хук, который её вызывает

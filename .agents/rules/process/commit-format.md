---
description: Comuki repo uses [hybrid] prefix on Conventional Commits 1.0.0
priority: high
always: true
---

# Commit format — Comuki

В репозитории `comuki.orchestrator` коммиты используют префикс `[hybrid]`.
Тип и scope — по **Conventional Commits 1.0.0**. Применяется во всю ширину
— включая соло-разработчика (нет смысла в `WIP`/`wip`/`tmp`).

## Формат

```
[hybrid] <type>: <description>
```

или с scope:

```
[hybrid] <type>(<scope>): <description>
```

Опционально — тело и footer через пустую строку.

## Типы

| Type       | Когда                                                                       |
|------------|------------------------------------------------------------------------------|
| `feat`     | новая фича / функциональность                                                |
| `fix`      | bug fix                                                                      |
| `refactor` | рефакторинг без изменения наблюдаемого поведения                             |
| `docs`     | только документация (md, дизайн-система, docs/, README)                     |
| `test`     | добавление или изменение тестов                                              |
| `perf`     | улучшение производительности                                                 |
| `build`    | build-система или external dependencies (Directory.Packages.props, .NET SDK) |
| `ci`       | CI конфигурация (workflows, build-verification)                              |
| `chore`    | тулинг, мета-вещи, форматирование, мелочи, не код и не фича                 |
| `style`    | форматирование без изменения смысла (whitespace, prettier, biome)            |

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
- **≤72 символа** (рекомендация).
- **Lowercase** для type/scope (Conventional Commits convention).
- **Префикс `[hybrid]`** — обязателен, с пробелом перед type.

## Body (опционально)

Через пустую строку после subject. Wrap ~72 символа. Объясняет **почему**, а
не **что** — diff показывает что.

## Footer (опционально)

Для breaking changes, ссылок на тикеты, и т.д.

```
[hybrid] feat(api): change /tasks response shape

BREAKING CHANGE: /tasks now returns { items, total } instead of array.
Migration: clients must read .items.

Refs: COM-142
```

## Good

```
[hybrid] feat(orchestration): add claim/lease loop for pull-queue
[hybrid] fix(database): correct cascade delete on runs table
[hybrid] docs(roadmap): clarify Slice 0 DoD with idempotency check
[hybrid] chore(deps): bump dotnet to 10.0.108
[hybrid] chore(rules): adopt [hybrid] prefix for comuki commits
[hybrid] refactor(translator): extract stream-json parser into separate file
[hybrid] test(orchestration): cover two-claimer race for FOR UPDATE SKIP LOCKED
[hybrid] ci(be): enforce extended analyzer rules in build-verification
```

## Bad

```
feat(orchestration): add foo                 ← нет [hybrid] префикса
feat: add foo                                ← нет [hybrid] префикса
[stbl](feat): add foo                        ← старый префикс, запрещён
[hybrid](feat): add foo                      ← вариант с parens вокруг type, не наш формат
feat: Added new endpoint.                    ← прошедшее время + точка
WIP                                         ← без type
feat add foo                                 ← нет `:` после type
update stuff                                 ← не описательно
```

## Overrides

Раньше глобальный `~/.claude/rules/git.md` требовал формат
`[stbl](<type>): <description>`. Этот файл удалён (см. `git log` для истории),
и теперь `.agents/rules/process/commit-format.md` — единственный source of truth
для формата коммитов в comuki.orchestrator.

Применяется **forward** — коммиты до этого правила не переписываются.
Если видишь в `git log` коммиты без `[hybrid]` префикса — это до принятия
текущего правила. Не правь историю ради единообразия.

## Commit body когда есть что сказать

Не пихай в subject "what" (видно в diff). Subject = **"why" в одной фразе**.
Body — контекст, риск, trade-off.

```
[hybrid] fix(orchestration): make claim transaction atomic with lease insert

Раньше claim читал task, потом отдельным UPDATE ставил lease —
между ними другой worker мог взять ту же задачу. Склеили в одну
транзакцию с FOR UPDATE SKIP LOCKED, как в stack.md §04.

Verified: 100-claimer race in tests/, lease loss = 0.
```

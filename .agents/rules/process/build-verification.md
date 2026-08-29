---
description: Когда и как прогонять build+verify на backend (.NET) и frontend (React/TS), какие ошибки недопустимы перед коммитом
globs: ["**/*.cs", "**/*.csproj", "**/*.sln", "**/*.slnx", "**/*.ts", "**/*.tsx", "**/package.json", "**/tsconfig*.json", "**/vite.config.*"]
priority: high
interactive: false
always: true
---

# Build & Verification

Это правило описывает, **когда** и **как** запускать сборку backend (.NET) и
frontend (React/TypeScript), какие состояния считаются failure, и что делать
с pre-existing ошибками в незатронутых файлах.

Сокращённая версия есть в `AGENTS.md` § Critical Non-Obvious Patterns.
Здесь — полная.

## Agent contract — Definition of Done

Перед тем как сказать «готово» — **обязательно** прогнать полный набор. Без сокращений:

```bash
# 1. Full format check (whitespace + style + analyzer fixes)
dotnet format comuki.slnx --verify-no-changes --severity warn

# 2. Build with extended analyzer rules
dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true --nologo

# 3. Frontend — если затронут (см. раздел "Команды / Frontend")
cd dashboard && bun run build
```

**Правило:** exit ≠ 0 от **любой** команды = задача **не готова**. Чинить и повторить.

Pre-commit hook (`scripts/hooks/pre-commit`) ловит только whitespace — он **не** покрывает style-rules и analyzer-fixes, поэтому полный прогон нужен перед тем, как сказать «готово», а не только перед коммитом.

**Исключение (только при горячем hotfix):** если коммит блокирует прод, а полный verify занимает слишком долго — прогнать хотя бы `dotnet build` без `-p:EnforceExtendedAnalyzerRules` и починить style в следующем коммите. **Не норма**, оправдано только явной срочностью. Обязательно отметить в commit message (см. `commit-format.md`).

## Когда применять

Применяется ко **всем** нетривиальным правкам в `src/`:

| Затронутая сторона | Паттерн файлов | Что проверять |
|--------------------|----------------|---------------|
| Только BE | `**/*.cs`, `**/*.csproj`, `**/*.sln*` | BE build |
| Только FE | `**/*.ts`, `**/*.tsx`, `**/package.json`, `**/tsconfig*.json`, `**/vite.config.*` | FE build |
| Обе стороны | mix of above | **Оба** билда (см. Dual-Build Rule) |
| OpenAPI / контроллеры API | `**/*.cs` с `[ApiController]` / `**/openapi.v1.json` | BE + регенерация FE API client |

**Тривиальные правки** (опечатки в markdown, переименование файла) — полный
прогон не требуется, но перед коммитом всё равно проверить сторону,
к которой относится изменение.

**Не применяется** к:
- Правкам только в `docs/`, `plans/`, `.planning/`
- Правкам только в `src/client-side/src/api/` (auto-generated через Kubb)
- Правкам только в `src/feature/*/Generated/` (Mapperly / source-gen)
- Правкам только в `**/*.md`

## Команды

### Backend (обязательно с extended analyzer rules)

```bash
# 1. Format + style + naming check (IDE0007, IDE1006 и т.п. — то, что
#    НЕ ловит dotnet build, ловит dotnet format --verify-no-changes).
#    Exit ≠ 0 = задача не готова.
dotnet format comuki.slnx --verify-no-changes --severity warn

# 2. Build с extended analyzer rules (CA*, RCS*, MA*, кастомный CMK*).
#    Exit ≠ 0 = задача не готова.
dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true --nologo
```

**Требования к выходу:**
- `dotnet format`: Exit code = `0`. Любой non-zero (даже warning) = задача не готова.
- `dotnet build`: Exit code = `0`, **0 warnings, 0 errors**.
- Время выполнения обоих шагов — ожидаемо 30–120 секунд после warm-up.

**Что ловит `dotnet format`, но НЕ ловит `dotnet build`** (даже при
`EnforceCodeStyleInBuild=true` + `TreatWarningsAsErrors=true`):
- **Naming rules** (IDE1006) — camelCase private fields, `I`-prefix на interfaces,
  `Async`-суффикс, и т.д. Настраиваются через `dotnet_naming_*` в `.editorconfig`.
- **Style rules** с severity ниже `error` (`silent`, `suggestion`).

Без `dotnet format --verify-no-changes` эти правила не enforce-ятся, и IDE1006
проходит мимо `dotnet build`.

**Глобально suppressed** (см. `directory.build.props`):
- `RMG012` (Mapperly)
- `CS1573` (param comment в partial-методах)

Все остальные warnings — **починить до коммита**. Не отключать анализаторы,
не править `.editorconfig` ради одного warning.

### Frontend

```bash
cd src/client-side && bun run build
```

`bun run build` запускает `vite build && tsc` — проверяет **и** сборку Vite,
**и** TypeScript-компиляцию в одном прогоне.

**Требования к выходу:**
- Exit code = `0`
- **0 TypeScript errors**
- Vite warnings (chunk size > 500kb, dynamic import hints) — допустимы,
  если не блокируют сборку

**Дополнительно (по ситуации):**

| Что | Команда |
|-----|---------|
| Линт FE (biome) | `cd src/client-side && bun run check` |
| Тесты FE (vitest) | `cd src/client-side && bun run test` |
| Regen API client | `cd src/client-side && bun run generate-api` |

Biome check **рекомендуется** перед коммитом, но **не блокирует** коммит,
если запуск делает невозможным прогон TypeScript-сборки (например, в
изолированном sandbox без biome).

## Dual-Build Rule

Если задача / план / коммит **затрагивает обе стороны** (новый endpoint на
BE + новая страница на FE, изменение контракта API, смена схемы БД,
отражающейся в FE) — прогнать **обе** сборки:

```bash
# 1. Backend — сначала format (IDE1006 и прочие naming/style), потом build
dotnet format comuki.slnx --verify-no-changes --severity warn
dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true --nologo

# 2. Frontend
cd src/client-side && bun run build

# 3. Опционально: регенерация API client, если менялись контроллеры
cd src/client-side && bun run generate-api
cd src/client-side && bun run build   # пересобрать после regen
```

**Обе** должны дать exit 0. Недопустимо коммитить с формулировкой
"FE компилируется, BE потом починю" или наоборот.

## Pre-existing errors

Если при сборке вылезли ошибки в **незатронутых** файлах (то есть они были
до правки), это **не** повод отложить "до лучших времён":

| Ситуация | Что делать |
|----------|------------|
| Warning в **затронутом** файле | ✅ Починить в рамках текущей задачи |
| Error в **затронутом** файле | ✅ Починить, иначе коммит невозможен |
| Warning в **незатронутом** файле | ✅ Починить, если блокирует `0 warnings` target |
| Error в **незатронутом** файле | ✅ Починить в рамках текущей задачи |
| Техдолг, зафиксированный в `.planning/BACKEND-ISSUES.md` или `NEXT-STEPS.md` | ⚠️ Можно отложить, **отметить в commit message** |

❌ **Запрещено:**
- `// @ts-ignore` / `// eslint-disable-next-line` без обоснования в коде
- `dotnet_diagnostic.* = none` в `.editorconfig` для подавления warning
- `#pragma warning disable` без `restore` и без комментария "почему"
- Глобальное отключение правила в biome.json / eslint.config

## Когда НЕ запускать

- Правка только markdown-файлов в `docs/`, `plans/`, `.planning/`
- Правка auto-generated `src/client-side/src/api/**` (регенерируется Kubb)
- Правка `src/feature/*/Generated/**` (Mapperly, source generators)
- Правка только `*.json` в корне (`.gitignore`, `.editorconfig` уже учтены)
- Правка бинарных ресурсов (`.png`, `.svg`, шрифты)

## Перед коммитом — чеклист

```
1. git status / git diff --stat
   ↓
2. По списку файлов определить затронутые стороны (BE / FE / обе)
   ↓
3. Если BE  → dotnet format + dotnet build (см. выше)
   Если FE  → cd src/client-side && bun run build
   Если обе → ОБА
   ↓
4. Все exit 0?  ── Нет → починить, goto 3
                  ↓ Да
5. git add + commit
```

## Good / Bad

```bash
# ✅ Correct — затронут только BE, проверены и format, и build
$ git diff --stat
 src/application/api/Foo/Bar.cs | 12 ++++++----

$ dotnet format comuki.slnx --verify-no-changes --severity warn
$ dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true --nologo
 ... Build succeeded. 0 Warning(s) 0 Error(s)

$ git add src/application/api/Foo/Bar.cs
$ git commit -m "feat(api): add Bar endpoint"
```

```bash
# ❌ Wrong — затронуты обе стороны, проверен только BE
$ git diff --stat
 src/application/api/Foo/Bar.cs        | 12 ++++++----
 src/client-side/src/routes/foo.tsx    |  5 ++++-

$ dotnet build anlytra.sln ...          # OK
$ git add .
$ git commit -m "..."                   # FE не проверен — TS-ошибка уйдёт в CI
```

```bash
# ❌ Wrong — проигнорированы warnings
$ dotnet build anlytra.sln ...
 ... Build succeeded. 2 Warning(s) 0 Error(s)

$ git commit -m "..."                   # warning = code smell, чиним ДО коммита
```

```bash
# ❌ Wrong — подавление через .editorconfig
# .editorconfig
dotnet_diagnostic.CA1822.severity = none   # ← запрещено
```

## Связанные правила и файлы

- `AGENTS.md` § Critical Non-Obvious Patterns (краткая версия)
- `.claude/rules/CODING-RULES.md` — code style
- `.claude/rules/TESTING-RULES.md` — запуск тестов
- `.claude/rules/RULES-FORMAT.md` — формат и иерархия правил
- `directory.build.props` — глобальные suppressed warnings
- `.planning/BACKEND-ISSUES.md`, `NEXT-STEPS.md` — зафиксированный техдолг

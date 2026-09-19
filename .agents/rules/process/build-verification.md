---
description: когда и как прогонять build+verify на backend (.NET) и frontend (dashboard), что реально блокирует, что делать с pre-existing ошибками
globs: ["**/*.cs", "**/*.csproj", "**/*.sln", "**/*.slnx", "dashboard/**/*.ts", "dashboard/**/*.tsx", "dashboard/**/package.json", "dashboard/**/tsconfig*.json", "dashboard/vite.config.*", "dashboard/vitest.config.*", "dashboard/eslint.config.*"]
priority: high
interactive: false
always: true
---

# Build & Verification

Это правило описывает, **когда** и **как** запускать сборку backend (.NET) и
frontend (`dashboard/`, React + TypeScript), какие состояния считаются failure,
и что делать с pre-existing ошибками в незатронутых файлах.

Сокращённая версия есть в `AGENTS.md` § Critical Non-Obvious Patterns.
Здесь — полная.

> **Источник истины по командам — `dashboard/package.json` и
> [`.github/workflows/ci.yml`](../../../.github/workflows/ci.yml).** Если этот
> файл разошёлся с ними — правы они, а этот файл чинится.

## Что реально блокирует (CI-гейт)

CI (`ci.yml`) — единственный автоматический гейт. Он гоняет ровно это:

| Job | Что запускает |
|-----|---------------|
| `build backend` | `dotnet restore comuki.slnx` → `dotnet build comuki.slnx -c Debug --no-restore` |
| `test backend (<project>)` | `dotnet run --project tests/Comuki.Architecture.Tests -c Debug` и `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.StreamJson -c Debug` |
| `test backend (integration)` | `dotnet run --project <project> -c Debug` по каждой папке `tests/integration/*/`, кроме `Comuki.Host.Testing` (общая харнесс-библиотека без entry point) |
| `build frontend (dashboard)` | из `dashboard/`: `bun install` → `bun run typecheck` → `bun run lint` → `bun run test` |
| `build agents (ts sdks)` | из `agents/`: `bun install` → `bun run build` (экспорт типов SDK) |

**CLI (`cli/`)** — отдельный TypeScript-пакет (Bun + Ink), не входящий в
CI-гейт на момент написания (см. issue #108). Agent contract для работы в
`cli/`: из `cli/` — `bun install` → `bun run typecheck` → `bun run test` →
`bun run build`. Известный baseline: `bun run test` падает на
`cli/spikes/opentui/tests/*` (5 fail + 5 errors) из-за отсутствующих
spike-депов в корневом workspace — это pre-existing и не связано с
продуктовым кодом `cli/src/`.

Всё из этой таблицы **блокирует**. Ни один шаг здесь не «рекомендуется»:
`bun run lint` (eslint) падает — PR не идёт, ровно как и `dotnet build`.

Чего в CI **нет**: `dotnet format`, `bun run build`, e2e (`playwright`),
`test:coverage`, Storybook. Это не значит «можно не проверять» — это значит,
что за них отвечает агент перед тем, как сказать «готово» (см. ниже).

## Agent contract — Definition of Done

Перед тем как сказать «готово» — **обязательно** прогнать полный набор. Без сокращений:

```bash
# 1. Backend build = gate. Warnings-as-errors, анализаторы и code-style уже
#    включены в Directory.Build.props, дополнительных флагов не нужно.
dotnet build comuki.slnx -c Debug

# 2. Формат — ловит whitespace и порядок using, на которых build не падает
dotnet format comuki.slnx --verify-no-changes --severity warn

# 3. Frontend — если затронут dashboard/
cd dashboard && bun run typecheck && bun run lint && bun run test
cd dashboard && bun run build
```

**Правило:** exit ≠ 0 от **любой** команды = задача **не готова**. Чинить и повторить.

Pre-commit hook (`scripts/hooks/pre-commit`) ловит только whitespace в staged
`.cs` — он **не** покрывает ни анализаторы, ни фронтенд, поэтому полный прогон
нужен перед тем, как сказать «готово», а не только перед коммитом. Второй хук,
`commit-msg`, проверяет формат subject и вырезает байлайны модели
(см. `commit-format.md`, `no-ai-attribution.md`).

**Исключение (только при горячем hotfix):** если коммит блокирует прод, а полный
verify занимает слишком долго — прогнать хотя бы `dotnet build` и починить
остальное следующим коммитом. **Не норма**, оправдано только явной срочностью.
Обязательно отметить в commit message (см. `commit-format.md`).

## Когда применять

Применяется ко **всем** нетривиальным правкам в коде:

| Затронутая сторона | Паттерн файлов | Что проверять |
|--------------------|----------------|---------------|
| Только BE | `platform/**/*.cs`, `**/*.csproj`, `**/*.slnx` | BE build |
| Только FE | `dashboard/src/**`, `dashboard/*.config.*`, `dashboard/package.json`, `dashboard/tsconfig*.json` | FE гейт + build |
| Обе стороны | mix of above | **Оба** (см. Dual-Build Rule) |
| OpenAPI / контроллеры API | `**/*.cs` с `[ApiController]` | BE + регенерация FE API client (`bun run generate-api`) |

**Тривиальные правки** (опечатки в markdown, переименование файла) — полный
прогон не требуется, но перед коммитом всё равно проверить сторону,
к которой относится изменение.

**Не применяется** к:
- Правкам только в `.agents/docs/`, `.agents/phases/`, `openspec/`
- Правкам только в `dashboard/src/shared/api/_generated/**` (генерируется Kubb)
- Правкам только в `**/*.md`

## Команды

### Backend

```bash
# 1. Build = gate. Directory.Build.props уже включает TreatWarningsAsErrors,
#    EnforceCodeStyleInBuild, AnalysisLevel=latest, AnalysisModeSecurity=All.
#    Дополнительные -p:… флаги ничего не добавляют и не нужны.
#    Exit ≠ 0 = задача не готова.
dotnet build comuki.slnx -c Debug

# 2. Формат. Exit ≠ 0 = задача не готова.
dotnet format comuki.slnx --verify-no-changes --severity warn
```

**Требования к выходу:**
- `dotnet build`: Exit code = `0`, **0 warnings, 0 errors**.
- `dotnet format`: Exit code = `0`.
- Время выполнения обоих шагов — ожидаемо 30–120 секунд после warm-up.

**Какой набор анализаторов включён** (см. `.editorconfig` и `ANALYZERS.md`):
- **IDE** (code-style) — через `EnforceCodeStyleInBuild`. Правила именования
  (`dotnet_naming_*`: camelCase для private-полей, `I`-префикс на интерфейсах)
  объявлены с severity `error` — они валят build, а не просто светятся в IDE.
- **CA** — `AnalysisMode=None`, включена только категория Security
  (`AnalysisModeSecurity=All`) плюс явные opt-in в `.editorconfig`.
- **VSTHRD** — хирургический набор: 200 / 002 / 104 = `error`, 103 = `none`.
- Meziantou (MA) и Roslynator (RCS) **удалены** 2026-08-31 как шум — не
  возвращать «заодно».

**Что ловит `dotnet format`, но не валит `dotnet build`:** whitespace, порядок
using и прочее форматирование, не выраженное диагностикой. Правила стиля с
severity ниже `warning` (`suggestion`, `silent`) тоже проходят мимо build —
их ловит только format.

**Глобально suppressed** (`Directory.Build.props`):
- `CS1591` (missing XML doc comment). `GenerateDocumentationFile` включён,
  потому что OpenAPI-документ читает `///`-саммари; доки добавляются по мере
  готовности области.

Все остальные warnings — **починить до коммита**. Не отключать анализаторы,
не править `.editorconfig` ради одного warning.

### Тесты (xUnit v3 = Microsoft Testing Platform)

```bash
dotnet run --project tests/unit/<Project> -c Debug
```

`dotnet test` **не работает** — VSTest не умеет discovery для xUnit v3.
Подробности — [`../coding/TESTING-RULES.md`](../coding/TESTING-RULES.md).

### Frontend (`dashboard/`)

```bash
cd dashboard
bun run typecheck   # tsc -b
bun run lint        # eslint .
bun run test        # vitest run
bun run build       # tsc -b && vite build
```

Линтер — **eslint** (`dashboard/eslint.config.js`), форматтер — **prettier**
(`bun run format`). Biome в этом репозитории нет и никогда не было;
скрипта `bun run check` не существует.

**Требования к выходу:**
- Exit code = `0` у всех четырёх.
- **0 TypeScript errors**, **0 eslint errors**.
- Vite warnings (chunk size, dynamic import hints) — допустимы,
  если не блокируют сборку.

**Дополнительно (по ситуации):**

| Что | Команда |
|-----|---------|
| Покрытие FE (пол 70% по строкам) | `cd dashboard && bun run test:coverage` |
| Regen API client (Kubb) | `cd dashboard && bun run generate-api` |
| Аудит корпуса правил | `cd dashboard && bun run audit:fe` (warning-only) |

`bun run prebuild` автоматически дёргает `audit:fe` перед `build`.

**Не запускать из агентской сессии:** `bun run dev`, `vite`, `storybook`,
`playwright` (`bun run test:e2e`), `dotnet watch` — long-lived процессы убивают
рантайм харнесса (`AGENTS.md` §6).

## Dual-Build Rule

Если задача / план / коммит **затрагивает обе стороны** (новый endpoint на
BE + новая страница на FE, изменение контракта API, смена схемы БД,
отражающейся в FE) — прогнать **обе** стороны:

```bash
# 1. Backend
dotnet build comuki.slnx -c Debug
dotnet format comuki.slnx --verify-no-changes --severity warn

# 2. Frontend
cd dashboard && bun run typecheck && bun run lint && bun run test && bun run build

# 3. Если менялись контроллеры — регенерация API client и пересборка
cd dashboard && bun run generate-api
cd dashboard && bun run build
```

**Обе** должны дать exit 0. Недопустимо коммитить с формулировкой
«FE компилируется, BE потом починю» или наоборот.

## Pre-existing errors

Если при сборке вылезли ошибки в **незатронутых** файлах (то есть они были
до правки), это **не** повод отложить «до лучших времён»:

| Ситуация | Что делать |
|----------|------------|
| Warning в **затронутом** файле | ✅ Починить в рамках текущей задачи |
| Error в **затронутом** файле | ✅ Починить, иначе коммит невозможен |
| Warning в **незатронутом** файле | ✅ Починить, если блокирует `0 warnings` target |
| Error в **незатронутом** файле | ✅ Починить в рамках текущей задачи |
| Техдолг, зафиксированный в `.agents/STATE.md` | ⚠️ Можно отложить, **отметить в commit message** |

❌ **Запрещено:**
- `// @ts-ignore` / `// eslint-disable-next-line` без обоснования в коде
- `dotnet_diagnostic.* = none` в `.editorconfig` для подавления warning
- `#pragma warning disable` без `restore` и без комментария «почему»
- Глобальное отключение правила в `dashboard/eslint.config.js`

## Когда НЕ запускать

- Правка только markdown-файлов в `.agents/`, `openspec/`
- Правка auto-generated `dashboard/src/shared/api/_generated/**` (регенерируется Kubb)
- Правка только `*.json` в корне (`.gitignore`, `.editorconfig` уже учтены)
- Правка бинарных ресурсов (`.png`, `.svg`, шрифты)

## Перед коммитом — чеклист

```
1. git status / git diff --stat
   ↓
2. По списку файлов определить затронутые стороны (BE / FE / обе)
   ↓
3. Если BE  → dotnet build + dotnet format (см. выше)
   Если FE  → cd dashboard && bun run typecheck && bun run lint
              && bun run test && bun run build
   Если обе → ОБА
   ↓
4. Все exit 0?  ── Нет → починить, goto 3
                  ↓ Да
5. git add + commit
```

## Good / Bad

```bash
# ✅ Correct — затронут только BE, проверены и build, и format
$ git diff --stat
 platform/src/host/Comuki.Host/Endpoints/Runs.cs | 12 ++++++----

$ dotnet build comuki.slnx -c Debug
 ... Build succeeded. 0 Warning(s) 0 Error(s)
$ dotnet format comuki.slnx --verify-no-changes --severity warn

$ git add platform/src/host/Comuki.Host/Endpoints/Runs.cs
$ git commit
```

```bash
# ❌ Wrong — затронуты обе стороны, проверен только BE
$ git diff --stat
 platform/src/host/Comuki.Host/Endpoints/Runs.cs | 12 ++++++----
 dashboard/src/routes/runs/index.tsx             |  5 ++++-

$ dotnet build comuki.slnx -c Debug      # OK
$ git add .
$ git commit                             # FE не проверен — eslint/TS упадёт в CI
```

```bash
# ❌ Wrong — проигнорированы warnings
$ dotnet build comuki.slnx -c Debug
 ... Build succeeded. 2 Warning(s) 0 Error(s)

$ git commit                             # warning = code smell, чиним ДО коммита
```

```bash
# ❌ Wrong — подавление через .editorconfig
# .editorconfig
dotnet_diagnostic.CA1822.severity = none   # ← запрещено
```

## Связанные правила и файлы

- `AGENTS.md` § Critical Non-Obvious Patterns (краткая версия)
- [`.github/workflows/ci.yml`](../../../.github/workflows/ci.yml) — что реально блокирует
- [`../coding/CODING-RULES.md`](../coding/CODING-RULES.md) — code style
- [`../coding/ANALYZERS.md`](../coding/ANALYZERS.md) — какие анализаторы подключены
- [`../coding/TESTING-RULES.md`](../coding/TESTING-RULES.md) — запуск тестов
- [`../coding/frontend-construct-rules.md`](../coding/frontend-construct-rules.md) — FE-гейт и no-slop
- [`../coding/RULES-FORMAT.md`](../coding/RULES-FORMAT.md) — формат и иерархия правил
- [`pre-commit.md`](pre-commit.md) — что делают хуки
- `Directory.Build.props` — глобальные suppressed warnings и флаги анализаторов

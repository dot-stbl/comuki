# ADR-0003 — CLI ownership: two `comuki` products, one command surface

- **Статус:** Accepted
- **Дата:** 2026-09-19
- **Контекст:** issue #82 — два разных продукта оба названы `comuki`:
  TS-пользовательский TUI (`cli/`, npm-пакет `@dot-stbl/comuki`,
  бинарь `comuki`) и .NET operator host (`platform/src/host/Comuki.Host/`,
  бинарь `comuki`). Команды и флаги CLI пересекаются с разной
  семантикой; TS-wire типы в `cli/src/lib/client.ts` и
  `cli/src/lib/signalr.ts` зеркалят C# модели вручную; часть slash
  команд упирается в отсутствующие host-API.
- **Issue:** https://github.com/dot-stbl/comuki/issues/82
- **Related ADR:**
  [adr-0002-cli-opentui.md](./adr-0002-cli-opentui.md) — TUI-стек CLI
  (принят, Core path); этот ADR поверх — про ownership
  executable/package + command surface, не про renderer.

## Контекст

В монорепо `comuki.orchestrator` два бинаря делят имя `comuki`:

1. **TS user CLI** (`cli/`, npm `@dot-stbl/comuki`):
   - `cli/package.json:2` — `"name": "@dot-stbl/comuki"`,
     `"bin": { "comuki": "./bin/comuki.ts" }`;
   - стек: Ink 5 + React 18 + `@microsoft/signalr@^8.0.7` +
     `yargs@^17.7.2` (`cli/package.json:24-33`);
   - аудитория: **пользователь оркестратора** — автор задач,
     читатель transcript'ов, оператор sessions/runs;
   - стартовые deps: `~/.config/comuki/config.json` (`cli/src/lib/config.ts`),
     host URL + API key или session cookie, локальный sessions.json
     в `~/.config/comuki/sessions.json` и archive в
     `~/.config/comuki/archive/`;
   - default = REPL (`cli/src/lib/commands.ts:42-44` — `args[0]` пустой →
     `repl`); argv-subcommands: `status runs login whoami config setup
     completion doctor archive` (`cli/src/lib/commands.ts:20-30`,
     `cli/src/index.tsx:83-125`); в-REПЛ slash-commands:
     `cli/src/lib/slash.ts:21-214` (31 команда).

2. **.NET operator host CLI** (`platform/src/host/Comuki.Host/Cli/`,
   shared в `platform/src/shared/Comuki.Shared.Bootstrap/Cli/`):
   - `platform/src/host/Comuki.Host/Program.cs:20-28` — argv
     диспатчится в `ComukiHostCli.IsDoctorRequested` →
     `ComukiDoctor.RunAsync` ИЛИ `ComukiHostCli.TryRun` →
     `ComukiHostCliRouter.Route`;
   - стек: .NET 10, ASP.NET Core minimal hosting, без third-party CLI
     парсера (`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:55-63`
     — switch по первому аргументу);
   - аудитория: **оператор кластера / SRE** — первый запуск,
     pre-flight check, миграции, конфиг;
   - стартовые deps: ни одного — все команды работают на bare
     machine (`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:8-15` —
     "no config.toml, no database, no logging pipeline");
   - argv-subcommands: `version`, `doctor`, `config show`, `init`
     (`platform/src/shared/Comuki.Shared.Bootstrap/Cli/ComukiCli.cs:15`,
     `platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:57-60`).
     Plus `comuki-migrator` отдельный exe —
     `platform/src/host/Comuki.Migrator/Program.cs:13-18` —
     `version`, `status`, default = apply (`platform/src/host/Comuki.Migrator/Program.cs:39-71`).

Эти **две** поверхности пересекаются по двум командам с **разной**
семантикой и по одному имени `comuki` без namespace.

### Конфликты имён (где семантика расходится)

| Verb | TS CLI (`cli/src/lib/commands.ts`) | .NET operator (`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:57-60`) |
|---|---|---|
| `doctor` | `cli/src/commands/doctor.ts:122` — `printDoctor`: проверяет `url` (HTTP probe `/api/v1/health`), `auth` (`GET /api/v1/auth/me`), `config` (config.json exists), `theme` (theme choice valid). Локально-локально: нет process-state, нет env, нет базы. Exit 1 на любом fail. | `platform/src/host/Comuki.Host/Cli/Doctor/ComukiDoctor.cs:35` — `RunAsync`: проверяет `config` (`config.toml` discovery chain), `environment` (`COMUKI_ENV`), `database` (Npgsql `SELECT 1`), `secrets:*` (`ProductionSecretAudit.Collect`). Подсказывает `comuki-migrator status` для миграций. Exit 1 на любом fail. |
| `setup` | `cli/src/commands/setup.tsx:248` — Ink wizard: URL → health probe → auth (login / api-key / skip) → project (fetch + pick) → theme. Пишет `~/.config/comuki/config.json`. | отсутствует |
| `init` | отсутствует | `platform/src/host/Comuki.Host/Cli/Init/ComukiInit.cs:78` — `Run`: пишет `./config.toml` + `./.env` (HMAC peppers, COMUKI_ENV, COMUKI_DB placeholder). Flags: `--env <name>`, `--force`. Exit 0 written, 1 files skipped, 2 usage. |
| `config show` | `cli/src/commands/config.ts` — резолв `~/.config/comuki/config.json` с masked credentials (`maskApiKey`/`maskCookie`, `cli/src/commands/config.ts:67-76`). Преценденс: arg > env > file. | `platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:34-46` — `RunConfigShow`: `ComukiConfigView.Render` (`platform/src/shared/Comuki.Shared.Bootstrap/Config/ComukiConfigView.cs`) поверх `config.toml + COMUKI_* env` (`UseComukiConfiguration`). |
| `status` | `cli/src/commands/status.tsx:62` — REST snapshot: `whoAmI` + compute + projects + knowledge + runs; Render = `lib/status.ts`. | отсутствует |

**Ключевое:** оба `comuki doctor` спросят «healthy?», но **про разное**.
TS-CLI спросит «мой клиент достучится до host?» (HTTP + cookie).
.NET-CLI спросит «host сам в порядке?» (config, env, DB, secrets).
Семантически это **разные** люди с **разными** глитчами. И оба
вызываются как `comuki doctor`.

### Что фактически задокументировано в коде v1 (TS CLI surface)

Эти факты взяты из текущего кода в `cli/src/` — единственные, на
которых ADR основывается.

- **`cli/src/lib/commands.ts:38-48`** — `resolveCommand` принимает
  только argv[0]; subcommands: `status runs login whoami config setup
  completion doctor archive`. Нет `version`, нет `init`, нет
  sub-`status` (например, `--json` уже реализован через global flag,
  см. `cli/src/index.tsx:68-72`).
- **`cli/src/lib/slash.ts:21-214`** — 31 slash-команда (см. §Inventory
  ниже). Каждая имеет один canonical name + optional aliases +
  usage + description; `resolveSlashAction` (`cli/src/lib/slash.ts:277-424`)
  возвращает tagged union `SlashAction` из 35 вариантов (включая
  sub-variants).
- **`cli/src/index.tsx:58-128`** — yargs registration с теми же
  9 subcommands; `--json` (`cli/src/index.tsx:68-72`) и `--message/-m`
  (`cli/src/index.tsx:78-82`) global flags. `demandCommand(0,0)`
  (`cli/src/index.tsx:126`) — no command → REPL.
- **`cli/src/index.tsx:147-192`** — `config show` (print, no Ink) и
  `setup`/`completion`/`doctor`/`archive` запускаются **до**
  `loadConfig` (для машины без config.json).
- **`cli/src/lib/auth.ts:loginAndStore`** — `comuki login` пишет
  session cookie в config.json.

### Что фактически задокументировано в коде v1 (.NET operator surface)

- **`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:8-15`** —
  "every branch here must work on a bare machine: no config.toml, no
  database, no logging pipeline". Runs **before** any host bootstrap.
  Build-time OpenAPI generation arrives with empty args и не входит
  ни в одну ветку (`ComukiHostCli.cs:13-15`).
- **`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs:55-63`** —
  `ComukiHostCliRouter.Route`: `version` / `config show` / `init`.
  `doctor` — async, `Program.cs:20-22` ловит его до `TryRun` и
  вызывает `ComukiDoctor.RunAsync` напрямую.
- **`platform/src/shared/Comuki.Shared.Bootstrap/Cli/ComukiCli.cs:5-11`**
  — shared CLI surface: `version` понимает **каждый** бинарь
  (`comuki`, `comuki-migrator`, …) через `ComukiBuildInfo.Read()
  .ToVersionLine(product)`.
- **`platform/src/host/Comuki.Migrator/Program.cs:13-18`** —
  `comuki-migrator version` и `status` (default = apply).

Эти факты — единственное эмпирическое основание для решения
ownership. Прочее (распределённые claims "operator vs user", и т.п.)
ADR не претендует выводить.

## Решение

### 1. Executable naming: отдельные бинари через разные имена + subcommand namespace

**TS user CLI остаётся `comuki`.** npm-пакет `@dot-stbl/comuki`,
bin `comuki` (default = REPL). Это **пользовательский** бинарь —
автор задачи/оператор sessions.

**.NET operator остаётся `comuki`** на диске в проектах как `comuki`,
но публикует **только operator surface** (`version`, `doctor`,
`config show`, `init`). Мигратор — отдельный exe `comuki-migrator`
(сегодня так и есть).

**Откуда взято "operator binary = `comuki`" сегодня:** `Program.cs:20`
и `ComukiCli.cs:36` называют продукт `comuki` — это **operator**
host (ASP.NET Core app, что запускается в k8s pod). Путаница в
том, что у TS-CLI тоже бинарь `comuki`.

**Решение naming:**

1. **Внутри проекта** оставляем `comuki` для .NET operator host
   (он уже развёрнут, переименование = breaking change для тех,
   кто по ssh делает `comuki doctor`). `.csproj` product name =
   `comuki` (фактически).
2. **В дистрибутивных artifacts** (.deb/.rpm/.tar, Docker image)
   operator host публикуется с **обоими** именами: `comuki` (для
   совместимости с уже написанными playbooks) + `comuki-host`
   (новый canonical). Docker image:
   `dot-stbl/comuki-host:{version}` (новая метка). Старый
   `dot-stbl/comuki:{version}` image переходит на TS CLI (один
   package `npm install -g @dot-stbl/comuki`).
3. **TS user CLI** остаётся `@dot-stbl/comuki` / `comuki` (npm), но
   **никогда** не публикуется как Docker image с именем
   `dot-stbl/comuki`. Это решит коллизию в публичном registry.

**Что это даёт:** `comuki` (бинарь на диске пользователя) = TS CLI;
`comuki` (бинарь в k8s pod оператора) = .NET operator host. На
диске это нельзя разрешить, но в **артефактах** дистрибуции — можно.
Шанс коллизии 0 для новых пользователей.

**Subcommand namespace внутри .NET operator** (как альтернатива
полному переименованию) — **отклонён** на текущий момент. Подробнее
в §Альтернативы ниже.

### 2. Command-by-command ownership matrix

Эта таблица — **решение**. Каждая команда получает одно из: **survive**
(оставить как есть), **move** (перенести в другой бинарь с
deprecation), **change** (изменить семантику/имя), **remove**
(удалить).

#### Argv-subcommands (TS CLI `cli/src/lib/commands.ts` + .NET `ComukiHostCli.cs`)

| Команда | TS user CLI сегодня | .NET operator сегодня | Решение | Комментарий |
|---|---|---|---|---|
| `version` | отсутствует | `ComukiCli.RunVersion("comuki", writer)` (`ComukiHostCli.cs:57`) | **add to TS** | Каждый бинарь должен иметь `version`. TS-CLI показывает `CLI_VERSION` из `cli/src/components/StatusLine.tsx` в `--version` через yargs (`cli/src/index.tsx:60`), но без `comuki version` argv. Добавить в `KNOWN_COMMANDS` (`cli/src/lib/commands.ts:20-30`) + branch в `main` рядом с `command === "login"` (`cli/src/index.tsx:140-142`). Источник: `ComukiBuildInfo` (C#) → JSON endpoint `GET /api/v1/version` (`platform/src/host/Comuki.Host/ApiRoutes.cs:13`). |
| `doctor` | URL+auth+config+theme | config+env+db+secrets | **survive (разделение)** | Семантика разная — оставить в обеих. ADR **не** объединяет; разные люди проверяют разное. |
| `status` | REST snapshot (compute, projects, knowledge, runs) | отсутствует | **survive** | TS-only. .NET operator делает это через OpenAPI/curl, добавлять не надо. |
| `runs [list]` | `cli/src/commands/runs.tsx:46-165` | отсутствует | **survive** | TS-only. |
| `login` | `cli/src/commands/login.tsx:19-82` — email+password → cookie в config.json | отсутствует | **survive** | TS-only. |
| `whoami` | `cli/src/index.tsx:265-288` — `client.me()` | отсутствует | **survive** | TS-only. |
| `config show` | `cli/src/commands/config.ts` — резолв `~/.config/comuki/config.json` с masked credentials | `ComukiHostCli.cs:34-46` — `RunConfigShow`: `config.toml + COMUKI_* env` | **survive (разные источники)** | Это **два разных конфига**: TS-CLI смотрит на свой `~/.config/comuki/config.json`; .NET operator смотрит на свой `config.toml`. Семантически — разные уровни. |
| `setup` | `cli/src/commands/setup.tsx:248-648` — Ink wizard | отсутствует | **change** (rename → `login --setup`) | Команда `setup` в TS создаёт **client-side** config. У .NET оператора есть `init` (создаёт **server-side** `config.toml` + `.env`). Коллизии имени `setup` с потенциальными server-side `setup` нет **сейчас**, но это слово уже зарезервировано в user-CLI; ADR **не** объединяет. Deprecation: добавить алиас `/setup` в `.agents/STATE.md` / `CHANGELOG`; v1.x: warn. v2: переименовать. |
| `completion` | `cli/src/commands/completion.ts` — print shell completion script | отсутствует | **survive** | TS-only. |
| `archive` | `cli/src/commands/archive.ts:8-12` — `listArchiveFiles` из `~/.config/comuki/archive/` | отсутствует | **survive** | TS-only. |
| `init` | отсутствует | `ComukiInit.cs:78-122` — пишет `config.toml` + `.env` | **survive** | Operator-only. TS-CLI не имеет аналога. |
| `--message/-m` | `cli/src/index.tsx:78-82` — `-m "text"` или stdin → one-shot | отсутствует | **survive** | TS-only. |
| `--json` | `cli/src/index.tsx:68-72` — `comuki status --json` / `runs --json` / `whoami --json` | отсутствует | **survive** | TS-only. |

#### Slash-commands (`cli/src/lib/slash.ts:21-214`) — классификация по host-gap

31 slash-команда классифицирована в **6 категорий** (см. задачу #82 §3):

| Класс | Определение | Команды |
|---|---|---|
| **working** | Полностью host-backed через существующий API, end-to-end протестировано. | `/exit` `/retry` `/edit` `/copycode` `/rename` `/clear` `/stop` `/help` `/new` `/sessions` `/login` `/approve` `/reject` `/runs` `/workers` `/status` `/kb add` `/kb list` |
| **local-only** | Локальный state (файлы, config.json, sessions.json, snippets, aliases). Не требует host. | `/export` `/bell` `/branch` `/snip [list\|save\|rm]` `/alias [list\|set\|rm]` `/profile [name]` `/theme [name]` `/keys` `/project <id\|slug\|name>` |
| **compatibility** | Работает через существующий wire, но с потерей семантики (нет фильтра, нет cursor, нет bookmark). | `/runs` (нет cursor/watermark, см. `cli/src/lib/runsfeed.ts:11-15`), `/workers` (только point-in-time, нет live feed — `cli/src/lib/client.ts:566-572`), `/status` (read-only, нет refresh — `cli/src/lib/status.ts`) |
| **host-gap** | Slash зарегистрирован, но host-API не существует. | `/tools` (`cli/src/lib/ops.ts:82-97` — honest-unavailable notice: нет HTTP GET для brain tools / MCP `tools/list`), `/note` (`cli/src/lib/ops.ts:104-119` — нет HTTP POST для memory write) |
| **misleading** | Slash обещает действие, которое на самом деле не происходит. | `/profile [name]` (см. `cli/src/lib/profiles.ts:73-74` — `createSession` has no profile field, so stored name never rides a turn; honest notice в коде уже есть) |
| **removable** | Кандидат на удаление. | `/archive` (как slash) — **collides** с argv-командой `comuki archive` (`cli/src/index.tsx:125`); функционально это дубликат argv. |

#### Slash-команды — детальные решения

| Slash | Сегодня (файл) | Решение | Обоснование |
|---|---|---|---|
| `/exit`, `/quit`, `/q` | `cli/src/lib/slash.ts:23-27` | **survive** | local-only, aliases |
| `/retry` | `slash.ts:28-33` | **survive** | local-only, retry last user message |
| `/edit` | `slash.ts:34-39` | **survive** | local-only, put last message back in prompt |
| `/copycode` | `slash.ts:40-45` | **survive** | local-only, OSC-52 clipboard |
| `/rename` | `slash.ts:46-51` | **survive** | local-only, sessions.ts rename |
| `/export` | `slash.ts:52-57` | **survive** | local-only, write transcript as markdown |
| `/bell` | `slash.ts:58-63` | **survive** | local-only, BEL+OSC-9 toggles |
| `/clear` | `slash.ts:64-69` | **survive** | local-only, wipe active transcript |
| `/stop` | `slash.ts:70-75` | **survive** | local-only, AbortController for in-flight turn |
| `/help` | `slash.ts:76-81` | **survive** | local-only, render registry |
| `/login` | `slash.ts:82-87` | **survive** | working — `POST /api/v1/auth/login` (`cli/src/lib/client.ts:438`) |
| `/sessions` | `slash.ts:88-93` | **survive** | local-only — sessions.json (см. `cli/src/lib/sessions.ts`); host list endpoint exists (`GET /api/v1/chat/sessions`, `ChatSessionsController.cs:66`) но CLI показывает local copy. |
| `/new` | `slash.ts:94-99` | **survive** | local-only, ctrl+n handler |
| `/approve` | `slash.ts:101-105` | **survive** | working — `POST /api/v1/chat/sessions/{id}/approve` (`cli/src/lib/client.ts:539-551`) |
| `/reject [reason]` | `slash.ts:106-111` | **survive** | working — same endpoint, `approved: false` |
| `/runs` | `slash.ts:112-117` | **change → live feed** | Совместимость. Сейчас `GET /api/v1/runs` через `client.runs()` (`cli/src/lib/client.ts:555-564`) — нет cursor. Решение: добавить host-side `?since=<unixMs>` query param (см. §Transport authority ниже); CLI — track `latestSeenUnixMs` локально в `fetchRunsFeedPanel` (`cli/src/commands/runsfeed.ts`). |
| `/workers` | `slash.ts:118-123` | **change → live feed** | Совместимость. Сейчас `GET /api/v1/workers/background` (`cli/src/lib/client.ts:570-572`) — single fetch. Решение: добавить host-side `?since=<unixMs>` для delta; или SignalR `Attention`/`RunEvent` с source `worker`. См. §Transport authority. |
| `/plan` | `slash.ts:124-129` | **survive** | working — `ChatMessageView.parts[].kind === "plan"` (`cli/src/lib/client.ts:96-100`) |
| `/project <id\|slug\|name>` | `slash.ts:130-135` | **change → host-backed switch** | local-only сейчас (sets `defaultProject` в `config.json`). Решение: добавить host-side `POST /api/v1/projects/{id}/switch` (или `PUT /api/v1/me/preferences { defaultProject }`) — server remembers the default per subject. Это сохраняет контекст между сессиями и устройствами. До этого — оставить local-only. |
| `/snip [list\|save\|rm\|<name>]` | `slash.ts:136-141` | **survive** | local-only, snippets в `~/.config/comuki/snippets/` |
| `/branch [message]` | `slash.ts:142-147` | **survive** | local-only, fork session — host не знает про branches |
| `/kb add <file\|glob>`, `/kb list` | `slash.ts:148-153` | **change → wire shape lock** | working. Сейчас `POST /api/v1/knowledge/ingest` (`cli/src/lib/client.ts:591-600`) + `GET /api/v1/knowledge/documents` (`cli/src/lib/client.ts:574-582`). Решение: контракт **wire-shape locked** — оба endpoint'а стабильные; cli должен показывать `kbWriteUnavailableLines` (`cli/src/lib/kb.ts:310`) при `401/403`, и НЕ fallback'ить на MCP `memory.recall` (отдельный surface, см. `cli/src/lib/mentions.ts:18`). |
| `/theme [name]` | `slash.ts:154-159` | **survive** | local-only, theme-switching через `resolveTheme` |
| `/whoami` | `slash.ts:160-165` | **survive** | working — `GET /api/v1/auth/me` (`cli/src/lib/client.ts:493-495`) |
| `/keys` | `slash.ts:166-171` | **survive** | local-only, keybinding reference |
| `/status` | `slash.ts:172-177` | **survive** | working — same as `comuki status` (single fetch) |
| `/open` | `slash.ts:178-183` | **survive** | local-only + side-effect (open dashboard URL в OS browser — `cli/src/lib/ops.ts:155-184`) |
| `/tools` | `slash.ts:184-189` | **remove (host-gap)** | Нет HTTP API. См. `cli/src/lib/ops.ts:82-97` — honest unavailable notice. **Решение:** убрать из registry. `/help` больше не показывает `/tools`. Сейчас выдаёт `toolsUnavailableLines`, что полезно, но **misleading** для пользователя: "я зарегистрировал команду, но она не работает". **Replace by:** `/open` → dashboard's MCP browser panel. |
| `/note <text>` | `slash.ts:190-195` | **remove (host-gap)** | Нет HTTP API. См. `cli/src/lib/ops.ts:104-131`. **Решение:** убрать из registry. Replace by: dashboard console. |
| `/profile [name]` | `slash.ts:196-201` | **remove (misleading)** | `createSession` has no profile field (см. `cli/src/lib/profiles.ts:73-74` — honest notice уже в коде). **Решение:** убрать. Replace by: `comuki config show` (показывает default profile, если добавлен) или отдельная страница в dashboard. |
| `/alias [set\|rm\|<name>]` | `slash.ts:202-207` | **survive** | local-only |
| `/archive` (slash) | `slash.ts:208-213` | **remove** | Дубликат argv `comuki archive` (`cli/src/index.tsx:125` + `cli/src/commands/archive.ts`). Оставить argv, удалить slash. |

### 3. Transport authority matrix

REST authoritative для всех state-changing и query операций. SignalR —
только progress (`ChatChunk`, `ChatTurnComplete`) + run-event live feed
(`RunEvent`, `Attention`). **Cursor/watermark status** — `Last-Modified`
or `ETag` HTTP headers, **не** SignalR today.

| Transport | Surface | Owner |
|---|---|---|
| REST `POST /api/v1/chat/sessions/{id}/messages` | синхронный turn return | **authoritative** |
| SignalR `ChatChunk` / `ChatTurnComplete` | live progress fragment + turn terminal signal | **authoritative** для progress (см. `platform/src/host/Comuki.Host/Realtime/Broadcasting/SignalRChatTurnProgress.cs:1-46`); REST остаётся source-of-truth для final state |
| REST `GET /api/v1/runs` | paged ledger | **authoritative**; cursor/watermark = planned (host adds `?since=<unixMs>`; CLI passes `lastSeenAt`) |
| SignalR `RunEvent` (group `run:{id}`) | journal append stream | authoritative для journal tail; REST list — point-in-time snapshot |
| SignalR `Attention` (group `project:{id}:attention`) | attention-worthy transitions (running/failed/escalated) | authoritative для live tail; REST `/api/v1/runs` — queryable filter |
| REST `GET /api/v1/workers/background` | point-in-time registry | **authoritative**; live updates — planned (см. ниже) |
| REST `GET /api/v1/knowledge/ingest` + `/documents` + `/search` | knowledge corpus | **authoritative**; не через MCP (см. `cli/src/lib/kb.ts:304-310` — `kbWriteUnavailableLines` для 401/403 на ingest; см. также `cli/src/lib/client.ts:584-590` — permission requirement) |
| REST `GET /api/v1/compute` | compute snapshot | **authoritative** |
| REST `GET /api/v1/projects` | project list | **authoritative** |
| REST `GET /api/v1/auth/me`, `POST /api/v1/auth/login` | identity | **authoritative** |
| REST `GET /api/v1/health` | liveness | **authoritative** |
| MCP `POST /api/v1/mcp` JSON-RPC | `tools/list`, `memory.note`, `memory.search` (think-loop tools) | authoritative для **tool** surface; **NOT** REST-equivalent (см. `cli/src/lib/ops.ts:82-119` — `/tools` и `/note` — host-gap именно потому что не REST) |
| SignalR hub `JoinChatAsync` / `JoinRunAsync` / `JoinProjectAsync` / `LeaveXxx` | group management | authoritative; client-id-scoped (см. `platform/src/host/Comuki.Host/Realtime/RunsHub.cs:42-126`) |

#### Cursor / watermark status today и план замен

| Surface | Сегодня | План замен |
|---|---|---|
| `GET /api/v1/runs` | no `since`, full paged list (см. `cli/src/lib/client.ts:555-564`) | **add** `?since=<unixMs>`; server возвращает только новые записи (после `createdAt > since`); клиент хранит `latestSeenUnixMs`. Это решает `/runs` cursor. |
| `GET /api/v1/workers/background` | single fetch, no delta (см. `cli/src/lib/client.ts:566-572`) | **add** `?since=<unixMs>` для registry entries (`lastRunAt > since`). |
| SignalR `RunEvent` | group-addressed, fire-on-write (см. `dashboard/src/shared/realtime/runs-hub.ts:35-40`) | оставить как live tail; CLI `/runs` использует REST cursor + SignalR для догонки. |
| `ChatMessageView.createdAt` ordering | server-stamped, monotonic (см. `cli/src/lib/client.ts:130`) | оставить; cursor `before=<messageId>` или `before=<unixMs>` — planned. |

### 4. Slash-command source of truth

`cli/src/lib/slash.ts` — single registry (`SLASH_COMMANDS`,
`cli/src/lib/slash.ts:21-214`) + parser (`resolveSlashAction`,
`cli/src/lib/slash.ts:277-424`) + autocomplete helpers. `/help`
(`slash.ts:217-227`) renders from the registry — удаление
команды из registry **автоматически** убирает её из help.

`commands.ts` — argv routing (`cli/src/lib/commands.ts:38-48`).
Single source: `KNOWN_COMMANDS: readonly CommandName[]`
(`cli/src/lib/commands.ts:20-30`).

**Двухрегистровая модель — оставить.** argv и slash живут в разных
местах, разные parsers, разный audience (one-shot vs REPL).

### 5. Wire-type source of truth

**Authoritative: C# `View` records в `platform/src/host/Comuki.Host/`.**
CLI в `cli/src/lib/client.ts:42-254` дублирует их **вручную**
(camelCase JSON, имена полей byte-for-byte, см. комментарий в
`cli/src/lib/client.ts:1-14`). Переименование любой стороны = wire break.

| TS type (`cli/src/lib/client.ts`) | C# view (`platform/src/host/Comuki.Host/`) |
|---|---|
| `MeView` (`:42-50`) | implicit from auth/me endpoint |
| `HealthView` (`:52-55`) | anonymous liveness contract |
| `ChatSessionView` (`:57-64`) | `Chat/Models/Views/ChatSessionView.cs:6-25` |
| `MessagePart` discriminated union (`:66-100`) | `Comuki.Shared.Contracts/Chat/MessagePart.cs` + `MessagePartKinds.cs` |
| `PlanItemView`, `PlanEdgeView` (`:102-112`) | `Comuki.Shared.Contracts/Plans/Plan*.cs` |
| `ChatMessageMetaView` (`:114-121`) | `Comuki.Shared.Contracts/Chat/ChatMessageMeta.cs` |
| `ChatMessageView` (`:123-131`) | `Chat/Models/Views/ChatMessageView.cs:11-35` |
| `ChatTurnResultView` (`:133-137`) | built in controller, not a separate view |
| `ChatMessagesPageView` (`:139-144`) | `Chat/Models/Views/ChatMessagesPageView.cs` |
| `RunView` (`:146-152`) | built in runs controller |
| `RunsPageView` (`:154-159`) | built in runs controller |
| `WorkerResultView` (`:161-166`) | `Comuki.Shared.Bootstrap/Workers/WorkerResult.cs` |
| `BackgroundWorkerView` (`:168-181`) | `Comuki.Shared.Bootstrap/Workers/WorkerStatus.cs` snapshot |
| `KnowledgeDocumentSummaryView` (`:183-193`) | knowledge module view |
| `KnowledgeDocumentsPageView` (`:195-200`) | knowledge module view |
| `KnowledgeIngestResultView` (`:202-206`) | knowledge module view |
| `KnowledgeSearchHitView` (`:208-214`) | knowledge module view |
| `ComputePoolView` (`:216-223`) | compute module view |
| `ComputeSnapshotView` (`:225-233`) | compute module view |
| `ProjectView` (`:235-241`) | projects module view |
| `ProfileView` (`:243-254`) | `Comuki.Shared.Contracts/ControlPlane/Profiles/ProfileDefinition.cs` |
| `LoginSuccess` (`:256-261`) | auth controller response (no formal record) |
| `ChatChunkView` (signalr.ts:105-109) | `Comuki.Shared.Contracts/Realtime/ChatChunkView.cs` |
| `ChatTurnCompleteView` (signalr.ts:111-114) | `Comuki.Shared.Contracts/Realtime/ChatTurnCompleteView.cs` |

**Планируемая замена ручного зеркала:** codegen из C# через
`Comuki.Shared.Contracts/Realtime/RealtimeContractEmitter.cs`
(уже существует для `RealtimeTransportMethods.cs:1-25` — один
constant emitter). Расширить emitter до полного `View`-record
emission → TypeScript types → `cli/src/lib/contracts.ts` (auto).
**Это не часть этого ADR** — это **re-open trigger** для ADR-0004
(proposed).

### 6. Compatibility obligations при депривациях

| Изменение | Backwards compat | Deprecation path |
|---|---|---|
| `comuki setup` → keep (no rename) | n/a | n/a — нотировать `init` (operator) vs `setup` (user) |
| `/tools` slash removal | warn-on-use одна minor-версию | удалить регистрацию; `/help` не показывает |
| `/note` slash removal | warn-on-use одна minor-версию | удалить регистрацию |
| `/profile` slash removal | warn-on-use одна minor-версию | удалить регистрацию |
| `/archive` slash removal | warn-on-use одна minor-версию | argv `comuki archive` остаётся |
| `/runs` cursor upgrade | old params still works; new params opt-in | old param → new param — backward-compatible |
| `/workers` cursor upgrade | same | same |

Все депривации — **soft-warn** (напечатать notice в transcript, не
exit 1). Hard cutover — следующая major.

---

## Отклонённые альтернативы

### A. Subcommand namespace `comuki host <verb>`

`comuki host doctor`, `comuki host config show`, `comuki host init` —
пользователь вызывает один бинарь `comuki` (TS CLI), а тот сам
решает, маршрутизировать в локальный или в operator (через host API).

**Отклонена.** Это **путало бы хуже**. TS CLI не имеет права
выполнять server-side команды (`init` пишет на диск сервера,
`doctor` проверяет Postgres). Прокидывание `init` через CLI = нужно
новый endpoint `POST /api/v1/host/init`, который выполняет ту же
операцию, что и `ComukiInit.Run`. Дублирование. Плюс: ломает
**уже работающий** TS-CLI `comuki doctor` (URL+auth), который
**не** делает server-side проверки, но **может** работать на машине
без доступа к Postgres. Subcommand namespace склеивает два разных
уровня абстракции.

### B. Single CLI binary на C# (замена TS CLI на .NET CLI)

Заменяет Ink/React TS-CLI на Spectre.Console / System.CommandLine.

**Отклонена.** Этот ADR не про renderer — он про ownership. ADR-0002
принял Core TUI path на OpenTUI/React (отложенный React). Замена
TS-CLI на .NET-CLI = новый spike, новый ADR, новый migration effort.
Сейчас TS-CLI работает и покрыт тестами.

### C. Single-binary fat-binary (TS-CLI + .NET-operator в одном exe)

TS-CLI шёл бы bundled в .NET-publish-output.

**Отклонена.** Bundling проблематичен (Bun compile vs
dotnet publish), размер артефакта ~120 MB уже пограничный
(см. ADR-0002 §3), а толку мало: TS-CLI и .NET-operator живут в
разных runtime-пространствах (Node vs .NET).

### D. Поменять имя TS-CLI на `cterm` / `comuki-tui` / etc.

**Отклонена.** `comuki` уже зарегистрирован в npm как
`@dot-stbl/comuki`. Renaming = breaking change для тех, кто
`npm install -g @dot-stbl/comuki`. Альтернатива — оставить npm
имя, добавить alias `cterm` для legacy-установок — не решает
проблему двух `comuki`-бинарей **на диске**. ADR идёт по (1): npm
package + bin = `comuki`; .NET operator публикуется как
`comuki-host` в Docker, имя `comuki` остаётся для совместимости.

---

## Re-open triggers

Этот ADR пересматривается, **когда любое** из:

1. **Codegen pipeline** для wire-types начинает выдавать
   TypeScript-контракт из C# (`comuki/src/lib/contracts.ts`).
   Сейчас — manual mirror (`cli/src/lib/client.ts:1-14`).
   Codegen → single source of truth, пересмотр ownership станет
   тривиальным.
2. **Кто-то реально пишет playbook, который вызывает
   `comuki init` на сервере через SSH** — это требует новый
   endpoint `POST /api/v1/host/init` (server-side вариант). На
   текущий момент ни одного playbook'а не задокументировано.
3. **Run/worker live feed через SignalR** — если это станет
   реальной фичей (v2+), `/runs` и `/workers` cursor upgrades
   могут оказаться избыточными. См. §Transport authority.
4. **Multi-binary install model** — если distribution решает
   публиковать только Docker images (без host-baremetal бинаря),
   `comuki-host` rename можно пересмотреть.
5. **OpenSpec `comuki host` post-mortem** — если такое
   инициируется, ownership становится trivially wrong.

## Плюсы и минусы

**Плюсы (на текущий момент):**

- `comuki doctor` остаётся **раздельно** — каждая среда проверяет
  своё, и путаница user-vs-operator не усугубляется;
- `.NET operator` бинарь сохраняет существующий surface (`version`,
  `doctor`, `config show`, `init`) — нет breaking change для
  операторов;
- TS-CLI сохраняет существующий surface (`status`, `runs`, `login`,
  `whoami`, `config`, `setup`, `completion`, `doctor`, `archive`)
  — нет breaking change для пользователей;
- `~/.config/comuki/config.json` (TS) и `config.toml` (.NET) —
  **разные** файлы, оба имеют смысл, оба сохраняются;
- удаление `host-gap` slash (`/tools`, `/note`) и
  `misleading` (`/profile`) делает `/help` честным.

**Минусы / честные неизвестные:**

- Naming collision на диске остаётся для тех, кто
  скачивает **оба** бинаря в одну PATH; для них `comuki doctor`
  берёт **первый** в PATH — потенциально wrong tool. ADR не
  решает это в дистрибуции (см. §Альтернативы);
- soft-deprecation `/tools`, `/note`, `/profile`, `/archive` slash
  — **dependent** на major version bump для hard cutover;
- `setup` (TS) vs `init` (.NET) — разные команды, разные
  конфиги. Документация обязана это объяснять;
- Codegen для wire-types не начата. Manual mirror — current
  source-of-truth;
- Live feed для `/runs` и `/workers` — **planned** cursor
  upgrade, не реализовано.

## Следующие шаги

1. **Issue: rename `.NET operator` Docker image `comuki-host`.**
   Не блокер для этого ADR — может идти параллельно.
2. **Issue: codegen pipeline `C# View records → TS types`.**
   Решает ручное зеркало, но **выходит за scope** ADR-0003.
3. **Issue: cursor upgrades (`?since=<unixMs>`) на `GET /runs` и
   `GET /workers/background`.** Закрывает `/runs` и `/workers`
   compatibility-категорию.
4. **Issue: server-side `/project switch` endpoint.** Закрывает
   local-only `/project` slash.
5. **Issue: slash deprecations soft-warn (`/tools`, `/note`,
   `/profile`, `/archive`).** Записываются в `STATE.md` / changelog
   v1.x.
6. **Docs: объяснить `setup` (TS) vs `init` (.NET) в
   `.agents/docs/architecture/`.** Not blocking — отдельный issue.

## Proposed issue titles (НЕ создавать — owner создаёт)

Владелец должен решить, какие из них становятся issues, и в каком
порядке. ADR не открывает issues автоматически.

1. `[host] rename Docker image to comuki-host (keep comuki as alias)`
2. `[cli] add argv version command (source: GET /api/v1/version)`
3. `[cli] remove slash `/tools` (host-gap: no HTTP GET for brain/MCP tools)`
4. `[cli] remove slash `/note` (host-gap: no HTTP POST for memory write)`
5. `[cli] remove slash `/profile` (misleading: stored name never rides a turn)`
6. `[cli] remove slash `/archive` (duplicate of argv `comuki archive`)`
7. `[host] cursor upgrades on GET /api/v1/runs (since param)`
8. `[host] cursor upgrades on GET /api/v1/workers/background (since param)`
9. `[host] server-side project preference endpoint (POST /api/v1/me/preferences)`
10. `[cli] codegen pipeline: C# View records → TS contracts (replaces manual mirror)`
11. `[docs] explain 'setup' (TS) vs 'init' (.NET) in .agents/docs/architecture/`
12. `[host] wire-type cross-check: every CLI Comuki*View mirrors a C# record`

## Related

- [adr-0002-cli-opentui.md](./adr-0002-cli-opentui.md) — TUI-стек CLI
  (Core path принят); orthogonal к этому ADR.
- [comuki-architecture.md](./comuki-architecture.md) — общая
  архитектура оркестратора (для контекста про operator / migrator).
- [`cli/src/lib/commands.ts`](../../cli/src/lib/commands.ts) — argv
  registry (TS).
- [`cli/src/lib/slash.ts`](../../cli/src/lib/slash.ts) — slash
  registry (TS).
- [`cli/src/lib/client.ts`](../../cli/src/lib/client.ts) — manual
  wire-type mirror (TS).
- [`cli/src/lib/signalr.ts`](../../cli/src/lib/signalr.ts) — SignalR
  client + retry policy (TS).
- [`platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs`](../../platform/src/host/Comuki.Host/Cli/ComukiHostCli.cs)
  — argv dispatcher (.NET).
- [`platform/src/shared/Comuki.Shared.Bootstrap/Cli/ComukiCli.cs`](../../platform/src/shared/Comuki.Shared.Bootstrap/Cli/ComukiCli.cs)
  — shared `version` (.NET).
- [`platform/src/host/Comuki.Host/Cli/Doctor/ComukiDoctor.cs`](../../platform/src/host/Comuki.Host/Cli/Doctor/ComukiDoctor.cs)
  — `comuki doctor` (.NET).
- [`platform/src/host/Comuki.Host/Cli/Init/ComukiInit.cs`](../../platform/src/host/Comuki.Host/Cli/Init/ComukiInit.cs)
  — `comuki init` (.NET).
- [`platform/src/host/Comuki.Host/ApiRoutes.cs`](../../platform/src/host/Comuki.Host/ApiRoutes.cs)
  — host API routes (source of truth для endpoint paths).
- [`platform/src/shared/Comuki.Shared.Contracts/Realtime/`](../../platform/src/shared/Comuki.Shared.Contracts/Realtime/)
  — SignalR contract (.NET source of truth).
- Issue #82: https://github.com/dot-stbl/comuki/issues/82

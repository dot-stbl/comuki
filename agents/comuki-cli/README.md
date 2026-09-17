# comuki — terminal-native CLI

Терминальный клиент платформы Comuki: мульти-сессия с мозгом (табы как
в браузере), снапшот платформы, реестр ранов. Без рамок и панелей —
терминал и есть интерфейс, один акцентный цвет (slate-blue), результат —
обычным цветом терминала.

```
  comuki v0.2.0 · brad · project: nova
  [1] identity-refactor ● [2] readme-fix [+]

you  › сделай план рефакторинга Identity

     ⠋ comuki thinking  …память: identity → Users/Grants/Keys…

     ✓ memory.search("identity")  2 facts
     ✓ list_profiles  4 keys

comuki › План рефакторинга Identity:
            1. Разделить на Users/Grants/Keys
            2. Вынести OIDC в handler

you  › approve
  ✓ approving…

  ●1 identity-refactor ●2 readme-fix   esc · tab · ctrl+n
```

Пустой старт — центрированный welcome (вордмарк, статистика платформы);
после первого сообщения он не возвращается до перезапуска.

## Запуск

```bash
cd agents && bun install

# из исходников (REPL — команда по умолчанию)
COMUKI_URL=http://localhost:8080 COMUKI_API_KEY=ck_… \
  bun run --filter '@comuki/cli' start

# или прямо бинарём
COMUKI_URL=http://localhost:8080 COMUKI_API_KEY=ck_… \
  bun agents/comuki-cli/bin/comuki.ts

# скомпилированный single-file бинарень (~110 MB, bun compile)
cd agents/comuki-cli && bun run build   # → ./comuki.exe (Windows) / comuki
```

## Команды

| Команда | Что делает |
|---|---|
| `comuki` | мульти-сессия REPL с мозгом (команда по умолчанию) |
| `comuki status` | снапшот платформы: provider, queue, projects, knowledge, runs |
| `comuki runs [list]` | таблица ранов; `--page`, `--pageSize`, `--filter status==queued` |
| `comuki login` | email+пароль → session cookie в `~/.config/comuki/config.json` |
| `comuki whoami` | текущий субъект, роли, разрешения |

Глобальные опции: `--url`, `--api-key`, `--project` (id, slug или имя).
Субкоманды `chat` больше нет — голый `comuki` и есть чат.

## Сессии — табы

N параллельных задач, каждая крутится на сервере; переключение — как
табы в браузере, ноутбук остаётся холодным.

| Клавиша | Действие |
|---|---|
| `enter` | отправить сообщение |
| `esc` | обзор сессий (закрыть — esc ещё раз) |
| `tab` / `shift+tab` | следующая / предыдущая сессия |
| `1`–`9` | выбрать сессию (в обзоре) |
| `ctrl+n` | новая сессия |
| `ctrl+w` | закрыть таб (задача продолжает работать на сервере!) |
| `ctrl+c` | выход из CLI |
| `↑` `↓` | история ввода |

Новая сессия становится серверной лениво — при первом сообщении; имя
таба — первые 20 символов сообщения. Фоновый таб с ответом подсвечивается
точкой (unread). Список открытых табов сохраняется в
`~/.config/comuki/sessions.json` и восстанавливается при старте
(транскрипт подтягивается при первом переключении на таб).

В REPL: `/exit` `/quit` `/q` — выход, `/clear` — очистить активный
транскрипт, `/help` — справка, `/sessions` — обзор, `/new` — новая
сессия, `approve` / `reject [reason]` — решение по ждущему плану.

## Конфигурация

Прецедентность: CLI-флаги > переменные окружения >
`~/.config/comuki/config.json` > `http://localhost:8080`.

| Источник | Переменная |
|---|---|
| URL сервера | `COMUKI_URL` |
| API-ключ (`Authorization: Bearer ck_…`) | `COMUKI_API_KEY` |
| Tenant для scoped-ключей (`X-Comuki-Tenant`) | `COMUKI_TENANT` |
| Проект по умолчанию | `COMUKI_PROJECT` |
| Каталог конфигов (XDG) | `XDG_CONFIG_HOME` |
| Cookie сессии (после `comuki login`) | `~/.config/comuki/config.json` |

```
~/.config/comuki/
├── config.json    # url, api_key, cookie, default_project
└── sessions.json  # открытые табы для восстановления при старте
```

## Как устроено

- **REST — источник истины.** `POST /api/v1/chat/sessions/{id}/messages`
  синхронный и возвращает весь результат хода (`ChatTurnResultView`),
  поэтому чат работает и без сокета. Ходы запускаются без await —
  фоновый таб думает себе спокойно, отмечаясь unread-точкой.
- **SignalR — живой прогресс.** `/ws/runs`, группа `chat:{id}` на одну
  (общую) связь: `ChatChunk` несёт `sessionId`, чанки маршрутизируются
  по табам. Сокет — best-effort: недоступен → тихий спиннер, чат не
  ломается.
- **Состояние сессий — чистые функции.** `src/lib/sessions.ts`:
  создание/переключение/закрытие/unread/persist — тестируется без Ink.
- **Рендер — чистые функции.** `src/lib/format.ts` превращает wire-части
  (text / thinking / tool / code / plan / handoff) в ANSI-строки; Ink
  только выводит.
- **Соответствие контракту.** Формы повторяют C# read-модели хоста
  (`ChatSessionsController`, `RunsController`, `ComputeSnapshotEndpoints`):
  camelCase, `kind`-дискриминатор частей, фильтр-DSL ранов.

## Тесты

```bash
cd agents && bun test comuki-cli   # config / client / format / signalr / sessions / commands
```

API замокан через инжектируемый `fetch` — живой сервер не нужен.

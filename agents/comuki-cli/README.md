# comuki — terminal-native CLI

Терминальный клиент платформы Comuki: интерактивная сессия с мозгом,
снапшот платформы, реестр ранов. Без рамок и панелей — терминал и есть
интерфейс, один акцентный цвет (slate-blue), результат — обычным цветом
терминала.

```
  comuki v0.1.0 · brad · project: nova

you  › сделай план рефакторинга Identity

     ⠋ comuki thinking  …память: identity → Users/Grants/Keys…

     ✓ memory.search("identity")  2 facts
     ✓ list_profiles  4 keys

comuki › План рефакторинга Identity:
           1. Разделить на Users/Grants/Keys
           2. Вынести OIDC в handler

you  › approve
  ✓ approving…
```

## Запуск

```bash
cd agents && bun install

# из исходников
COMUKI_URL=http://localhost:8080 COMUKI_API_KEY=ck_… \
  bun run --filter '@comuki/cli' start -- chat

# или прямо бинарём
COMUKI_URL=http://localhost:8080 COMUKI_API_KEY=ck_… \
  bun agents/comuki-cli/bin/comuki.ts chat

# скомпилированный single-file бинарень (~110 MB, bun compile)
cd agents/comuki-cli && bun run build   # → ./comuki.exe (Windows) / comuki
```

## Команды

| Команда | Что делает |
|---|---|
| `comuki chat` | интерактивный REPL с мозгом (команда по умолчанию) |
| `comuki status` | снапшот платформы: provider, queue, projects, knowledge, runs |
| `comuki runs [list]` | таблица ранов; `--page`, `--pageSize`, `--filter status==queued` |
| `comuki login` | email+пароль → session cookie в `~/.comuki/config.json` |
| `comuki whoami` | текущий субъект, роли, разрешения |

Глобальные опции: `--url`, `--api-key`, `--project` (id, slug или имя).

В REPL: `exit`/`quit`/`q` — выход, `clear` — очистить экран, `help` —
справка, `approve` / `reject [reason]` — решение по ждущему плану. Стрелки
вверх/вниз — история ввода.

## Конфигурация

Прецедентность: CLI-флаги > переменные окружения > `~/.comuki/config.json`
> `http://localhost:8080`.

| Источник | Переменная |
|---|---|
| URL сервера | `COMUKI_URL` |
| API-ключ (`Authorization: Bearer ck_…`) | `COMUKI_API_KEY` |
| Tenant для scoped-ключей (`X-Comuki-Tenant`) | `COMUKI_TENANT` |
| Проект по умолчанию | `COMUKI_PROJECT` |
| Cookie сессии (после `comuki login`) | `~/.comuki/config.json` |

## Как устроено

- **REST — источник истины.** `POST /api/v1/chat/sessions/{id}/messages`
  синхронный и возвращает весь результат хода (`ChatTurnResultView`),
  поэтому чат работает и без сокета.
- **SignalR — живой прогресс.** `/ws/runs`, группа `chat:{id}` (`JoinChatAsync`),
  события `ChatChunk` / `ChatTurnComplete` анимируют ожидание. Сокет —
  best-effort: недоступен → тихий спиннер, чат не ломается.
- **Рендер — чистые функции.** `src/lib/format.ts` превращает wire-части
  (text / thinking / tool / code / plan / handoff) в ANSI-строки; Ink
  только выводит.
- **Соответствие контракту.** Формы повторяют C# read-модели хоста
  (`ChatSessionsController`, `RunsController`, `ComputeSnapshotEndpoints`):
  camelCase, `kind`-дискриминатор частей, фильтр-DSL ранов.

## Тесты

```bash
cd agents && bun test comuki-cli   # 38 тестов: config / client / format / signalr
```

API замокан через инжектируемый `fetch` — живой сервер не нужен.

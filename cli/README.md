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

  ●1 identity-refactor ●2 readme-fix   esc · tab · pgup/pgdn · ctrl+n
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

Глобальные опции: `--url`, `--api-key`, `--project` (id, slug или имя),
`--theme <name>-<dark|light>` (по умолчанию `dichromat-dark` — все семь
тем дашборда: dichromat, graphite, dockside, blueprint, bureau,
aperture, dispatcher; выбор сохраняется в `config.json` → `theme`),
`--tui opentui` (REPL на OpenTUI Core — см. ниже).
Субкоманды `chat` больше нет — голый `comuki` и есть чат.

## OpenTUI-хост (`--tui opentui`)

Опциональный focus-mode REPL на `@opentui/core` + `@opentui/keymap`
(ADR-0002; Ink остаётся хостом по умолчанию и не тронут):

```bash
comuki --tui opentui     # тот же config/ auth/ sessions.json, новый рендер-стек
COMUKI_LANG=ru comuki --tui opentui   # ru-локаль chrome/карточки (по умолчанию en)
```

Что работает в этом срезе: alternate-screen, compact-раскладка на узких
терминалах (48x16), транскрипт прямо из снапшотов ClientKernel (эхо,
живой текст стрима, финальный ответ, строка ошибки), инлайн-карточка
одобрения (intent/scope/risk/plan-steps/diff, реальные опции
approve/reject), named-command keymap, чистый выход
(kernel.stop → whenIdle → renderer.destroy, терминал восстановлен).

| Клавиша | Действие |
|---|---|
| `enter` | отправить сообщение |
| `esc` | прервать текущий ход (клиентский abort, `/stop`) |
| `y` / `n` | одобрить / отклонить план (только пока карточка на экране) |
| `ctrl+n` | новая сессия |
| `ctrl+w` | закрыть таб (задача продолжает работать на сервере) |
| `ctrl+c` | выход |

Чего пока нет: палитра команд, slash-команды, очередь follow-up,
переключение табов (одна активная сессия), $EDITOR через lifecycle-seam
(seam встроен и тестируется, редактор не подключён).

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
| `pgup` / `pgdn` | прокрутка транскрипта на страницу |
| `↑` `↓` | история ввода — а когда транскрипт проскроллен вверх, построчная прокрутка |
| `home` / `end` | в начало / в конец транскрипта (`end` возобновляет следование) |

Транскрипт скроллится внутри приложения (как в irssi/htop), а не в
скроллбэке терминала: по умолчанию вьюпорт прижат к низу и следует за
новым выводом. `pgup` отрывает следование; пока вы наверху, новый вывод
не дёргает экран — внизу вьюпорта появляется приглушённая строка
`↓ new messages`, а `end` / `pgdn` / `↓` возвращают следование. Инпут
и футер закреплены и никогда не уезжают.

Новая сессия становится серверной лениво — при первом сообщении; имя
таба — первые 20 символов сообщения (ручной `/rename` имеет приоритет
над автоименем). Фоновый таб с ответом подсвечивается точкой (unread).
Список открытых табов сохраняется в `~/.config/comuki/sessions.json`
и восстанавливается при старте (транскрипт подтягивается при первом
переключении на таб).

В REPL: `/exit` `/quit` `/q` — выход, `/clear` — очистить активный
транскрипт, `/help` — справка (рендерится из реестра команд), `/retry`
— повторно отправить последнее сообщение, `/rename <title>` —
переименовать активную сессию, `/export [path]` — сохранить транскрипт
в markdown (ходы — `## you` / `## comuki`, thinking/tool свёрнуты в
цитаты; путь по умолчанию `./comuki-{имя}-{дата}.md`), `/bell on|off` —
звонок терминала при завершении хода (OSC 9 toast приходит всегда),
`/sessions` — обзор, `/new` — новая сессия, `/stop` — прервать текущий
ход (клиентский abort: сервер продолжает думать, CLI перестаёт слушать
и ставит dim-метку `⏺ stopped`), `approve` / `reject [reason]` —
решение по ждущему плану, `/snip` — сохранённые промпты (`/snip` список,
`/snip <name>` отправить сниппет как сообщение — редактор не имеет
prefill, поэтому сниппет уходит в чат целиком; `/snip save <name>`
сохраняет последнее отправленное сообщение, `/snip rm <name>` удаляет;
имена `[a-z0-9-]+`, хранится в `~/.config/comuki/snippets.json`),
`/branch [message]` — форк активной сессии: новый таб с новой серверной
сессией `fork of {имя}` в том же проекте, первым сообщением уходит
явный текст или последнее сообщение источника (retry-in-new-tab;
транскрипт сервер не копирует — полный форк требует серверной
поддержки). При вводе `/` над промптом открывается меню
автокомплита: фильтрация на лету, `↑`/`↓` выбор, `tab`/`enter`
дополнение, `esc` закрыть.

Промпт многострочный: `shift+enter` / `alt+enter` (или `\` в конце
строки + `enter`) переносят строку, чистый `enter` отправляет. Пока ход
думает, промпт жив: отправленное сообщение встаёт в очередь (метка
`⏺ queued`, над промптом счётчик) и уходит по мере завершения ходов —
`/stop` и ошибки очередь сохраняют.

Заголовок окна терминала следует за активным табом:
`comuki — {имя} ⏳` пока ход в полёте, `comuki — {имя} ✓` когда
завершился.

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
├── config.json    # url, api_key, cookie, default_project, theme, bell
└── sessions.json  # открытые табы для восстановления при старте
```

## Как устроено

- **REST — источник истины.** `POST /api/v1/chat/sessions/{id}/messages`
  синхронный и возвращает весь результат хода (`ChatTurnResultView`),
  поэтому чат работает и без сокета. Ходы запускаются без await —
  фоновый таб думает себе спокойно, отмечаясь unread-точкой.
- **SignalR — живой прогресс.** `/ws/runs`, группа `chat:{id}` на одну
  (общую) связь: `ChatChunk` несёт `sessionId`, чанки маршрутизируются
  по табам. Живой текст рендерится на лету растущим блоком с курсором
  `▌`; финальный ответ хода заменяет его собой. Сокет — best-effort:
  недоступен → тихий спиннер, чат не ломается.
- **Состояние сессий — чистые функции.** `src/lib/sessions.ts`:
  создание/переключение/закрытие/unread/persist — тестируется без Ink.
- **Рендер — чистые функции.** `src/lib/format.ts` превращает wire-части
  (text / thinking / tool / code / plan / handoff) в ANSI-строки; Ink
  только выводит. Markdown-проза ассистента — через `src/lib/markdown.ts`
  (лексер `marked` → ANSI в палитре темы): код-блоки с dim-рамкой и
  меткой языка, акцентные inline-код и буллеты, bright/accent заголовки,
  перенос по ширине терминала.
- **Соответствие контракту.** Формы повторяют C# read-модели хоста
  (`ChatSessionsController`, `RunsController`, `ComputeSnapshotEndpoints`):
  camelCase, `kind`-дискриминатор частей, фильтр-DSL ранов.

## Тесты

```bash
cd agents && bun test comuki-cli   # config / client / format / signalr / sessions / commands
```

API замокан через инжектируемый `fetch` — живой сервер не нужен.

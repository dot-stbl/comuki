# Comuki — Agents (TS packages)

Три TS-пакета, два runtime'а (`pi` для воркеров, Claude Code для разрабов), одно ядро.

## Пакеты

### `comuki-agent-core` — общее ядро
Шарится обоими SDK. То, что должно быть **одинаковым** у воркера и разраба.

| Папка | Что будет |
|---|---|
| `src/events/` | Типы событий Translator ↔ Orchestrator: `StageReport`, `EscalationRequest`, `Heartbeat`, и т.д. (gRPC bidirectional stream) |
| `src/rules/` | Чтение и применение **декларативных** правил (текст) — мягкие правила, конвенции. Жёсткие (запреты) — НЕ здесь, в `pi-extensions` / `hooks` |
| `src/mcp/` | Клиент к `Comuki.Platform.Knowledge` по MCP-протоколу |
| `src/protocol/` | Формат брифа/отчёта (стык C# Orchestrator ↔ TS агент). JSON DTO |

### `comuki-worker-sdk` — воркеры
Специфика `pi` (pi-coding-agent). Запускается в контейнере воркера.

| Папка | Что будет |
|---|---|
| `src/pi-extensions/` | Адаптеры принуждения механикой pi (замки): запрет править тесты, install, push в main |
| `src/skills/` | Загрузка скиллов-рецептов из `control-plane/skills/` и проектных |

### `comuki-dev-sdk` — разрабы
Форк GSD под Claude Code (имя по роли, не по происхождению: после форка это наш код).
Сидит в IDE разработчика, не в контейнере.

| Папка | Что будет |
|---|---|
| `src/hooks/` | Те же замки, механикой Claude Code (pre-tool-use hook → блокировка Edit на test-файле) |
| `src/subagents/` | Сабагенты, переопределённые под стадии Comuki (изучатель, контракт-агент, doc-агент) |

## Почему три, а не два

- **Разные механики принуждения** — `pi-extensions` vs Claude Code `hooks` (перехват у них
  разный на уровне рантайма).
- **Одна общая база** — `comuki-agent-core` шарят оба SDK: типы событий, MCP-клиент,
  формат брифа/отчёта, чтение декларативных правил. Пишется один раз — разъехаться
  не может.
- **Замки дублируются дважды** (pi + Claude Code) — но это маленький фиксированный набор
  (запрет править тесты, install, push в main), который почти не меняется. Общее в ядре,
  специфичное в тонких адаптерах.

## Рантаймы (не наш код, берутся готовыми)

| SDK | Runtime | Где сидит |
|---|---|---|
| `comuki-worker-sdk` | **pi** (pi-coding-agent) | В контейнере воркера, headless |
| `comuki-dev-sdk` | **Claude Code** | В IDE разраба, интерактивный |

Оба рантайма — external. Наши три пакета — поверх.

## Когда что наполняется

| Папка | Phase | Что |
|---|---|---|
| `comuki-agent-core/src/events/` | 3.3 (Slice 0 Step 2) | Типы для Translator ↔ Orchestrator gRPC |
| `comuki-agent-core/src/protocol/` | 3.3 | Формат брифа/отчёта |
| `comuki-agent-core/src/mcp/` | 5 (Slice 2) | MCP-клиент к Knowledge |
| `comuki-agent-core/src/rules/` | 5 | Чтение декларативных правил через MCP |
| `comuki-worker-sdk/src/pi-extensions/` | 6 (Slice 3) | Замки (запрет тестов, install, push) |
| `comuki-worker-sdk/src/skills/` | 5–6 | Загрузчик скиллов |
| `comuki-dev-sdk/src/hooks/` | 6 | Те же замки механикой Claude Code |
| `comuki-dev-sdk/src/subagents/` | 7 (Slice 4) | Изучатель, контракт-агент, doc-агент |

`package.json` / `tsconfig.json` / bun workspace setup — Phase 2 (Stack Foundation).

## Подробнее

- `.soly/docs/comuki-project-structure.md` § 3
- `.soly/docs/comuki-decisions.md` § "Два агента под две ситуации, три пакета"
- `.soly/docs/comuki-architecture.md` § 05 (знания и правила), § 06 (workflow)

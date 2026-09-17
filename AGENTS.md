# AGENTS.md — точка входа для любого агентского харнесса

Этот файл читают Codex, Cursor, Windsurf, OpenCode, Aider и всё, что
придёт после них. **Единственный** source of truth для ориентации.

> `CLAUDE.md` — тонкий pointer сюда. Не дублируй содержание там.
> Каноничные правила — в [`.agents/rules/`](.agents/rules/).
> Планирование — в [`.agents/`](.agents/) (`STATE.md`, `ROADMAP.md`, `phases/`, `docs/`).

Общение с пользователем — **на русском**.

---

## Что это за репозиторий

**Comuki** — платформа, где ведущая модель (мозг) декомпозирует задачу и
дирижирует роем эфемерных воркеров в контейнерах. Общая база знаний и
правила по MCP. Защита от слопа — скиллы-рецепты + жёсткие анализаторы +
дизайн-система как принуждаемое правило.

Comuki **не пишет свой код сам** — это инструмент, который пишет *другие*
проекты. В этом репо нет продуктового кода клиентов, только платформа,
агентские SDK и operational UI.

Полиглот-монорепо, верхний уровень **по стеку**:

| Папка | Стек | Назначение |
|-------|------|------------|
| `platform/` | C# / .NET 10 | Оркестратор, proxy (YARP), knowledge, rules, Translator |
| `agents/` | TypeScript | `comuki-agent-core` + `comuki-worker-sdk` (pi) + `comuki-dev-sdk` (Claude Code) |
| `dashboard/` | React 19 + Vite + собственный кит (CSS Modules на токенах) | Operational UI |
| `control-plane/` | markdown / конфиги | Воркер-правила и скиллы роя (не код продукта) |
| `deploy/` | Docker Compose | postgres+pgvector, minio, nexus, victoria |
| `tests/` | C# | Unit / integration / architecture tests |
| `.agents/` | markdown | Rules, docs, phases, STATE — **агентский контур**, не путать с `agents/` |

> **`agents/` ≠ `.agents/`.** `agents/` — TS SDK-пакеты. `.agents/` —
> правила и планирование для AI-харнессов.

Подробнее: [`.agents/docs/architecture/`](.agents/docs/architecture/).

---

## Навигация

| Нужно | Читать |
|-------|--------|
| Где мы сейчас / решения | [`.agents/STATE.md`](.agents/STATE.md) |
| Фазы / roadmap | [`.agents/ROADMAP.md`](.agents/ROADMAP.md) |
| Архитектура / почему так | [`.agents/docs/architecture/`](.agents/docs/architecture/) |
| Структура репо / слои C# | [`.agents/docs/architecture/comuki-project-structure.md`](.agents/docs/architecture/comuki-project-structure.md) |
| Визуальный мир / токены | [`DESIGN.md`](DESIGN.md) + [`dashboard/src/app/styles/tokens.css`](dashboard/src/app/styles/tokens.css) |
| C# style / DI / testing | [`.agents/rules/coding/`](.agents/rules/coding/) |
| Build / commits / scripts | [`.agents/rules/process/`](.agents/rules/process/) |
| Текущая фаза (контекст) | [`.agents/phases/`](.agents/phases/) |

---

## Текущий статус (кратко)

Milestone **v1**, phase **3 complete** (design system + testing infra).
Дальше — **Phase 4: Slice 0** (вертикальный срез: один тикет через
одного воркера — pull-claim, Translator/gRPC, container lifecycle).

Смотри актуальные цифры в `.agents/STATE.md`.

---

## Critical Non-Obvious Patterns

1. **Build = gate.** `dotnet build comuki.slnx -c Debug` — warnings-as-errors,
   analyzers, format. Exit ≠ 0 → не готово. Полный контракт:
   [`.agents/rules/process/build-verification.md`](.agents/rules/process/build-verification.md).

2. **Commits:** `[hybrid] <type>(<scope>): <description>` —
   Conventional Commits + префикс `[hybrid]`. Не `[stbl]`, не `feat/` без
   type. См. [commit-format.md](.agents/rules/process/commit-format.md).
   Байлайнов модели (`Co-Authored-By: Claude`, `🤖 Generated with …`) нет
   нигде — хук `commit-msg` их вырезает, см.
   [no-ai-attribution.md](.agents/rules/process/no-ai-attribution.md).

3. **Python запрещён.** Скрипты только bun/node. См.
   [allowed-scripts.md](.agents/rules/process/allowed-scripts.md).

4. **xUnit v3 = MTP.** Тесты через `dotnet run --project <csproj>`, не
   `dotnet test` (VSTest не умеет xUnit v3 discovery).

5. **Coverage floor 70% line** (BE + FE). Не 80%+.

6. **Не запускай long-lived dev/watch/serve** из агента (`bun run dev`,
   `dotnet watch`, playwright, chromium). Убьёшь runtime харнесса.
   Собирай / тестируй в single-run режиме; dev-сервер — у пользователя.

7. **`comuki.slnx` в корне**, не внутри `platform/`. Solution folder =
   physical path. На Windows `.NET 10` `dotnet sln add --solution-folder`
   иногда схлопывает пути — после add проверяй `.slnx` глазами.

8. **Визуальный мир — [`DESIGN.md`](DESIGN.md) в корне.** Палитра, темы,
   типографика, формы, статусы — только оттуда; токены в
   [`dashboard/src/app/styles/tokens.css`](dashboard/src/app/styles/tokens.css).
   Здесь их **не пересказываем** — два пересказа разъезжаются.
   UI-библиотеки нет: shadcn вырезан целиком, кит `@/shared/ui` —
   единственный источник примитивов.
   **Tailwind при этом остаётся и удалению не подлежит.** Утилит-классов
   в компонентном коде нет — и не заводи, — но слой сборки живой:
   `@tailwindcss/vite` в [`dashboard/vite.config.ts`](dashboard/vite.config.ts),
   `@import "tailwindcss"` и блок `@theme inline` в
   [`dashboard/src/index.css`](dashboard/src/index.css), `twMerge` внутри
   `cn()` (`dashboard/src/shared/lib/utils.ts`). `@theme inline` — мост,
   которым токены видны Tailwind-слою; вырезали уже — вёрстка плывёт.
   Следствие, на котором спотыкаются аудиты: токены, упомянутые только
   внутри `@theme inline` (`--sidebar-*`, `--chart-*`, `--*-foreground`,
   `--input`, `--card-*`, `--popover-*`), **живые** — их потребитель сам
   этот блок, а не компонент. Мёртвым токен считается, лишь когда на него
   нет ссылок и в `index.css`.
   [`.agents/docs/design-system/`](.agents/docs/design-system/) — **прежний**
   визуальный мир, историческая справка, не источник решений.

9. **Port pool 17000–17200 — обязательно.** Случайные порты запрещены.
   Dashboard = **17173** (`strictPort`). Таблица и резервы:
   [`.agents/rules/process/ports.md`](.agents/rules/process/ports.md).

---

## Команды

```bash
# Backend
dotnet build comuki.slnx -c Debug
dotnet format comuki.slnx --severity hidden
dotnet run --project tests/unit/Comuki.Engine.Orchestration.Unit.StatusMachine

# Frontend (http://localhost:17173 — port pool, not 5173)
cd dashboard && bun install
cd dashboard && bun run typecheck && bun run lint && bun run test
cd dashboard && bun run build
# User only: cp .env.example .env && bunx vite   → :17173
```

Не стартуй `bun run dev` / `dotnet run` (host) / watch из агентской сессии.

---

## Правила — куда править

- Проектные: `.agents/rules/**` (этот репо).
- User-global: `~/.agents/rules/` (не дублировать сюда).
- `.claude/rules/` — только тонкие ссылки, без копий текста.
- Новый rule → frontmatter обязателен (`RULES-FORMAT.md`).

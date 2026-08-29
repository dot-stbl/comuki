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
| `dashboard/` | React 19 + Vite + shadcn | Operational UI |
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
| Design system | [`.agents/docs/design-system/`](.agents/docs/design-system/) |
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

8. **Design tokens** — Comuki palette (slate-blue + cool-black), IBM Plex
   Mono, `--radius: 0.375rem`, 6 status tokens. Источник:
   `.agents/docs/design-system/Comuki Design System.md`.

---

## Команды

```bash
# Backend
dotnet build comuki.slnx -c Debug
dotnet format comuki.slnx --severity hidden
dotnet run --project tests/Comuki.Platform.Orchestration.Unit.Lease

# Frontend
cd dashboard && bun install
cd dashboard && bun run typecheck && bun run lint && bun run test
cd dashboard && bun run build
```

Не стартуй `bun run dev` / `dotnet run` (host) / watch из агентской сессии.

---

## Правила — куда править

- Проектные: `.agents/rules/**` (этот репо).
- User-global: `~/.agents/rules/` (не дублировать сюда).
- `.claude/rules/` — только тонкие ссылки, без копий текста.
- Новый rule → frontmatter обязателен (`RULES-FORMAT.md`).

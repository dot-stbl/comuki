---
description: формат и иерархия правил в проекте — где что лежит, как грузится, как добавлять новое
always: true
---

# Формат правил

В проекте **две** системы правил. Этот файл — карта между ними: что куда
класть, как грузится, как не дублировать.

Каноническое дерево правил — `.agents/rules/`. `.claude/rules/` — **тонкие
указатели** на канон, без дублирования содержания. Корневой вход для любого
агентского харнесса — `AGENTS.md`; `CLAUDE.md` только ссылается на него.

## 1. Где что лежит

### `.agents/rules/<category>/*.md` — канонические правила

Файлы с frontmatter. Грузятся:
- по `globs` (применяется к указанным файлам),
- по `always: true` (всегда в контексте).

Используются для code-style, process, тестирования, архитектуры.

### `.claude/rules/*.md` — указатели для Claude-based харнессов

Тонкие файлы-ссылки на `.agents/rules/`. **Не дублируют** содержание.

> **Hard rule: дублирование содержания между `.agents/` и `.claude/` запрещено.**
> Если правило развёрнуто в `.agents/rules/coding/X.md` — в `.claude/rules/`
> только ссылка, не копия.

### `AGENTS.md` / `CLAUDE.md`

| Файл | Роль |
|------|------|
| `AGENTS.md` | **Единственный** source of truth для ориентации агента |
| `CLAUDE.md` | Тонкий pointer → `AGENTS.md` (для Claude Code / Claude-based) |

## 2. Frontmatter — REQUIRED формат

```yaml
---
description: one-line lowercase — что это правило ограничивает
globs: ["**/*.py", "**/requirements.txt"]   # опционально
priority: high | medium | low                # опционально
interactive: false                           # true = только для интерактивного LLM
always: false                                # true = обходить glob-проверку
---
```

**`description` — всегда lowercase**, разделитель `-` или `.`.

## 3. Иерархия (от высшего приоритета к низшему)

1. `.agents/rules.local/` (per-project, gitignored, личные overrides)
2. `.agents/rules/` (per-project, коммитится в репу)
3. `.claude/rules/` (коммитится, тонкие указатели)
4. `~/.agents/rules/` (user-global)
5. `~/.claude/rules/` (user-global)

При коллизии — выигрывает правило с **меньшим** номером.

## 4. Структура `.agents/rules/` в этом репо

```
.agents/rules/
├── coding/
│   ├── CODING-RULES.md          # C# code style
│   ├── FRAMEWORK-RULES.md       # EF Core, ASP.NET, MEL, Gridify
│   ├── PROJECT-STRUCTURE.md     # layers, naming, layout
│   ├── DI-RULES.md              # DI, IOptions, composition root
│   ├── TESTING-RULES.md         # xUnit v3 + Shouldly + NSubstitute + Testcontainers
│   ├── ANALYZERS.md             # Roslynator / Meziantou / NetAnalyzers
│   ├── frontend-construct-rules.md
│   └── RULES-FORMAT.md          # ← этот файл
└── process/
    ├── build-verification.md
    ├── commit-format.md         # [hybrid] + Conventional Commits
    ├── pre-commit.md
    ├── worker-audit.md
    └── allowed-scripts.md       # Python banned; bun/node only
```

Планирование (не rules): `.agents/STATE.md`, `.agents/ROADMAP.md`,
`.agents/phases/`, `.agents/docs/`, `.agents/HANDOFF.json`.

## 5. Как добавить новое правило

1. Создать `.agents/rules/<category>/<name>.md` (lowercase kebab-case).
2. **Обязательно** frontmatter с `description:`.
3. Body: ToC + `##` / `###` + Good / Bad + Related.
4. Если всегда в контексте — `always: true`; если узкое — `globs: [...]`.
5. При необходимости — тонкий pointer в `.claude/rules/` (только ссылка).

Без frontmatter файл в `.agents/rules/` **не загрузится**.

## 6. Reload

После правки `.agents/rules/` — перезапуск сессии подхватит изменения.
Для soly-aware агентов: `/rules reload`.

## Related

- `AGENTS.md` — точка входа
- `process/build-verification.md` — build gate
- `process/worker-audit.md` — self-audit gate
- `process/commit-format.md` — `[hybrid]` commits

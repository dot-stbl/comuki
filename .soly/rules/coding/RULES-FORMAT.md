# Формат правил в этом проекте

В проекте **две** системы правил, которые дополняют друг друга. Этот файл —
карта между ними: что куда класть, как они грузятся, как не дублировать.

## 1. `.claude/rules/*.md` — основная документация

Большие MD-файлы-мануалы **без frontmatter**, в стиле подробного руководства
(см. `CODING-RULES.md`, `FRAMEWORK-RULES.md`, `PROJECT-STRUCTURE.md`,
`TESTING-RULES.md`).

Когда класть сюда:
- Код-стайл, naming conventions, паттерны
- Детальные how-to для конкретных библиотек (EF Core, ASP.NET Core, MS.Ext.Logging)
- Архитектурные правила и слои
- Структура проекта, naming, layout
- Тестовый стэк и подход (xUnit v3 + Shouldly + NSubstitute + Testcontainers)

Стиль:
- Заголовки `##` / `###` с якорями
- Good / Bad примеры
- Таблицы "когда что"
- Code blocks с указанием языка
- Связи на другие правила внизу ("См. также")
- **Без frontmatter** — это обычный markdown

## 2. `.soly/rules/<category>/*.md` — контекстные правила

Файлы с **soly frontmatter**, загружаются по `globs` или при `always: true`.
Подходят для:
- Process / workflow правил (когда что запускать, dual-build, pre-commit)
- Constraints ("запрет X", "обязательно Y", "не более N")
- Контекстно-зависимые правила, которые активируются только когда промпт
  ссылается на файлы из `globs`
- Бланкетные запреты, которые должны быть в эффекте всегда (`always: true`)

### Формат frontmatter

```yaml
---
description: One-line — что это правило ограничивает
globs: ["**/*.py", "**/requirements.txt"]   # опционально: применяется только к этим файлам
priority: high | medium | low                # подсказка для упорядочивания
interactive: false                           # true = только для интерактивного LLM, не для subagent-воркеров
always: false                                # true = обходить glob-проверку, применять всегда
---
```

### Доступные категории

| Категория | Когда использовать |
|-----------|---------------------|
| `architecture` | Слои, зависимости, layering, bounded contexts |
| `code-style` | Naming, форматирование, паттерны (если узкие, не как большой мануал) |
| `testing` | Когда писать тесты, test stack, naming (если узкие) |
| `process` | Workflow, build, commits, dual-build, pre-commit hooks |
| `performance` | Hot-path, allocations, кеширование, профилирование |
| `security` | Auth, secrets, sensitive data, OWASP |

### Иерархия (от высшего приоритета к низшему)

1. `.soly/rules.local/` (per-project, gitignored, личные override'ы)
2. `.soly/rules/` (per-project, **коммитится в репу**)
3. `.claude/rules/` (коммитится, грузится всеми агентами)
4. `~/.soly/rules/` (user-global)
5. `~/.claude/rules/` (user-global)

При коллизии — выигрывает правило с **меньшим** номером в иерархии.
Например, личный override в `.soly/rules.local/` бьёт всё ниже.

## Где лежит что (текущее состояние)

```
.claude/rules/
├── CODING-RULES.md          # C# code style (naming, primary ctor, sealed, async, var, braces)
├── FRAMEWORK-RULES.md       # EF Core, ASP.NET Core, MS.Ext.Logging, Gridify
├── PROJECT-STRUCTURE.md     # layers, naming, layout, testing structure
├── TESTING-RULES.md         # xUnit v3 + Shouldly + NSubstitute + Testcontainers
└── RULES-FORMAT.md          # ← этот файл

.soly/rules/
└── process/
    ├── build-verification.md    # когда и как билдить BE/FE, dual-build, pre-existing errors
    └── allowed-scripts.md       # запрет Python, Node.js/bun only
```

## Принцип: дублирование запрещено

Если правило уже развёрнуто в `.claude/rules/CODING-RULES.md` — **не дублировать**
его в `.soly/rules/`. Наоборот:

| Если правило... | Класть в |
|------------------|----------|
| Учит, развёрнутое, 100+ строк, ссылаются агенты в любом контексте | `.claude/rules/` |
| Короткое, контекстное, "когда касается X — делай Y" | `.soly/rules/<category>/` |
| Бланкетный запрет / обязанность, должен быть в эффекте всегда | `.soly/rules/<category>/` с `always: true` |
| Большое + должно быть в эффекте при касании конкретных файлов | TL;DR в `.soly/rules/`, полная версия в `.claude/rules/` |

## Как добавить новое правило

См. `soly help add-rule` или (кратко):

### В `.claude/rules/` (большой мануал)

1. Создать `<NAME>-RULES.md` рядом с существующими
2. Следовать стилю существующих: ToC, ## / ###, Good / Bad, "См. также" внизу
3. **Без frontmatter** — обычный markdown
4. Добавить ссылку в этот файл (RULES-FORMAT.md) в секцию "Где лежит что"

### В `.soly/rules/<category>/` (контекстное)

1. Создать `.soly/rules/<category>/<name>.md`
2. **Обязательно** frontmatter с `description:` (одна строка, понятная)
3. Body: правило + Good / Bad
4. Если не уверен в категории — посмотри таблицу выше
5. Если правило бланкетное (не зависит от файлов) — добавить `always: true`
6. Если правило узкое и про конкретные файлы — добавить `globs: [...]`

Без frontmatter файл в `.soly/rules/` **не загрузится**.

## Reload

После создания / редактирования правил в `.soly/rules/` обычно нужно
выполнить `/rules reload` (если используется soly-aware агент).
Для Claude Code, Cursor, Windsurf и т.п. — перезапуск сессии подхватит
новые правила автоматически.

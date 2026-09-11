---
description: no ai authorship bylines in commits, prs, code comments or docs — enforced by the commit-msg hook
priority: high
always: true
---

# No AI attribution

Авторство в этом репозитории принадлежит человеку. Модель — инструмент, а не
соавтор: у неё нет ответственности за изменение, её нельзя спросить через
полгода «почему так», и `git log --author` по ней ничего не значит.

Поэтому **байлайнов модели нет нигде**: ни в коммитах, ни в описаниях PR, ни в
комментариях в коде, ни в документации.

Для коммитов правило **машинно проверяемое**: хук `commit-msg` вырезает такие
строки сам (`scripts/commit-lint.mjs`). См.
[`pre-commit.md`](pre-commit.md) — установка хуков.

## Что запрещено

| Форма | Пример |
|-------|--------|
| `Co-Authored-By:` с моделью в имени | `Co-Authored-By: Claude <noreply@anthropic.com>` |
| `Co-Authored-By:` с вендорским доменом в почте | `Co-Authored-By: Jane Doe <jane@anthropic.com>` |
| Футер «сгенерировано» | `🤖 Generated with [Claude Code](https://claude.com/claude-code)` |
| Проза об авторстве | `Co-authored by Claude`, `Written with Codex`, `Built by Cursor`, `Made with Gemini` |
| Трейлеры «ассистировано» | `Assisted-By: …`, `AI-generated`, `AI-assisted`, `AI-authored` |
| Вендорские адреса инлайном | `noreply@anthropic.com` в любой строке |

Список вендоров, который считается байлайном: `claude`, `anthropic`,
`chatgpt`, `openai`, `gpt-`, `codex`, `copilot`, `cursor`, `gemini`,
`opencode`, `devin`, `aider`, `windsurf`. Точный источник правды —
`scripts/commit-lint.mjs` (`AI_VENDORS`, `WHOLE_LINE_PATTERNS`,
`INLINE_PATTERNS`), не этот список.

## Где применяется

| Место | Как |
|-------|-----|
| Commit message | Хук `commit-msg` вырезает строку и печатает предупреждение в stderr. Коммит **не падает** — трейлер не должен стоить человеку коммита |
| Описание PR | Вручную. Футера `🤖 Generated with …` быть не должно |
| Комментарии в коде | Вручную. `// написано Claude` — мусор, удалять |
| Документация | Вручную. Не подписывать `.agents/**`, `README.md` и прочее моделью |
| Существующие коммиты | `node scripts/commit-lint.mjs --range <A..B>` — ручной аудит. Здесь байлайн это **ошибка**, а не автофикс: чужой запушенный коммит не переписывают |

## Отключить на источнике

Лучше не вырезать, а не порождать. Claude Code умеет не добавлять байлайн сам:

```json
// .claude/settings.json (в репе, коммитится)
{ "includeCoAuthoredBy": false }
```

Этот ключ гасит и трейлер `Co-Authored-By`, и футер `🤖 Generated with` в PR.
`@comuki/dev-sdk` проставляет его и в пользовательский `~/.claude/settings.json`:

```bash
bun agents/comuki-dev-sdk/src/install.ts            # поставить
bun agents/comuki-dev-sdk/src/install.ts --uninstall # убрать обратно
```

## Что **не** запрещено

Правило про **атрибуцию авторства**, а не про слово. Claude Code — продукт,
с которым Comuki интегрируется; упоминать его по делу нужно и нормально.

```md
✅ Comuki оркестрирует пул воркеров Claude Code через stream-json.
✅ Translator разбирает вывод `claude -p --output-format stream-json`.
✅ `.agents/docs/architecture/*` — целые разделы про Claude Code, Codex CLI, OpenCode.
✅ chore(deps): bump @anthropic-ai/sdk to 0.40.0
✅ Co-Authored-By: Иван Петров <ivan@hybrid.ai>   ← живой человек
```

```md
❌ Co-Authored-By: Claude <noreply@anthropic.com>
❌ 🤖 Generated with [Claude Code](https://claude.com/claude-code)
❌ // этот парсер написан Claude, осторожно
❌ Assisted-By: Cursor
```

Разница простая: **о чём код** — можно; **кто автор** — только человек.

## Обход

```bash
git commit --no-verify
```

Обходит оба хука. Для этого правила уместно ровно в одном случае: строка тела
коммита легально начинается с `Generated with …` и хук съел её ошибочно.
Перепиши строку, а не обходи хук.

## Related

- [`commit-format.md`](commit-format.md) — формат subject, который проверяет тот же линтер
- [`pre-commit.md`](pre-commit.md) — установка и обход git-хуков
- `scripts/commit-lint.mjs` — реализация, единственный source of truth по паттернам
- `scripts/commit-lint.test.mjs` — `node --test`, покрывает каждый паттерн
- `scripts/hooks/commit-msg` — сам хук
- `.claude/settings.json` — `includeCoAuthoredBy: false`

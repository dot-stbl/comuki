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
| CI (сервер, автоматически) | `scripts/ci/no-ai-attribution.mjs --range <base>..<head>` — джоба в `.github/workflows/ci.yml` / GitLab. Байлайн здесь — **жёсткая ошибка** (exit 1), не автофикс, и не обходится `--no-verify` |

## Слои защиты

Правило применяется на двух независимых слоях — локальный обходится, серверный нет.

| Слой | Где | Что проверяет | Обходимо? |
|------|-----|---------------|-----------|
| 1 — commit-msg (локально) | `scripts/commit-lint.mjs --file` через `scripts/hooks/commit-msg` | Текст сообщения коммита — вырезает трейлер/фрагмент и предупреждает в stderr, коммит не блокируется | Да — `git commit --no-verify` |
| 2 — CI (сервер) | `scripts/ci/no-ai-attribution.mjs --range <base>..<head>` | Каждый коммит в диапазоне — subject/body/trailers + имя и почта author/committer (вендорские токены); опционально `--text-file` для описания PR/MR | Нет — джоба падает, `--no-verify` тут не действует |

GitHub: `.github/workflows/ci.yml`, job на `pull_request` (`base.sha..head.sha` плюс описание PR) и на `push` (`before..after`). GitLab: `deploy/hybrid/**` — отдельный оверлей; точный job-сниппет лежит в `scripts/ci/README.md`, координатор вставляет его туда руками.

Один источник правды на паттерны — `scripts/commit-lint.mjs` экспортирует `AI_VENDORS`, `AI_EMAIL_DOMAINS`, `alternation`, `stripAttribution`; `scripts/ci/no-ai-attribution.mjs` импортирует их, а не дублирует.

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
- `scripts/ci/no-ai-attribution.mjs` — server-side layer, reuses this file's patterns via commit-lint.mjs's exports

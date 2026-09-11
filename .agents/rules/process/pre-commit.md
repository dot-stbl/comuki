---
description: repo git hooks — pre-commit runs dotnet format whitespace, commit-msg gates the commit message. Install once per clone, bypass via --no-verify.
globs: ["scripts/hooks/**", "scripts/install-hooks.sh", "scripts/commit-lint.mjs"]
always: true
---

# Git hooks — pre-commit + commit-msg

Repo-level git hooks (`scripts/hooks/`) версионируются в репе и ставятся
per-clone. Их два, и они про разное:

| Hook | Когда | Что делает | Падает? |
|------|-------|------------|---------|
| `pre-commit` | до редактора сообщения | `dotnet format whitespace` по staged `.cs` | да, если есть format drift |
| `commit-msg` | после написания сообщения | вырезает AI-байлайны, проверяет subject | да, но только на кривом subject |

Обход обоих — `git commit --no-verify`.

## Install (once per clone)

```bash
./scripts/install-hooks.sh
```

Ставит `core.hooksPath = scripts/hooks` (путь **относительный** — linked
worktrees под `.agents/worktree/` используют каждый свою копию) и делает
`chmod +x` на всех файлах-хуках. Инсталлер проходит по директории циклом —
добавить новый хук = просто положить файл рядом, инсталлер менять не надо.

## `pre-commit` — dotnet format whitespace

Проверяет **только whitespace** (indent, line endings, blank lines) по
`.editorconfig` — быстро, косметика, без семантического риска.

| Check | Where |
|-------|-------|
| Whitespace (indent, line endings, blank lines) per `.editorconfig` | **pre-commit hook** |
| Full `dotnet format` (whitespace + style + analyzers) | **CI** — `process/build-verification.md` |
| Build with extended analyzer rules | **CI** — `process/build-verification.md` |

Почему только whitespace:

- Whitespace — чистая косметика, нет семантического риска, ~1s на коммит
- Style rules — бывают false positives, нужен review
- Analyzer fixes — могут менять семантику, слишком агрессивно для pre-commit

Если `dotnet` нет в PATH — хук пропускается с предупреждением.

## `commit-msg` — commit hygiene gate

Тонкая обёртка над `node scripts/commit-lint.mjs --file "$1"` (plain ESM, ноль
зависимостей). Если `node` нет в PATH — пропускается с предупреждением:
сломанный гейт не должен блокировать коммит.

Две разные реакции, и это осознанно:

- **AI-байлайн → вырезается на месте**, каждая удалённая строка печатается в
  stderr, коммит проходит. Трейлер не должен стоить человеку коммита.
  См. [`no-ai-attribution.md`](no-ai-attribution.md).
- **Кривой subject → hard fail.** Это может починить только автор.
  См. [`commit-format.md`](commit-format.md).

Проверить сообщение, ничего не коммитя:

```bash
echo '[hybrid] feat(api): add the thing' | node scripts/commit-lint.mjs --stdin
node scripts/commit-lint.mjs --range master..HEAD   # аудит существующих коммитов
node --test scripts/commit-lint.test.mjs            # тесты линтера
```

## Skip

```bash
git commit --no-verify
```

Пропускает **оба** хука. Уместно когда:

- Format failure — pre-existing проблема, не связанная с твоим изменением
- Ты в середине рефакторинга и переформатируешь следующим коммитом
- Hotfix, который CI всё равно поймает
- Хук ошибочно съел легальную строку тела (перепиши строку, а не привыкай к флагу)

## Requirements

- `dotnet` CLI в PATH для `pre-commit` (иначе skip с предупреждением)
- `node` в PATH для `commit-msg` (иначе skip с предупреждением)
- `comuki.slnx` в корне репозитория
- Git Bash на Windows (любая установка Git for Windows)

## Uninstall

```bash
git config --unset core.hooksPath
```

## Related

- `scripts/hooks/pre-commit` — хук форматирования
- `scripts/hooks/commit-msg` — хук сообщения коммита
- `scripts/commit-lint.mjs` — линтер сообщения (subject + байлайны)
- `scripts/hooks/README.md` — установка и обход
- `scripts/install-hooks.sh` — one-line installer
- [`commit-format.md`](commit-format.md) — формат subject
- [`no-ai-attribution.md`](no-ai-attribution.md) — байлайны модели
- [`build-verification.md`](build-verification.md) — CI-сторона той же проверки формата
- `.agents/rules/coding/ANALYZERS.md` — analyzer packages (Roslynator, Meziantou)

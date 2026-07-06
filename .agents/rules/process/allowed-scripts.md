---
description: Python запрещён в любом виде. Скрипты, утилиты, devtools, data processing — только на Node.js (bun/node)
globs: ["**/*.py", "**/*.pyi", "**/requirements.txt", "**/requirements-*.txt", "**/pyproject.toml", "**/Pipfile", "**/Pipfile.lock", "**/setup.py", "**/setup.cfg", "**/poetry.lock", "**/uv.lock", "**/conda*.yml", "**/environment.yml"]
priority: high
always: true
interactive: false
---

# Allowed Scripts & Tools

## 🚫 Hard rule — прочитай первым

**Python запрещён в этом проекте. Без исключений и без «а может, всё-таки».**

Если задача попадает в **любую** из этих категорий — **сразу пиши на
`bun script.ts` / `node script.mjs` / `bun add <pkg>`**, не предлагай
Python и не спрашивай разрешения:

- data munging / ETL / миграция данных
- batch-операции с файлами (переименование, перемещение, конвертация)
- одноразовые скрипты / devtools / CI-хелперы
- парсинг CSV / JSON / YAML / Excel
- скрапинг / API-автоматизация / HTTP-клиенты
- генерация тестовых данных / seed data
- работа с архивами / бинарными форматами

**Запрещено предлагать / устанавливать / запускать:**
`python`, `python3`, `pip`, `pip3`, `pipx`, `uv`, `poetry`, `pyenv`,
`conda`, `requirements.txt`, `pyproject.toml`, `Pipfile`, `.py`-файлы,
`#!/usr/bin/env python`.

**Запрещено произносить в ответах** (фразы, которые триггерят LLM
предложить Python — избегай их как чумы):
- «Может, проще на Python?»
- «Python тут хорошо подойдёт»
- «Через pip можно установить...»
- «Можно одной строкой на Python: ...»

**Если не нашёл bun/node-эквивалент** — **спроси пользователя явно**,
не угадывай. «Проще написать на Python» — **не аргумент**.

---

В этом проекте **запрещён** Python и любые Python-инструменты в любом виде.
Все скрипты, утилиты, CI-нотчи, data processing, миграции данных, devtools,
one-off автоматизация — только на **Node.js** через `bun` (в FE) или `node`
(в остальном проекте).

## Почему

- В проекте один зафиксированный рантайм для скриптов — **JavaScript /
  TypeScript** через bun
- `packageManager: "bun@1.3.10"` зафиксирован в `src/client-side/package.json`
- Установка Python (`pyenv`, `venv`, `system pip`, `uv`, `poetry`) — лишний
  moving part, который ломается по-разному на Windows / Linux / macOS и
  плодит environment-specific баги
- Python-скрипты в репе тянут за собой `requirements.txt` / `pyproject.toml`
  / venv, которые **не управляются** ни одним из существующих lock-файлов
  (`bun.lockb`, `package-lock.json`, `Directory.Packages.props`)

## Что под запретом

| Категория | Примеры |
|-----------|---------|
| `.py` / `.pyi` файлы в репе | `scripts/fetch_data.py`, `tools/migrate.py`, `src/utils/foo.py` |
| Манифесты Python | `requirements.txt`, `requirements-dev.txt`, `pyproject.toml`, `Pipfile`, `Pipfile.lock`, `setup.py`, `setup.cfg`, `poetry.lock`, `uv.lock` |
| Conda-манифесты | `environment.yml`, `conda.yml` |
| Установка Python-пакетов | `pip install`, `pip3 install`, `pipx install`, `uv add`, `poetry install`, `conda install` |
| Запуск через интерпретатор | `python script.py`, `python3 script.py`, `uv run script.py`, `poetry run script.py` |
| Inline Python в shell | `python -c "..."`, heredoc `python <<EOF ... EOF` |
| Shebang в файлах | `#!/usr/bin/env python`, `#!/usr/bin/python3` |
| Утилиты, написанные на Python | `yq` (если Python-версия), `httpie` (если не бинарь), `cookiecutter` (если не wheel) |

## Что использовать вместо

| Задача | Решение в Node.js |
|--------|-------------------|
| Одноразовый скрипт (data munging, миграция) | `bun run script.ts` или `bun script.ts` (bun нативно) |
| Утилита для FE | Добавить в `scripts:` блок `src/client-side/package.json` |
| Утилита для BE / общая | Создать локальный `package.json` рядом со скриптом, заполнить через `bun init -y` |
| Dev-тулинг для backend | .NET CLI: `dotnet run --project ...` или `dotnet script` (если установлен) |
| Code-gen / API client | `bun run generate-api` (Kubb) |
| Линтинг / форматирование | `biome` (FE), `dotnet format` (BE) |
| Парсинг JSON / YAML / CSV | npm: `js-yaml`, `papaparse`, `csv-parse` |
| HTTP-запросы | npm: `axios`, `undici`, `node-fetch` |
| Запуск shell-команд | `node:child_process` (через `exec`/`spawn`) |
| Работа с файлами / FS | `node:fs/promises` (нативно) |
| Архивирование | npm: `archiver`, `yauzl` |
| CSV / Excel | npm: `xlsx`, `papaparse` |

## Запуск скриптов вне `src/client-side/`

Если скрипт не относится к FE (например, миграция данных, CI-хелпер),
создать **локальный** `package.json` рядом с ним:

```
scripts/
└── migrate-foo/
    ├── package.json          # { "name": "migrate-foo", "type": "module" }
    ├── tsconfig.json         # опционально
    └── migrate.ts            # сам скрипт
```

В `package.json`:

```json
{
  "name": "migrate-foo",
  "type": "module",
  "private": true
}
```

Запуск — `bun migrate.ts` из этой папки. Bun подхватит локальный
`package.json` и `tsconfig.json` без необходимости в `node_modules`
(для проектов без зависимостей).

Если нужны npm-пакеты — `bun add <pkg>` создаст `bun.lockb` локально.
**Глобальный** `package.json` корня проекта для этого **не использовать** —
только локальный рядом со скриптом.

## Исключения (явно разрешено)

- **`python` / `python3` как runtime для НЕ-Python бинарников** (например,
  `python -m http.server` для статики) — допустимо как системная команда,
  но **не** для запуска наших `.py` файлов
- **Зависимости транзитные** — если какой-то NuGet или npm-пакет внутри
  дёргает Python (некоторые ML-тулы, gRPC-плагины), это не наша зона
  ответственности
- **Сторонние `.py` файлы** в `node_modules/` (зависимости npm) — вне нашего
  контроля, не редактируются

## Если задача требует Python

Стоп → **обсудить с пользователем** явно:

1. Действительно ли задача не решается через `bun` / `node` (нет npm-эквивалента
   в разумных пределах)?
2. Если да — какой именно пакет нужен и **почему** нельзя обойтись?
3. Где будет жить? Если вынужденно — в отдельной подпапке с **локальным**
   `package.json` и `bun`-рантаймом, **не** `requirements.txt`.

❌ "Проще написать на Python" — **не аргумент**. Эквивалент на node/bun
почти всегда есть, либо можно вызвать существующий .NET-тулинг через
`dotnet run` / `dotnet script`.

## Detection — проверка перед коммитом

```bash
# 1. Python-файлы в staged
git diff --cached --name-only | grep -E '\.(py|pyi)$' \
  && echo "❌ Python file in staged changes" \
  || echo "✅ No Python files staged"

# 2. Python-манифесты в staged
git diff --cached --name-only | grep -E '(requirements.*\.txt|pyproject\.toml|Pipfile|setup\.(py|cfg)|poetry\.lock|uv\.lock|conda.*\.yml|environment\.yml)' \
  && echo "❌ Python manifest in staged changes" \
  || echo "✅ No Python manifests staged"

# 3. Python-shebang в любых staged файлах
git diff --cached | grep -E '^(\+|\-).*#!.*python' \
  && echo "❌ Python shebang found" \
  || echo "✅ No Python shebang"
```

Если хоть одна проверка падает — **не коммитить**, переделать на Node.js
или обсудить с пользователем.

## Good / Bad

```typescript
// ✅ Correct — bun/TS-скрипт для CSV-to-JSON
// scripts/csv-to-json.ts
import { readFileSync, writeFileSync } from 'node:fs';

const csv = readFileSync('input.csv', 'utf8');
const rows = csv.split('\n').map((line) => line.split(','));
writeFileSync('output.json', JSON.stringify(rows, null, 2));
```

```bash
# Запуск
$ bun scripts/csv-to-json.ts
```

```python
# ❌ Wrong — тот же скрипт на Python
# scripts/csv-to-json.py
import csv
import json
# ...
```

```bash
# ❌ Wrong — установка через pip
$ pip install pandas
$ python scripts/transform.py
```

```bash
# ❌ Wrong — Python-манифест в репе
# requirements.txt
pandas>=2.0
numpy>=1.24
```

```bash
# ❌ Wrong — shebang в скрипте
#!/usr/bin/env python
print("hello")
```

## Связанные правила и файлы

- `AGENTS.md` § Critical Non-Obvious Patterns
- `.claude/rules/CODING-RULES.md` — code style для .NET
- `.claude/rules/PROJECT-STRUCTURE.md` § 9 — layer dependencies
- `.claude/rules/RULES-FORMAT.md` — формат и иерархия правил
- `src/client-side/package.json` — `packageManager: "bun@1.3.10"`

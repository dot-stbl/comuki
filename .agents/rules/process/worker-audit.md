---
description: Mandatory self-audit gate for worker subagents — run after writing code, before git commit. Catches analyzer violations, cross-checks against loaded rules, fills rule gaps via analyzer-coach skill.
priority: high
---

# Worker self-audit gate

После того как ты написал код, и **до** `git commit` — ОБЯЗАТЕЛЬНО прогон
этого gate. Цель: поймать нарушения правил которые ты внёс, и
обнаружить пробелы где существующих правил не хватает.

Skipping = полагаться на CI чтобы поймать то что ты пропустил. Это
противоположно self-verification.

## Шаги (по порядку)

### 1. Mechanical: build + analyzer warnings

```bash
dotnet build comuki.slnx -c Debug -p:EnforceExtendedAnalyzerRules=true --nologo
```

**Любой warning = failure.** В этом проекте `TreatWarningsAsErrors=true`
глобально (см. `Directory.Build.props`), так что build падает
автоматически. Если не падает — warning ≥ intended level всё равно
чини.

**Чини код, не подавляй warning.** Документированный escape hatch —
только с baseline-issue (см. `coding/ANALYZERS.md` §"Что делать при новых
warnings"):

- `#pragma warning disable` — только с комментарием-обоснованием + restore
- `dotnet_diagnostic.X.severity = none` — **запрещено** (это отключение
  правила, а не enforcement)
- `<NoWarn>` в csproj — только с записью в baseline-issue

### 2. Convention: cross-check diff против загруженных rules

Ты уже имеешь в контексте эти rules (load order ниже на случай если
не помнишь что у тебя в system prompt):

- `coding/CODING-RULES.md` — naming, primary ctor, sealed, async, var, braces
- `coding/FRAMEWORK-RULES.md` — EF Core, ASP.NET Core, MS.Ext.Logging, Gridify
- `coding/ANALYZERS.md` — analyzer packages, severity в editorconfig
- `coding/PROJECT-STRUCTURE.md` — слои, нейминг, layout
- `coding/TESTING-RULES.md` — xUnit + Shouldly + NSubstitute + Testcontainers
- `process/build-verification.md` — 0 warnings перед commit
- `process/commit-format.md` — Conventional Commits 1.0.0

**Пройдись по diff'у и для каждого релевантного rule проверь
compliance.** «Я вроде нормально написал» — это не проверка. Реально
прочитай правило, найди в diff'е место, убедись что соответствует.

Частые точки внимания:
- Public методы без `<summary>` (CODING-RULES §10)
- Braces без скобок (CODING-RULES §9)
- Async метод без `Async` суффикса (CODING-RULES §1, VSTHRD200)
- `_privateField` (CODING-RULES §1 — convention)
- `var` vs explicit type (CODING-RULES §7)
- `IReadOnlyCollection` vs `List` в public API (CODING-RULES §12)
- `throw ex;` вместо `throw;` (FRAMEWORK-RULES / analyzer-coach cookbook)
- Commit message format (commit-format.md)

### 3. Rule gaps: вызови `analyzer-coach` skill

Если ты нашёл в diff'е style issue, который:
- Не enforced ни одним rule (analyzer + .editorconfig + convention docs)
- Скорее всего повторится (не разовый случай)

→ **Запусти `analyzer-coach` skill.** Он предложит один из вариантов:

| Куда | Когда |
|---|---|
| `.editorconfig` (через `dotnet_diagnostic.RCS####.severity`) | Issue покрывается standard analyzer (Roslynator/CA/MA/VSTHRD) |
| `coding/CODING-RULES.md` или `coding/FRAMEWORK-RULES.md` | Convention/pattern, нет standard rule |
| **Custom analyzer** в `platform/src/generation/Comuki.Platform.Code.Roslyn/` (правила `CMK####`) | Project-specific, code-level check даст больше чем convention |
| "Not analyzable" verdict | Это правда convention, обсуди в code review |

Custom analyzer path конкретно:
- Проект: `platform/src/generation/Comuki.Platform.Code.Roslyn/`
- Convention: один файл на правило, sealed class, public const `DiagnosticId` = `CMK####`
- Severity задаётся в `.editorconfig` (`dotnet_diagnostic.CMK0001.severity = error`)
- Перед добавлением проверь что issue не покрыт standard analyzer'ом

Примени proposal, прогони шаг 1 ещё раз чтобы убедиться что ничего не
сломал.

### 4. Loop до чистого состояния

Если шаг 1 или 2 находит violations — fix и перепрогон обоих. **Max 3
итерации** чтобы не уйти в infinite loop на genuine conflicts. Если
после 3 итераций что-то всё ещё не проходит — опиши проблему в
completion report и попроси parent решить (не коммить с warning).

### 5. Только после этого commit

Commit только когда:
- Шаг 1 passes (0 warnings)
- Шаг 2 не нашёл violations
- Rule gaps из шага 3 либо resolved, либо явно описаны в report

**Никаких `--no-verify`** для обхода pre-commit hook. Hook ловит
whitespace — это легитимная проверка, не opt-in.

## Почему это mandatory

Цель soly'евской rule infrastructure — LLM пишет код соответствующий
project standards **автоматически**, не «стараясь». Этот gate это
enforce'ит. Skipping = мы в том же месте что и без rules: «агент
написал, CI поймал, переделываем».

## Связанные rules

- `coding/ANALYZERS.md` §"Что делать при новых warnings" — escape hatches
- `process/build-verification.md` — full verify перед "готово"
- `process/pre-commit.md` — git hook (whitespace only)
- `process/commit-format.md` — Conventional Commits

## См. также

- `~/.pi/agent/skills/analyzer-coach/SKILL.md` — skill для шага 3 (rule gaps)
- `~/.pi/agent/skills/analyzer-coach/references/cookbook.md` — топ-30 жалоб → правила

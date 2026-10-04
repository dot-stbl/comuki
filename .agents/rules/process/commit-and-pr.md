---
description: commit body and pull request text — subject stays in the global commit-format rule; body says why, PR has three sections
globs: [".github/pull_request_template.md"]
priority: high
always: true
---

# Коммит и PR

Subject коммита — в `~/.agents/rules/process/commit-format.md`. Хук
`commit-msg` проверяет только его. Это правило — про **тело** коммита и
про **текст PR**. Ни то ни другое хук не проверяет.

## Тело коммита

Пиши тело, когда из subject не ясно **зачем**. Не пиши, что изменилось:
это видно в diff.

- Пустая строка после subject, затем 2–4 строки.
- Зачем правка и чего она сознательно не делает.
- Русский. Без анкеты `Change:` / `Gate:` / `Out:`.
- Однострочный фикс остаётся одной строкой.

```
[.stbl](feat/projects): cover domain-type settings json in mapper, handler, and validator tests

Карта domain-type → profile-key уже в настройках, а тесты смотрели
только Standard и null. Projects.Unit — 200/0. Прод-код не менялся.
```

## Pull request

Заголовок PR совпадает с subject коммита, без тега `[.stbl]`.

Описание — три секции, шаблон
[`.github/pull_request_template.md`](../../../.github/pull_request_template.md).
Гейты пишутся прозой в «Как проверено», включая то, что не запускалось
и почему. Чеклиста нет.

```markdown
## Что

## Зачем

## Как проверено
```

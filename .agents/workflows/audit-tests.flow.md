---
name: audit-tests
description: Добивка на готовой ветке: глубокий аудит → тесты покрытия → финальный гейт. Для веток, прошедших сокращённый флоу без audit/tests этапов.
defaults:
  timeoutMin: 60
  access: write
---

audit ⇄ $end ×0 → tests → gate-final ⇄ tests ×2

## audit
- role: review
- read-only
- onFail: { goto: $end, maxLoops: 0, then: human }

Проведи глубокий аудит изменений текущей ветки (git diff master...HEAD) против задачи «{task}»: мёртвый код и утечки ресурсов, race и конкурентность, безопасность (секреты, инъекции, авторизация), несоответствие спеке и design.md, регрессии в смежных путях, пропущенные edge-cases и обработка ошибок. Код не правь.
done: Отдан result.json с findings. Нет blocker-находок — status pass; иначе fail, у каждой находки файл, строка, severity и что исправить.

## tests
- role: code
- inputs: [audit]

Добей покрытие тестами на текущей ветке (задача «{task}»): используя findings аудита, допиши недостающие тесты (edge-cases, негативные ветки, пограничные значения) в существующих тест-проектах репозитория. Существующие ассерты не ослабляй и не удаляй, тесты не помечай Skip без ссылки на issue. Закоммить отдельным коммитом.
done: Недостающие тесты написаны и закоммичены; тест-проекты ветки зелёные.

## gate-final
- type: gate
- run: ["{gates.build}", "{gates.test}"]
- onFail: { goto: tests, maxLoops: 2, then: human }
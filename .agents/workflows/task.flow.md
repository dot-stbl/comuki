---
name: task
description: Единица ad-hoc декомпозиции, глубокий флоу: работа → гейт → аудит → ревью (глобальные правила) → тесты покрытия → финальный гейт
defaults:
  timeoutMin: 60
  access: write
---

work → gate ⇄ work ×1 → audit ⇄ work ×2 → review ⇄ work ×2 → reinvention ⇄ work ×1 → tests → gate-final ⇄ tests ×2

## work
- role: code

{task}
done: Критерий из брифа выполнен, изменения вместе с базовыми тестами закоммичены на текущей ветке.

## gate
- type: gate
- run: ["{gates.build}"]
- onFail: { goto: work, maxLoops: 1, then: human }

## audit
- role: review
- read-only
- inputs: [work]
- onFail: { goto: work, maxLoops: 2, then: human }

Проведи глубокий аудит изменений текущей ветки (git diff master...HEAD) против задачи и спеки change: мёртвый код и утечки ресурсов, race и конкурентность, безопасность (секреты, инъекции, авторизация), несоответствие спеке и design.md, регрессии в смежных путях, пропущенные edge-cases и обработка ошибок. Код не правь.
done: Отдан result.json с findings. Нет blocker-находок — status pass; иначе fail, у каждой находки файл, строка, severity и что исправить.

## review
- role: review
- skill: {skills.review}
- read-only
- inputs: [work]
- onFail: { goto: work, maxLoops: 2, then: human }

Проведи ревью скиллом review (canon + drift + nitor по ~/.agents/rules и канону репозитория) изменений текущей ветки против брифа единицы. Код не правь.
done: Отдан result.json с findings. Нет blocker-находок — status pass; иначе fail, у каждой находки файл, строка, severity и что исправить.

## reinvention
- role: review
- skill: {skills.reinvention}
- read-only
- inputs: [work]
- onFail: { goto: work, maxLoops: 1, then: human }

Прогони скилл reinvention (~/source/hybrid/skills/reinvention/SKILL.md) по изменениям текущей ветки: ищи самописный код для не доменной задачи, когда в стеке проекта уже есть принятый механизм (платформа, подключённая библиотека, идиома доступной версии, общая утилита репозитория). Каждая находка — ссылка на механизм (манифест:строка, путь утилиты). Код не правь.
done: Отдан result.json с findings. Нет blocker-находок — status pass; иначе fail, у каждой находки файл, строка, механизм-замена и почему замена эквивалентна на граничных случаях.

## tests
- role: code
- inputs: [work, audit, review]

Добей покрытие тестами: work уже написал базовые тесты. Используя findings аудита и ревью предыдущих этапов, допиши недостающие тесты (edge-cases, негативные ветки, пограничные значения) в существующих unit-проектах репозитория. Существующие ассерты не ослабляй и не удаляй, тесты не помечай Skip без ссылки на issue. Закоммить отдельным коммитом.
done: Недостающие тесты написаны и закоммичены; dotnet run --project по каждому затронутому тест-проекту зелёный.

## gate-final
- type: gate
- run: ["{gates.build}", "{gates.test}"]
- onFail: { goto: tests, maxLoops: 2, then: human }
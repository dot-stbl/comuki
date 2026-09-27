---
description: wire time format — ISO 8601 is the comuki project override of the user-global unix-ms wire rule
globs: ["platform/src/**/*.cs"]
priority: high
always: false
---

# Wire time format — ISO 8601 (проектный override)

Глобальное правило `~/.agents/rules/csharp/time-and-wire-format.md` требует
UTC unix milliseconds на wire — но его скоуп «сервис над time-stamped
upstream'ами» (прокси-контур). Comuki — не прокси: собственная БД →
собственный дашборд, одного владельца контракта два.

**Проектная конвенция:**

- Время на wire — **ISO 8601** (`DateTimeOffset`, стандартная сериализация
  ASP.NET Core / System.Text.Json); во view-типах поле остаётся
  `DateTimeOffset` / `DateTimeOffset?`, FE читает ISO-строку.
- Не переименовывать в `*UnixMs` и не вводить unix-ms на новых эндпоинтах.
- Внутри логики — по-прежнему `TimeProvider`, никаких
  `DateTime.UtcNow` / `DateTimeOffset.UtcNow` (глобальное правило в силе).

Осознанное отклонение по форме `frontend-construct-rules.md` §8: аудит не
поднимает ISO-поля как нарушение глобального правила, а введение unix-ms
на новых поверхностях — нарушение этого.

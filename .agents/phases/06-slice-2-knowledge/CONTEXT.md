# Phase 5 — Slice 2: Knowledge & MCP

## Цель

`comuki-mcp` отдаёт знания и правила по MCP. Брифы собираются
context-менеджером, а не пишутся вручную. Код в RAG **не индексируем** —
агент грепает в worktree.

Подробное обоснование — `.agents/docs/architecture/comuki-architecture.md` § 05 и
`.agents/docs/architecture/comuki-decisions.md` § "База знаний и документация".

## Что входит

Заполняется в `soly plan 5`. Ожидаемый scope:

- `Comuki.Platform.Knowledge` + `Comuki.Platform.Database.Knowledge` (pgvector).
- `Comuki.Platform.Mcp` — официальный C# MCP SDK, exposes retrieval.
- Context manager в Orchestration: карта репо + релевантные доки + дайджест
  конвенций для брифа.
- Seed знаний: проектные правила, дизайн-система, onboarding-инструкции.
- Ingestion path (manual на этом этапе): класть новые доки через dashboard /
  CLI. Auto-update — Phase 6+.
- `comuki-agent-core` (TS) — MCP-клиент, типы событий, формат брифа/отчёта
  (доля `agents/`, которая нужна именно здесь).

## Что НЕ входит

- Doc-agent (auto-update базы при merge фичи) — Phase 6+.
- Qdrant — только если pgvector не справится под реальной нагрузкой.
- Два свода правил (проектные + воркер) — воркер-свод живёт в `control-plane/`
  с Phase 7 (дашборд). Здесь только retrieval для проектных.

## Зависит от

Phase 4 (worker должен ходить через прокси, чтобы MCP-вызовы были
наблюдаемыми и атрибутировались по cost).

## Definition of Done

Заполняется в `soly plan 5`. Ожидаемые проверки:

- Воркер в Slice 0 цикле запрашивает MCP "дай рецепты под X" — получает
  релевантный кусок, добавляет в контекст, завершает задачу с учётом.
- `comuki-mcp` корректно отдаёт "current" версию документа, скрывая
  superseded (per decisions.md: upsert по ключу фичи).
- Проектные правила (в репо продукта) и воркер-правила (пока нет, придут в
  Phase 7) физически в разных местах — дрейф невозможен by construction.

## Hard constraints

- **Код НЕ в RAG** — только доки/спеки/правила/решения. Агентный grep по
  worktree.
- **Upsert по ключу фичи**, не дописывание. Старое → superseded, retrieval
  по умолчанию current.
- **Метаданные происхождения** на каждом куске (из какой задачи/коммита) —
  лечит "эхо-камеру" из architecture.md §08.
- **Только current-версии** в retrieval, если явно не попросили историю.

## Why

A prod-разбор Translator + pi выявил девять дыр, ни одна из которых не
покрыта существующими спеками. Проверено по 22 живым `openspec/changes/`
(см. §Coordination notes в `design.md` за каждым владельцем) — ни одна
текущая спека не закрывает эти девять; закрывать их там — значит
растягивать чужие скоупы и терять читаемость.

Девять дыр по порядку убывания серьёзности:

1. **Молчащий живой pi = бесконечный ран.** Heartbeat (REST `heartbeat`)
   доказывает, что контейнер и Translator живы — но не что pi
   продвигается. pi завис на медленном провайдере или сломался без
   crash'а → heartbeat идёт, lease держится, работа стоит.
   `worker-runtime` сегодня говорит «heartbeat is liveness» —
   архитектурная ошибка (см.
   `.agents/docs/architecture/comuki-architecture.md:416`, два таймера —
   решение зафиксировано; реализация отдана этому change'у).
2. **Крах pi mid-run маскируется под success.** Translator ждёт
   `StageReport` (stream-json event с `result`); процесс pi упал, не
   отправив финального `agent_end` — Translator зависает, lease
   держится, run никогда не закрывается. Сегодня: silent fail.
   `add-orchestra` §1.3.1 ужесточил инициацию (`pi --mode rpc`); этот
   change добавляет **чтение ExitCode** и **outstanding-bidi-keepalive**
   на ветке краха (фаза 1; полный фикс exit-code + cleanup —
   отдельный bug-батч, см. `design.md` §Coordination notes).
3. **Метрик воркер-контура ноль.** Phase 2 `add-orchestra` добавила
   MEL→OTLP на host-стороне. Воркер-контур (Translator + pi) не
   инструментирован: ни `events_total`, ни `last_event_age`, ни
   `parse_errors_total`, ни `heartbeat_outcome`. Операторы не видят
   зомби; дебаг только через journal.
4. **Image-skew молча теряет команды.** Если Translator и host
   разошлись по версии протокола, сегодня `WorkerCommandHub` тихо
   шлёт команды, которые пи-рантайм другой версии не понимает —
   operator не замечает, что воркер «не отвечает» потому что не
   распознал.
5. **`WorkerCommandHub` и мятые virtual keys — single-replica.** Эти
   два in-process state-кармана делают невозможным второй replica
   host'а (Baton в `add-orchestra` §D11 явно зафиксировал single-replica
   как ограничение; этот change закрывает один из двух карманов).
6. **Backpressure отсутствует.** События pi читаются через
   `ReadLine`; длинная строка (>1 МБ) может исчерпать stdin buffer;
   молчаливая деградация — ассиметрия между progress-фрагментами
   (допустимо дропать) и обязательными событиями (дропать нельзя).
7. **Contract-handshake не закреплён.** `Connect` несёт worker token,
   но не версию протокола и не capability-флаги. Совместимость
   проверяется на integration-тестах, а не в runtime.
8. **Autonomy-escalation-timeout не интегрирован с воркер-evidence.**
   Phase 3 `add-orchestra` (Coda) оставила `VerificationRecord`-consumer
   спроектированным; TrustClass-промоушен («K зелёных подряд →
   PromoteTo», «Red-паттерн → DemoteTo») — non-goal везде.
9. **Wall-clock дедлайны — воркер-контур vs brain-ops.** Brain-ops
   бюджеты `add-mission-cowork` (5/15/30 мин turn-budget) — другая
   территория. Воркер-контур сегодня без wall-clock охраны.

Этот change — **семь фаз** с явной границей. Каждая закрывает одну
или несколько дыр; ни одна не лезет в скоуп `add-orchestra` /
`add-mission-cowork` / `agent-runtime-capabilities` / `harden-pi-worker-sandbox`
(см. `design.md` §Coordination notes за разграничением). Общий объём
**семь spec-дельт** (1 новый capability `worker-runtime` —
полностью **MODIFIED**, 1 новый capability `observability` для
воркер-телеметрии, 1 MODIFIED для `runs`), **одна** правка чужого
change'а (`add-orchestra` §8 — две дополнительные задачи этого
изменения, см. ниже).

## What Changes

Семь фаз:

- **Liveness two-timer** — Translator измеряет *progress*
  (last-event-age: любой законный стрим-ивент сбрасывает) и
  *liveness* (heartbeat). Stall на progress-таймере дольше порога →
  `worker.stall_detected` journal event → политика `warn → gentle-kill
  → fail item` (escalation tiers). Пороги — `TranslatorOptions` (по
  канону `di-options`, `ValidateOnStart`).
- **Дедлайны** — turn-budget (wall-clock на ход, по умолчанию равен
  brain-ops 5/15/30 мин по профилю) и run-budget (wall-clock на
  всю сессию); превышение → тот же stall-путь. Разграничение с
  brain-ops бюджетами `add-mission-cowork` — наш budget — это *timeout
  на идемпотентный restart*, их — на *task completion semantics*.
  Воркер может пережить 1 превышение turn-budget без падения run'а;
  3 превышения run-budget подряд → fail item.
- **Воркер-телеметрия** — OTel-SDK в Translator (тот же OTLP endpoint,
  что и host из phase 2 `add-orchestra`): gauges/counters
  `last_event_age`, `events_total{type}`, `stdin_commands_total{cmd}`,
  `parse_errors_total`, `heartbeat_outcome{status}`; histogram
  `events_per_sec`; `traceparent`-пропагация от claim (MEL лог
  Translator'а уносит W3C trace id из claim body). MEL лог воркера
  шлётся через OTLP-экспортёр (host-сторона уже на master после
  `add-orchestra` phase 2).
- **Backpressure** — events-канал bounded (`Channel<WorkerEvent>`
  bounded с политикой drop-oldest для progress-фрагментов; обязательные
  события — без дропа). Кап длины строки `ReadLine` (default 1 МБ,
  конфигурируемый, `0` = unlimited; `0` — non-production). При
  переполнении — `worker.events_dropped` journal event + counter.
- **Contract-handshake** — `Connect` несёт `{ protocolVersion,
  capabilities[] }`. Host логирует `worker.skew_detected` (с версией
  и ожидаемой), `worker.unknown_command` (имя неподдерживаемой
  команды) — с метриками `skew_total` / `unknown_command_total`.
- **Multi-replica воркер-hub** — `WorkerCommandHub` + мятые virtual
  keys из in-memory → PG (LISTEN/NOTIFY, см. trade-off в `design.md`).
  Вторая жизнь in-memory слоя — cache поверх PG. Single-replica probe
  `comuki.orchestra.single_replica` (из `add-orchestra` §10.7) остаётся
  как **documented** ограничение — этот change не снимает его, но
  подготавливает почву (multi-replica = следующий change, требующий
  ещё и PG-claim-fencing).
- **TrustClass-промоушен** — `K зелёных подряд → PromoteTo`,
  `Red-паттерн → DemoteTo` для Coda-`VerificationRecord`-consumer'а
  (Phase 3 `add-orchestra` оставил consumer спроектированным —
  теперь первый реальный вызывающий). Параметры: `K`, window,
  red-pattern. Autonomy-политика — отдельный signal (`add-orchestra`
  §`autonomy-escalation-timeout` остаётся на пассивном даун-ракете,
  этот change не отменяет его; TrustClass-промоушен — наш
  `up/down` по evidence, два канала не пересекаются).

Плюс **одна** правка чужого change'а (мы владельцы `add-orchestra`):
фаза 8 (`Instrument`) дополняется двумя задачами —
`8.5` «ACP-adapter as third harness» (Agent-Client-Protocol,
вход для Codex/Gemini/OpenHands-совместимых; зависит от 8.1-8.3),
`8.6` «PlanValidator width/depth caps» (лимит узлов/рёбер/глубины —
конфигурируемый, дефолт консервативный). Эти два пункта живут в
`add-orchestra`, не здесь; в `design.md` §Coordination notes — указатель.

## Capabilities

### New Capabilities

- `worker-runtime`: не-новый, переписывается как MODIFIED (см. ниже).
- `observability`: не-новый, переписывается как MODIFIED в части
  воркер-телеметрии. Создание нового capability для воркер-телеметрии
  — `worker-observability` — отвергнуто: один capability на одну
  платформенную ответственность, observability уже владеет
  телеметрией host'а. `design.md` §D3 записывает границу.

### Modified Capabilities

Три capability несут `## MODIFIED Requirements` deltas. Существующие
требования **переписываются**, не дополняются — каждое новое
требование ниже стоит на месте существующего, которое переезжает в
MODIFIED:

- `worker-runtime`: две новые секции поверх существующих требований —
  *liveness/progress two-timer* (фаза 1), *event-pipeline backpressure
  и protocol-handshake* (фазы 4-5). Существующее требование «Agent
  invocation and stream parsing» (v1.x `pi -p BRIEF --no-session`)
  переписано в MODIFIED: протокол handshake'а и формат event'ов.
  Существующее требование «Translator loop» (claim → spawn → report)
  переписано в MODIFIED: progress-таймер, deadline policy,
  heartbeat-side metric.
- `observability`: новая секция «Worker telemetry: Translator side».
  Существующее требование «MEL exports logs through OTLP» (host side,
  phase 2 `add-orchestra`) остаётся нетронутым; это MODIFIED
  расширяет экспорт-ногу на Translator, не дублирует.
- `runs`: новая секция «Run-level wall-clock budget» и
  «TrustClass promotion driven by verification evidence».
  Существующее требование «Run state machine» (семь состояний) не
  переписано — wall-clock budget не меняет state machine, он
  *усиливает* терминальное решение (Coda §`verification-pending` уже
  существует, см. `add-orchestra` §3.3).

### Touched but no spec delta

Два capability читаются и пишутся, но без `## MODIFIED Requirements` —
изменение в реализации, не в контракте:

- `host`: `comuki.orchestra.single_replica` health check (из
  `add-orchestra` §10.7) становится источником для фазы 6 single-replica
  документации; новая правка в README — не дельта.
- `identity`: `McpWorkerToolGate` (если воркер-телеметрия выносит
  метрики через MCP — в этой фазе нет, только OTLP).

## Impact

- `platform/src/host/Comuki.Host.Translator/` (основное): две новые
  таймерные политики (`ProgressWatchdog`, `DeadlinePolicy`), bounded
  events-канал, contract-handshake, OTel-инструментация Translator'а,
  worker-exit-code чтение (полное — отдельный bug-батч; базовая
  ветка здесь).
- `platform/src/host/Comuki.Host/Observability/Installers/` —
  расширение `ComukiTelemetryInstaller` воркер-OTLP-ногой.
- `platform/src/host/Comuki.Host/Runs/WorkerCommandHub.cs` — миграция
  in-memory hub на PG LISTEN/NOTIFY (Trade-off в `design.md` §D6).
- `platform/src/engine/Comuki.Engine.Orchestration/` —
  `VerificationRecordStore` consumer для TrustClass-промоушена.
- `agents/comuki-worker-sdk/` — protocol-handshake (capability-флаги
  в `Connect` request).
- `dashboard/src/` — не затрагивается; телеметрия — через OTLP,
  MCP-инструменты для неё — следующий change (per
  `add-orchestra` §`critic-foundation`).

## Non-goals

- **Reading pi `ExitCode` end-to-end.** Базовая ветка (Translator
  фиксирует `process.ExitCode` при ненормальном завершении, журналит
  в `worker.exit_code` event) — здесь. Полный end-to-end фикс
  (Translator делает authoritative `cancelled`/`failed` по exit-code,
  отказывается от «wait for StageReport» как единственного сигнала)
  — отдельный bug-батч. Этот change делает exit-code *видимым*, не
  *полномочным*.
- **Multi-replica coordinator с PG claim-fencing.** Этот change
  убирает in-memory как **источник** состояния для `WorkerCommandHub`
  и virtual keys, но claim-фенс остаётся single-replica. Multi-replica
  полностью — следующий change.
- **TrustClass-домен в orchestration.** `RunTrustClass` уже на master
  (см. `Comuki.Engine.Orchestration.Domain.RunTrustClass`). Этот
  change добавляет промоушен-логику (consumer), не меняет домен.
- **Brain-ops бюджеты `add-mission-cowork`.** 5/15/30 мин
  turn-budget — их территория. Разграничение в
  `design.md` §Coordination notes.
- **Cowork #101 realtime-backplane.** Multi-replica для realtime
  presence/typing — их. Наш multi-replica — только `WorkerCommandHub` +
  virtual keys, не пересекается.
- **`autonomy-escalation-timeout`.** Существующий change остаётся на
  пассивном даун-ракете. TrustClass-промоушен — наш ап/даун по
  evidence. Два канала не сливаются.
- **Замена `WorkerId` на `WorkerHostId` / `SlotId` / `ExecutionId`.**
  Это `add-mission-cowork` §11. Наша фаза 6 работает с v1.x
  `WorkerId`-именованным каналом (как и `add-orchestra` §D11).
- **MCP-инструменты для воркер-телеметрии.** Observability MCP-каталог
  из `add-orchestra` §2 (Critic-foundation) — host-сторона; воркер-side
  метрики уезжают в тот же VictoriaLogs/VictoriaMetrics, но
  MCP-обёртки — следующий change.
- **Авторство кода.** Этот change — план, не реализация. Реализация —
  отдельные worker-flow.

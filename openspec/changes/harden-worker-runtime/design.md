## Context

См. `proposal.md` за мотивацией. Ключевые ограничения, формирующие *как*:

- Translator + pi запускается один раз на цикл claim → spawn → report →
  repeat (`openspec/specs/worker-runtime/spec.md` секция «Translator
  loop»). v1.x работает в режиме `pi -p BRIEF --mode json --no-session`;
  `add-orchestra` уже на master переключает v2 на `pi --mode rpc
  --no-session` (Baton §1.3.1). Этот change **не** лезет в форму
  запуска — он работает поверх существующего wire (events на stdout,
  команды на stdin по биди; см.
  `openspec/changes/add-orchestra/spike-1b-report.md`).
- Heartbeat (REST `POST /workers/{workItemId}/heartbeat`) — текущий
  «liveness». Сегодня прод-разбор показал, что это **неправильное
  определение** — heartbeat доказывает только, что Translator жив и
  lease держится, но не что pi продвигается. Архитектурная ссылка
  на двухтаймерное решение:
  `.agents/docs/architecture/comuki-architecture.md:416` (зафиксировано
  до этого change'а; реализация отдана этому change'у).
- `WorkerCommandHub` сегодня — in-memory singleton в одном Host
  replica (`add-orchestra` §D11). Второй replica молча теряет команды
  Baton'а (фаза 1 `add-orchestra`). Этот change переезжает hub на
  PG LISTEN/NOTIFY как **источник** состояния, оставляя in-memory
  cache как оптимизацию — но не снимает single-replica как таковой.
- Мятые virtual keys (`ConfigurationVirtualKeyStore` + in-memory
  overlay) — тот же single-replica карман; этот change переезжает
  store на PG. Второй replica host'а читает из той же таблицы.
- `Observability:VictoriaMcpBaseUrl` и OTLP endpoint — на master
  (phase 2 `add-orchestra`). Воркер-сторона инструментации
  добавляется на ту же OTLP ногу.
- `VerificationRecord` (`add-orchestra` §3, Coda) уже спроектирован.
  Consumer-промоушен «K зелёных подряд → PromoteTo» — первый
  реальный вызывающий (домен `RunTrustClass` на master, логика
  промоушена — наш).
- Brain-ops бюджеты `add-mission-cowork` (5/15/30 мин
  turn-budget) живут в **task-completion** семантике: «5 мин на
  выполнение одной задачи мозгом». Воркер-контур имеет другую
  семантику: «timeout на идемпотентный restart», потому что воркер
  ephemeral (lease dies при завершении). Эти две бюджеты **не**
  объединяются (см. §Coordination notes).
- Cowork #101 realtime-backplane — multi-replica для *presence* и
  *typing* в Mission-стриме. Наша фаза 6 — multi-replica для
  *command-hub* и *virtual-key store*. Два разных механизма; не
  смешивать (см. §Coordination notes).

**Существующие спеки, к которым этот change НЕ лезет:**

- `agent-runtime-capabilities` — секреты (Phase D §4.2-§4.6: secret
  refs в Docker/K8s providers; logger filter; per-project
  `Compute:InjectSecrets`). Ничего из этого этого change не дублирует.
- `harden-pi-worker-sandbox` — клонирование SourceGit (§4.x),
  NetworkPolicy/Docker internal net, non-root image. Ничего из
  этого этот change не дублирует.
- `add-orchestra` — Baton (WorkerCommandHub), Coda (verification),
  Section (scope layers), Tuner (mutable virtual keys), Instrument
  (IHarness). Этот change дополняет Baton/Tuner одним **слоем**
  (PG LISTEN/NOTIFY как источник) — не дублирует, не отменяет.
- `add-mission-cowork` — work substrate, brain-ops, slots/execution
  identity. Этот change работает на v1.x `WorkerId`-канале; ride'ит
  на слот/execution identity, когда она приземлится (как и
  `add-orchestra`).

## Goals / Non-Goals

**Goals:**

- Translator шестой-deadlock от зависшего pi (heartbeat идёт, работа
  стоит): progress-таймер с политикой escalation (`warn →
  gentle-kill → fail item`).
- Wall-clock дедлайны (turn + run) с конфигурируемыми порогами,
  отдельными от brain-ops бюджетов.
- Видимость воркер-контура в телеметрии (last-event-age, counters,
  parse errors, heartbeat outcome) через OTLP — тот же endpoint,
  что и host.
- Backpressure: bounded events-канал с разной политикой для
  progress-фрагментов и обязательных событий; bounded `ReadLine`
  против длинных строк.
- Protocol-handshake в `Connect`: версия + capability-флаги, чтобы
  хост ловил skew и unknown-команды явно (журнал + метрика), а
  не молча.
- WorkerCommandHub + mutable virtual-key store читаются из PG
  (LISTEN/NOTIFY) всеми replica'ми host'а; in-memory cache как
  оптимизация. Single-replica по-прежнему документирован — следующий
  change снимает.
- TrustClass-промоушен по evidence (K зелёных подряд → PromoteTo,
  Red-паттерн → DemoteTo) для Coda-VerificationRecord-consumer'а.

**Non-Goals:** *(design-level)*

- Reading pi `ExitCode` end-to-end — полная ветка fail-by-кода.
  Базовое журналирование exit-code — да (фаза 1). Авторитетное
  решение «run failed по exit-code» — bug-батч.
- Multi-replica claim-fencing — следующий change.
- TrustClass-домен — на master; этот change добавляет
  промоушен-логику, не меняет домен.
- Brain-ops бюджеты (5/15/30 мин) — территория `add-mission-cowork`.
- Realtime presence-разъезд (`#101`) — территория cowork; этот
  change не смешивается.
- Slot/execution identity (`WorkerHostId`/`SlotId`/`ExecutionId`) —
  территория cowork 11.1.
- Замена wire формата (`stream-json` → что-то другое). Протокол
  handshake'а — это **метаданные** на `Connect`, не замена формата.
- Authoring кода. Change — план.

## Decisions

### D1. Liveness two-timer: progress (last event age) и liveness (heartbeat)

```text
WorkerProgressWatchdog
  ├── last_event_age   — монотонный, любой законный stream-event сбрасывает
  ├── progress_timeout — TranslatorOptions.WorkerProgressTimeout (default 60s)
  ├── escalation:
  │   ├── tier 1 (warn)         — log + journal `worker.stall_warn`
  │   ├── tier 2 (gentle kill)  — послать `Stop` через WorkerCommandHub
  │   └── tier 3 (fail item)    — fail-soft через WorkItem claim-complete с message
  └── heartbeat_outcome — отдельный counter от lease-side heartbeat (REST)
```

Heartbeat (REST `POST /workers/{workItemId}/heartbeat`) продолжает
работать — он по-прежнему доказывает, что lease жив. **Progress**
таймер — *дополнительный*, не *заменяющий* heartbeat: heartbeat без
progress = stall warning tier 1 → escalation → gentle-kill → fail
item. Сценарий `--timeout` в spec про `halt semantics` /
`lease.lost` остаётся в силе (cancelled — heartbeat приходит, но
перестаёт получать 204 из-за cancelled Run).

**Альтернатива:** «progress = время между двумя событиями pi».
Отвергнуто — `EventUpdate` (text delta) у pi случается чаще, чем
раз в минуту; `EventUpdate` слишком частый, тайминг-порог становится
бессмысленным. Источник истины — last event age, не inter-arrival.

### D2. Wall-clock дедлайны: turn-budget и run-budget

- `TranslatorOptions.TurnBudget` (default 60 min, конфигурируемый) —
  время на один «ход» (от spawn до StageReport). Превышение →
  gentle-kill (D1 tier 2).
- `TranslatorOptions.RunBudget` (default 480 min, конфигурируемый) —
  время на всю сессию (worker process lifetime, несколько ходов).
  Превышение → fail item, идемпотентный restart.
- Оба bound'а — на **воркер-контур**; brain-ops `5/15/30 мин` —
  на **task-completion** семантику. Воркер может пережить 1
  превыщение turn-budget (restart) без падения run'а; 3 превыщения
  run-budget подряд → fail item.

### D3. Worker telemetry: OTel-SDK в Translator

Translator подключается к **тому же** OTel SDK, что host
(Composer `ComukiTelemetryInstaller` уже на master; phase 2
`add-orchestra`). Воркер-сторона добавляется **через тот же
exporter**, через новую `ActivitySource` `Comuki.Translator.Runtime`:

| Metric | Type | Tags | Purpose |
|---|---|---|---|
| `comuki.worker.last_event_age_ms` | gauge | `worker_id` | progress watchdog input |
| `comuki.worker.events_total` | counter | `worker_id`, `type` | parsed events by type |
| `comuki.worker.parse_errors_total` | counter | `worker_id`, `kind` | JSON parse failures |
| `comuki.worker.stdin_commands_total` | counter | `worker_id`, `cmd` | command delivery |
| `comuki.worker.heartbeat_outcome` | counter | `worker_id`, `status` | 204/409 distribution |
| `comuki.worker.events_per_sec` | histogram | `worker_id` | throughput sanity check |
| `comuki.worker.skew_detected_total` | counter | `expected_version` | protocol skew |
| `comuki.worker.unknown_command_total` | counter | `cmd` | unsupported commands |
| `comuki.worker.events_dropped_total` | counter | `kind` | backpressure drop |

Worker MEL лог → OTLP-экспортёр (`AddOpenTelemetry(o => o.AddOtlpExporter())`)
через тот же OTLP endpoint. W3C trace id берётся из claim body
(claim — первое, что происходит, у worker'а ещё нет своего
ActivitySource); trace id пробрасывается в каждый `Activity`,
который Translator открывает.

**Альтернатива:** создать новый capability `worker-observability`.
Отвергнуто — один capability на одну платформенную ответственность;
observability уже владеет host-телеметрией. Граница в `proposal.md`.

### D4. Backpressure: bounded events-канал + bounded ReadLine

- `Channel<WorkerEvent>` bounded (default 1024) с политикой
  drop-oldest для progress-фрагментов (text deltas); обязательные
  события (`StageStart`, `StageReport`, `agent_end`) — без
  drop'а: при переполнении — `consumer_dropped` journal event,
  Reader ждёт пока channel освободится (миллисекунды).
- `ReadLine` cap длины строки (default 1 МБ; `0` = unlimited,
  non-production); строки длиннее капа → drop + counter
  `parse_errors_total{kind = line_too_long}`.

### D5. Contract-handshake: версия протокола + capability-флаги в `Connect`

`Connect` request (worker → host) несёт:

```proto
message WorkerHandshake {
  required string protocol_version = 1;   // semver: "1.2.0"
  repeated string capabilities      = 2;   // ["session.live", "rpc.steer", ...]
  // ... existing fields ...
}
```

Host сравнивает `protocol_version` с **ожидаемой** (semver, MAJOR +
MINOR). Major mismatch → host отказывает `Connect` с кодом
`worker.skew_major`; minor mismatch → host ставит worker'у
`capabilities_compat:*{}` (только capabilities, совместимые с
ожидаемой) и журналирует `worker.skew_detected` (с версией).
Unknown capability-флаг в `capabilities[]` → игнорируется +
`worker.unknown_command_total{cmd = <flag>}` counter.

Worker-exit-code чтение (фаза 1, базовая ветка): если pi процесс
exit'нул ненормально **до** StageReport — Translator фиксирует
`process.ExitCode` и журналирует `worker.exit_code` event (с кодом
и safe stderr detail). Этот event *видим*, но **не авторитетен** —
Translator всё ещё ждёт StageReport. Полная fail-by-exit-code —
отдельный bug-батч (см. Non-goals).

### D6. Multi-replica воркер-hub: PG LISTEN/NOTIFY

```text
  ┌────────────────────────────────┐
  │  WorkerCommandHub (host, any replica)
  │   └── читает через PG LISTEN channel `comuki.cmdhub`
  │   └── пишет через INSERT в `comuki_cmdhub` (NOTIFY trigger)
  │   └── in-memory cache как оптимизация (TTL 1s, refresh-on-miss)
  │       не как источник истины
  └────────────────────────────────┘

  ┌────────────────────────────────┐
  │  IVirtualKeyStore (mutating Tuner)
  │   └── таблица `proxy_virtual_keys` (PG)
  │   └── in-memory cache как оптимизация (read-through)
  └────────────────────────────────┘
```

`comuki_cmdhub` (PG таблица + LISTEN channel):

```sql
CREATE TABLE comuki_cmdhub (
  id           bigserial PRIMARY KEY,
  worker_id    text NOT NULL,
  cmd          text NOT NULL,        -- 'Stop' | 'InjectContext' | 'LeaseExpired' | 'TurnInput' | ...
  payload      jsonb NOT NULL,
  created_at   timestamptz NOT NULL DEFAULT now(),
  claimed_by   text NULL,             -- replica name; NULL = unclaimed
  claimed_at   timestamptz NULL
);
CREATE INDEX comuki_cmdhub_worker_unclaimed
  ON comuki_cmdhub (worker_id) WHERE claimed_by IS NULL;
-- trigger: AFTER INSERT → NOTIFY comuki.cmdhub;
-- worker-side: LISTEN channel (channel-level), then SELECT FOR UPDATE SKIP LOCKED LIMIT 1
```

**Trade-off: PG LISTEN/NOTIFY vs Redis**

| Критерий | PG LISTEN/NOTIFY | Redis |
|---|---|---|
| Infra уже на master | ✅ Postgres есть (deploy/docker-compose) | ❌ Redis **опциональный** (`feature/restore-redis-cache`, `feature/redis-compose`); в проде не дефолт |
| Готовый код | ❌ нужно писать | `rescue/redis-cache` ветка (resolved) есть |
| Latency | ≤2 — ~1-3 мс per NOTIFY (in-region) | ~15-20 мс per round-trip |
| Транзакционная семантика | ✅ в PG-транзакции вместе с другими state-changes | ❌ отдельный broker, отдельные tx |
| Operational surface | ✅ один кластер, один backup | ❌ второй кластер, мониторинг отдельно |
| Single-replica coordinator | ✅ LISTEN даёт любой replica, FOR UPDATE SKIP LOCKED делает fencing | ⚠ PUB/SUB не fencing — duplicate обработка |
| Стоимость смены infra | ✅ zero — Postgres уже required | ❌ new required для всех |

**Выбор: PG LISTEN/NOTIFY.** Postgres уже required для всех
deploy'ев (`deploy/docker-compose.yml`); это не добавляет ни одного
нового сервиса. Redis остаётся *опциональным* (`feature/redis-compose`)
для read-cache (`ProjectSettingsCache`); воркер-hub — другой
класс задачи, и новый required service для него — слишком высокая
цена. Готовый код из `rescue/redis-cache` не переиспользуется
(он для cache, не для command delivery); Trade-off отвергает Redis
по стоимости infra. Trade-off отвергает in-memory-LISTEN-only
(нет fencing, multi-replica молча потеряет команды — ровно то, что
D11 из `add-orchestra` фиксирует как ограничение).

### D7. TrustClass-промоушен: `K зелёных подряд → PromoteTo`, `Red-паттерн → DemoteTo`

```text
TrustClassPromotionPolicy
  ├── K_green:        int   (default 3)         — K подряд gate_evaluated:passed
  ├── window_ms:      long  (default 24h)       — sliding window
  ├── red_pattern:    int   (default 3)         — N подряд gate_evaluated:failed
  ├── promotion_target: TrustClass (default Max)   — куда повышать
  └── демоушен на каждый Red-pattern — на Predecessor уровень
```

Консумер `VerificationRecordStore.ListRecentByProjectAsync(projectId,
window_ms)`. Если в последних K оценок все `passed` (verified) →
`RunTrustClass.SetAsync(runId, promotion_target)`. Если в Red-pattern
(3 подряд `failed`) → `RunTrustClass.SetAsync(runId, predecessor)`.

**Альтернатива:** Auto-promotion через `autonomy-escalation-timeout`.
Отвергнуто — `autonomy-escalation-timeout` остаётся на
пассивном даун-ракете (даун при timeout без активности); наш
промоушен — на **evidence** (gate verdicts). Два канала не
пересекаются, два механизма даун'а разные в одной системе.

### D8. TrustClass-домен: на master, не меняется

`Comuki.Engine.Orchestration.Domain.RunTrustClass` (smoke-type'ы
`Trusted | Standard | Limited | Isolated`) уже на master. Этот
change добавляет промоушен-логику (consumer), не меняет домен.
Coordination notes: если run-time промоушен нужен в cowork 11.x,
он ride'ит на те же smoke-типы.

## Risks / Trade-offs

- **[Risk] PG LISTEN/NOTIFY — fire-and-forget без ACK** → Host шлёт
  `NOTIFY` через INSERT, но worker реплики может быть offline.
  Решение: INSERT с `claimed_by = NULL` остаётся в таблице;
  worker-side LISTEN забирает **+ polling fallback** (каждые 5с
  SELECT WHERE claimed_by IS NULL) — гарантирует доставку при
  network blip. Тесты с `pg_terminate_backend` подтверждают
  fallback путь.
- **[Risk] progress-таймер ложно-срабатывает на долгом tool-call** →
  Конфигурируемый порог (default 60s, max 1h); профильная ракета
  (`control-plane/profiles/<name>.md` `progress_timeout_s` frontmatter)
  позволяет удлинить для долгих tool-call'ов. Escalation tier 1
  (warn) срабатывает на `60s` без действия — журнал `worker.stall_warn`
  для оператора.
- **[Risk] wall-clock budget vs legitimate long-running tool calls**
  → Tool-call-specific budget (отдельный `tool_call_timeout_s`,
  default 5min, равен brain-ops 5min для согласованности). Brain-ops
  и воркер-tool-call — разные сущности, но **default** совпадает
  (см. Coordination notes). Профильная ракета может настроить.
- **[Trade-off] TrustClass-промоушен рискует промахнуться в обе
  стороны** → `K` и `window` конфигурируемые; default — K=3,
  window=24h. Первое production-window оценивается отдельно.
- **[Trade-off] protocol-handshake ужесточает wire** → Это
  **метаданные**, не замена формата. Wire `stream-json` остаётся.
  Handshake — это `Connect` request payload; v1.x worker'ы без
  handshake'а работают в compat-mode (default capabilities), новые
  worker'ы объявляют capabilities явно. Двухшаговый rollout.
- **[Trade-off] PG `comuki_cmdhub` — новая таблица** → Подобрано
  schema-stable место рядом с другими infra-таблицами; миграция
  `dotnet ef migrations add AddWorkerCommandHubTable` (по канону
  `ef-migrations.md`).

## Migration Plan

```text
Phase 1 (Liveness + dead-drain + exit-code baseline)
  + TranslatorOptions.WorkerProgressTimeout / TurnBudget / RunBudget
  + ProgressWatchdog + DeadlinePolicy
  + worker.stall_detected / worker.exit_code journal events
  + single-replica probe (комментарий в D6)
  → existing wire unchanged

Phase 2 (Worker telemetry)
  + ComukiTelemetryInstaller воркер-OTLP-нога (D3)
  + traceparent propagation от claim
  → existing wire unchanged

Phase 3 (Backpressure)
  + Channel<WorkerEvent> bounded (D4)
  + ReadLine строки cap
  → existing wire unchanged

Phase 4 (Contract-handshake)
  + Connect.Handshake payload (D5)
  + worker.skew_detected / worker.unknown_command_total
  → backward-compat: v1.x worker без handshake'а работает в compat-mode
  → existing wire изменение — добавляется поле, не заменяется

Phase 5 (TrustClass-промоушен)
  + TrustClassPromotionPolicy (D7)
  + RunTrustClass.SetAsync вызывающий
  → existing wire unchanged

Phase 6 (Multi-replica воркер-hub)
  + PG-таблица comumi_cmdhub (D6)
  + WorkerCommandHub читает/пишет через PG
  + IVirtualKeyStore читает/пишет через PG
  → in-memory cache остаётся (optimistic), но вторичен
  → single-replica по-прежнему документирован (claim-fencing — следующий change)

Phase 7 (Cross-phase gate)
  + dotnet build comuki.slnx -c Debug
  + dotnet run --project tests/integration/Comuki.Host.Translator.Integration.PiCli
  + openspec validate harden-worker-runtime --strict
```

**Wave split.** Phase 1 — single PR (progress-watchdog + deadline +
exit-code baseline). Phase 2 — single PR (OTel). Phase 3 — single PR.
Phase 4 — single PR (wire-изменение, требует координированного
worker-SDK release). Phase 5 — single PR. Phase 6 — два под-PR'а
(6a: PG schema + read; 6b: PG write + cache invalidation). Каждая
фаза shippable behind feature flag; rollback per фаза — существующий
«roll back the diff, leave the wire».

## Open Questions

- **Phase 6 lock:** FOR UPDATE SKIP LOCKED на `comuki_cmdhub` — не
  строгий fencing (replica может умереть между SELECT и claim);
  замена на advisory lock — следующий change (multi-replica полностью).
  Здесь принимаем: если replica умерла claim между, другая replica
  разгребает через polling fallback (D6). Это не абсолютно fencing,
  но достаточно для single-replica scenario (dead replica не
  обрабатывает anyway).
- **Phase 1 turn-budget default:** 60min — мой choice, не владельца.
  Архитектурный референс `.agents/docs/architecture/comuki-architecture.md`
  не уточняет; brain-ops default `5/15/30 мин` относится к task
  completion. Открытый вопрос владельцу при первом review.
- **Phase 5 `K` для проекта vs глобальный:** Глобальный `K` в
  TranslatorOptions — простой default. Per-project override
  (ProjectSettings.TrustClassPromotionK) — natural extension,
  не в этом change. Записано как follow-up.

## Coordination Notes

Cross-change зависимости, о которых следующая сессия должна знать.
**Не блокеры**, но записаны, чтобы audit не переоткрывал их:

- **`add-orchestra` §1 (Baton)** — `WorkerCommandHub` уже на master,
  Baton первая production caller. Наша фаза 6 переезжает hub на
  PG; Baton не меняется (это первая сторона нового источника).
- **`add-orchestra` §3 (Coda) / §3.1** — `VerificationRecord` +
  `gate_evaluated` event уже спроектированы. Наша фаза 5 —
  первая реальная промоушен-логика (consumer).
- **`add-orchestra` §7 (Tuner)** — mutable `IVirtualKeyStore` уже
  на master (in-memory overlay primary). Наша фаза 6 переезжает
  store на PG; Tuner меняется через новый источник.
- **`add-orchestra` §10.7** — single-replica health check —
  documented ограничение. Наш change не снимает (multi-replica =
  следующий change), но в D6 мы переезжаем на PG-источник
  чтобы multi-replica был **возможен**.
- **`add-orchestra` §8 (Instrument)** — этот change переехал
  `WorkerCommandHub` на PG. Дополнительно (по явному указанию
  владельца) фаза 8 `add-orchestra` дополнена двумя задачами
  в **этом** change'е:
  - **8.5 «ACP-adapter as third harness»** — IHarness-реализация
    поверх Agent-Client-Protocol; вход для Codex/Gemini/OpenHands-
    совместимых; зависит от 8.1-8.3.
  - **8.6 «PlanValidator width/depth caps»** — лимит узлов/рёбер/
    глубины в PlanValidator — конфигурируемый, дефолт
    консервативный.
  Эти два пункта **живут в `add-orchestra`**, не здесь.
  Правка — мелкое tasks-дополнение (мы владельцы `add-orchestra`).
- **`add-mission-cowork` §11** — slot/execution identity. Наша фаза 6
  работает с v1.x `WorkerId`-каналом; ride'ит на slot identity когда
  она приземлится (как `add-orchestra` §D11). Coordination
  от `add-orchestra` §Coordination notes сохраняется.
- **`add-mission-cowork` §brain-ops budgets (5/15/30 мин
  turn-budget)** — task-completion семантика. Наша фаза 2
  (wall-clock turn-budget) — wall-clock для воркер-контура.
  Воркер-tool-call default `5min` **численно** совпадает с
  brain-ops `5min` для default-профилей, но семантика другая:
  воркер-tool-call — «timeout on идемпотентный restart»,
  brain-ops — «timeout on task completion». Профильная ракета
  разводит.
- **`add-mission-cowork` #101 realtime-backplane** — multi-replica
  для presence/typing в Mission-стриме. Наша фаза 6 — multi-replica
  для command-hub и virtual-key store. **Не смешивать**: разные
  механизмы, разные типы сообщений, разные scope. Наш LISTEN-channel
  `comuki.cmdhub` — единственная task-owned для command delivery;
  realtime backplane — отдельный owned для realtime.
- **`autonomy-escalation-timeout`** — пассивный даун-ракет при timeout
  без активности. Наша фаза 5 (TrustClass-промоушен) — **активный**
  ап/даун по evidence. Два канала не пересекаются (passive
  timeout-only vs evidence-driven); в `design.md` §D7 зафиксировано.
- **`agent-runtime-capabilities` §Phase D (Worker secret injection)**
  — секреты в `ComputeStartRequest.SecretRefs`; Docker/K8s providers
  резолвят и export. Ничего из этого этот change не дублирует.
- **`harden-pi-worker-sandbox`** — клонирование SourceGit, egress
  fail-closed, hardening (CPU/memory limits, non-root). Ничего из
  этого этот change не дублирует. **Bug-fix reading pi `ExitCode`**
  батч — отдельный; наш базовый branch (фаза 1, `worker.exit_code`
  journal event) — не дублирует и не отменяет.
- **`rescue/redis-cache`** — Redis восстанавливается для read-cache
  (`ProjectSettingsCacheRefresher`); trade-off §D6 отвергает Redis
  для command-hub (стоит дороже, см. таблицу). Если в будущем Redis
  станет required для всех deploy'ев (отдельное решение) — этот
  change пересмотривается.
- **OpenSpec capabilities directory** — три MODIFIED-каталога: `worker-runtime`
  / `observability` / `runs`. Главное `openspec/specs/` дерево не
  редактируется в этом change — дельты архивируются в main, когда
  каждая фаза приземлится.

## One-Legacy Right (закрытый после-фикса, не в спеке)

Чтение pi `ExitCode` end-to-end (Translator идёт через `cancelled` /
`failed` по exit-code, не от «wait for StageReport») — базовая ветка
(фаза 1) журналирует `worker.exit_code` event с кодом и safe stderr
detail. **Авторитетное** решение Translator'а «exit-code = failed» —
отдельный bug-батч, потому что это меняет контурный state machine
(«`StageReport` — единственный авторитетный finish»), а это
задевает `runs` capability. В этом change делается exit-code *видимым*,
не *полномочным*.
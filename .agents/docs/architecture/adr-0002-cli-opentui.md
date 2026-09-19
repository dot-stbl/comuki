# ADR-0002 — CLI presentation stack: OpenTUI Core + keymap; React deferred

- **Статус:** Accepted (Core path) · Deferred (React path on Windows)
- **Дата:** 2026-09-19
- **Контекст:** issue #72 — child of the CLI rebuild epic issue #71
  (driver of "CLI presentation ADR — Core vs React decision").
- **Issue:** https://github.com/dot-stbl/comuki/issues/72
- **Related ADR:** [adr-0001-ui-kit-react-aria.md](./adr-0001-ui-kit-react-aria.md) —
  dashboard UI kit decision (parallel work, unrelated TUI surface).

## Контекст

Comuki — полиглот-монорепо с C#/.NET 10 платформой (`platform/`),
TS-агентским слоем (`agents/`) и TS-React дашбордом (`dashboard/`).
CLI в v1 — TS-терминальный клиент к оркестратору (`cli/`), который
**сегодня** построен на `ink@5.2.0` + `react@18.3.1` + `ink-text-input`
+ `clipboardy`. Этот стек проверен; Ink-компонентные тесты в `cli/`
зелёные. Issue #72 предлагает OpenTUI как альтернативу — рендер-стек
на нативном zig-ядре с React-обёрткой (`@opentui/react`).

Цель spike: решить **Core vs React** для v1 CLI на основе измеримых
доказательств в `cli/spikes/opentui/`, а не общих соображений.

## Что фактически задокументировано в коде v1 (Ink-стек)

Эти факты взяты из текущего кода в `cli/src/` и `cli/scripts/build.ts`.
Они единственные, на которых ADR основывается; всё остальное про Ink —
общие соображения и ADR не претендует на их измерение.

- **`cli/src/components/PromptInput.tsx` строки 1–17** —
  `PromptInput` — собственный редактор: «the multi-session shell
  needs `tab`, `esc`, `ctrl+n` / `ctrl+w` as global hotkeys, and
  ink-text-input would insert the letter of a ctrl-combo into the
  buffer — there is no way to consume a key before another
  `useInput` listener sees it». Конфликт ownership у `useInput`
  между shell hotkeys и редактором — главная причина кастомного
  редактора.
- **`cli/src/hooks/useCopyLastAnswer.ts` строки 1–8** — `Ink fans
  every key out to all active listeners and PromptInput ignores
  ctrl-combos, so the hook cannot clash with the editor`. Тот же
  fan-out + отсутствие key-priority API у `useInput`.
- **`cli/src/hooks/useHomeEndKeys.ts` строки 1–11** — `Ink 5's
  useInput key object stops at arrows / page keys — Home and End are
  parsed (parseKeypress knows their names) but never exposed on the
  key flags, and their input is blanked for non-alphanumeric keys».
  На Ink пришлось навесить side-channel через `internal_eventEmitter`
  чтобы ловить Home/End вообще.
- **`cli/src/hooks/useMouse.ts` строки 1–7** — `Ink 5's useInput
  never surfaces mouse reports`. Mouse-tracking приходится
  подключать руками: `useMouse` пишет DECSET 1000/1006/1002 в
  stdout и слушает внутренний emitter.
- **`cli/src/theme.ts` строки про chrome** — `Ink 5 paints chrome
  as filled rectangles (backgroundColor hex on Box/Text)`. Нет
  z-plane / shadow API, нет opacity per renderable. Хром
  реализуется через `backgroundColor` на `<Box>` / `<Text>`.
- **`cli/scripts/build.ts`** — `ink@5 statically traces
  react-devtools-core (a DEV-only dynamic import in its
  reconciler)`. Это объясняет, почему `bun build --compile`
  требовал stub-плагин в production.

Эти пять наблюдений — единственное эмпирическое основание для
сравнения Ink ↔ OpenTUI в этом ADR. Дополнительные претензии к Ink
(«React-DOM-style reconciler», «каждый keystroke идёт через
React-DOM», «задержка в миллисекунды», «escape-перформанс»,
«main-screen по умолчанию») — это **не** задокументировано в текущем
коде и **не** измерено в этом spike. Они удалены из этого ADR.

## Решение

### 1. TUI-стек: OpenTUI Core + keymap (принято)

Для v1 CLI берём **Core**-путь OpenTUI плюс `@opentui/keymap` для
keymap-движка. Это означает:

- **Host пишется на `@opentui/core`** — никакого React-слоя между
  бизнес-логикой и рендером. Renderables (`BoxRenderable`,
  `ScrollBoxRenderable`, `TextRenderable`, `TextareaRenderable`,
  `SelectRenderable`) собираются напрямую.
- **Keystrokes роутим через `@opentui/keymap`** с официальным
  OpenTUI-адаптером `createOpenTuiKeymap(renderer)`. Palette и
  keybinding dispatch через одну поверхность: spike-овский
  `commands.test.ts` ассертит ровно 2 инвокации
  (palette/name + chord) одного и того же обработчика, без
  unbound-key побочных эффектов.
- **Domain/application код не имеет зависимости на OpenTUI или
  React.** Это инвариант, а не пожелание — `bun run test:core`
  зелёный на 34/34 (34 pass / 0 fail / 416 expect), и `ChatShell`
  сам по себе не импортит React.
- **i18n: real i18next async initialised for en + ru, command IDs
  stay stable.** Это инвариант, а не пожелание — `i18next@^23`
  закреплён в `cli/spikes/opentui/package.json`,
  `createI18nFor(locale)` возвращает
  `Promise<I18nInstance>` (await внутри factory), а
  `buildBuiltinCommands(i18n)` читает `cmd.*.label` /
  `cmd.*.description` через `tr(i18n, key)` в момент host-construction.
  Имя команды (`BUILTIN_COMMANDS[].name`) — стабильный programming
  handle, не переводится; `tr` бросает на пустой или равный ключу
  результат, что ловит build-time drift между ресурсами. Тестовая
  матрица: en-default и ru-fresh оба зелёные через
  `instance.exists(key, { ns: "spike" })` в `tests/i18n.test.ts`.
  Audit remediation и завершено; spike не stub-модуль.
- **Lifecycle: injected seam, default no-op, renderer adapter.**
  Это инвариант, а не пожелание — `TerminalLifecycle`
  (`suspend()` / `resume()`) резолвится в
  `createChatShell` по правилу: `internals.terminalLifecycle` →
  memoryMode no-op → адаптер над `renderer.suspend()` /
  `renderer.resume()`. `suspendForEdit` вызывает `suspend()` первым;
  если он throws, editor / `onRestore` / `resume` не выполняются, и
  возвращается `{ ok: false, error }`. На успехе editor → `onRestore`
  → `finally` (`resume()` + `composer.focus()` +
  `renderer.requestRender()`) — ровно один раз. `tests/suspend.test.ts`
  ассертит каноническую последовательность через spy lifecycle
  (suspend → editor-start → editor-end → onRestore → resume) и
  отдельный тест на throw из `suspend()`. **Реальный TTY не
  проверяется этими тестами** — seam inject'ится, реальный
  renderer-adapter покрыт только type-shape.

### 2. Screen mode: alternate-screen (принято)

Берём `screenMode: "alternate-screen"`. Обоснование:

- **Focus-first UX.** Корневой контракт CLI — `›`-промпт должен
  стоять там, где его оставил пользователь. При alternate-screen
  renderer swap-in / swap-out на входе/выходе, и scrollback
  чистый. При main-screen rendering последний кадр остаётся в
  terminal scrollback после exit — главная жалоба на full-screen
  TUI-приложения, которые не swap'ают.
- **Соответствует текущему состоянию.** Текущий Ink-based CLI не
  использует alternate-screen явно (в `cli/src/index.tsx` нет
  `stdout` / `screenMode` параметра в `render()` — проверено
  grep'ом). ADR делает выбор альтернативы явным, а не выводимым
  из текущего поведения.
- **Split-footer отклонён.** Альтернатива — последние N строк
  статус-бара в main-screen, остальное в alternate. Это даёт
  видимый «дыхательный» UX, но удваивает количество отрисовываемых
  зон и не даёт ничего, что focus-first UX уже не даёт статус-баром
  внутри alternate-screen.
- **Главный экран и approval-card живут в одной coordinate
  system.** При alternate-screen resize (160x50 → 80x24 → 48x16)
  spike доказывает, что chrome + transcript + composer
  сосуществуют во всех трёх размерах (см. `tests/resize.test.ts`,
  один renderer + одна shell, draft set один раз, captureCharFrame
  на каждом размере содержит chrome + transcript marker + тот же
  draft string).

### 3. Упаковка: Bun compile в single-file бинарь (принято)

`bun build --compile` собирает Core-хост и нативный OpenTUI модуль в
один `comuki-opentui-spike[.exe]`. Тестовая сборка под Bun 1.3.10 на
Windows x64 — **120.63 MB** (126 489 600 bytes), sha256
`480fedc09b95d6c5171073c697d0617e17e8ff1c8b60b57c31db557f6e9376c4`
(см. таблицу ниже).

Это пограничный размер для дистрибуции standalone-бинаря через
полосу пропускания release-артефактов. Стратегия дистрибуции
потребует отдельного решения (сжатие / split-binary / native-binding
через `bun add` addon). ADR фиксирует это как **re-open trigger**
(см. ниже), не как blocker.

### 4. Linear accessibility fallback (planned, NOT implemented)

ADR #0002 описывает планируемое поведение: для no-TTY / pipe /
redirect режима (`comuki ... | jq`, `> log`) CLI должен
использовать linear string-based renderer, не требующий terminal
capability detection. **Это решение, не реализация.** В текущем
spike-овском state linear renderer-файлы удалены
(`src/renderer/linear.ts` и `src/renderer/linear-approval.ts`
больше не существуют, см. iteration 6). Что spike **реально**
делает для no-TTY пути — `createTestRenderer(width, height, ...)` в
тестах использует `bufferedOutput: "memory"` и захватывает frame
через `setup.captureCharFrame()` в `string`. Это test-time sink,
не production linear renderer.

**UNPROVEN на текущий момент:**
- Реальная pipe-mode поддержка в production CLI — не реализована
  и не измерена в этом spike.
- Screen-reader / a11y поведение OpenTUI — не измерено.
- «bufferedOutput memory» и «no-TTY linear renderer» — это
  разные вещи: первое test-only, второе планируемое.

Production-путь требует **отдельной** работы:
1. Реализовать linear adapter (`cli/src/lib/linear-renderer.ts`)
   как отдельный renderer, активируемый при `!process.stdout.isTTY`.
2. Определить, что linear renderer пишет — текст (для `| grep`),
   или JSON (для `| jq`), или `--format json` flag.
3. a11y-аудит OpenTUI как таковой — отдельная задача.

До этого момента linear fallback — **решение, не реализация**.
Это re-open trigger, не blocker для Core path.

### 5. Платформенная матрица

| Платформа | CLI-mode | Standalone | Тестировано в spike |
|---|---|---|---|
| Windows x64 (Bun 1.3.10) | Да | Да (compile OK) | Да (наш worktree) |
| Windows ARM64 | Не тестировано | Не тестировано | UNVERIFIED |
| Linux x64 glibc | Не тестировано | Не тестировано | UNVERIFIED |
| Linux ARM64 glibc | Не тестировано | Не тестировано | UNVERIFIED |
| Linux x64 musl | Не тестировано | Не тестировано | UNVERIFIED |
| macOS x64 / arm64 | Не тестировано | Не тестировано | UNVERIFIED |
| within SSH (tmux/screen) | Не тестировано | Не тестировано | UNVERIFIED |
| screen-reader + manual TTY | Не тестировано | Не тестировано | UNVERIFIED |

Это узкая таблица — spike выполнялся только на Windows x64 под Bun.
Любая production-готовность потребует расширения матрицы. См.
§"Re-open triggers".

### 6. Lifecycle ownership (принято)

- **Renderer / root / native feed:** lifecycle живёт в
  `CliRenderer.destroy()`. Spike tests вызывают его **ровно один
  раз** per test (см. `afterEach` в `tests/resize.test.ts`,
  `tests/suspend.test.ts`) — zero listener leaks при двух
  последовательных `bun run test:core`.
- **Keymap:** `@opentui/keymap` экспортирует
  `unregisterLayer()` на каждый `keymap.registerLayer(layer)`. Shell
  хранит disposer и вызывает его в `destroy()`.
- **Command registry (single source of truth):**
  `BUILTIN_COMMANDS` живёт в `commands/registry.ts`. Никаких
  parallel `Map<string, handler>` или дублирующих диспатчер-файлов.
  Palette-клик и key-chord идут через `keymap.runCommand(name, payload)`.

### 7. 80x24 и 48x16 поведение (доказано)

Spike содержит **единый renderer** (`createTestRenderer` один раз в
`beforeEach`), один `createChatShell`, один
`setDraft("CORE-DRAFT-PRESERVED")`, затем
`shell.setSize(160,50) → 80x24 → 48x16`. На каждом размере
`captureCharFrame` содержит:
- chrome (`comuki · opentui-spike (core) · focus-mode` или compact
  `comuki·opentui-spike` при ширине < 60),
- transcript marker (`Answer #99\d:`),
- ровно тот же draft string.

`@opentui/core` рисует 48x16 в режиме **compact layout**: chrome
короткий, composer схлопывается до 1–2 строк, viewport получает
остаток. Доказательство в `tests/resize.test.ts::at 48x16`.

### 8. Тестовая стратегия (принято)

Этот репо придерживается **integration-first** testing convention (см.
`.agents/rules/coding/TESTING-RULES.md`: xUnit v3 + Shouldly +
NSubstitute + Testcontainers, integration-first pyramid, MTP не
VSTest, coverage floor 70%). Для spike-local unit/integration suite
это означает:

| Уровень | Что проверяет | Где |
|---|---|---|
| Unit + integration (Core) | command/keymap shared surface, draft round-trip, suspend cleanup, layout invariant, fixture cardinality, i18n parity | `tests/commands.test.ts`, `tests/suspend.test.ts`, `tests/fixture-cardinality.test.ts`, `tests/i18n.test.ts` |
| Layout (Core) | chrome + transcript + composer + approval card in real `captureCharFrame` | `tests/resize.test.ts`, `tests/approval.test.ts` |
| Evidence (Core) | cold-start, fixture flatten, idle memory, native stats, standalone build size/hash | `scripts/measure-*.ts`, `scripts/build-standalone.ts` |

`bun run test:core` — это **spike-local gate**, не CI gate (CI
ещё не интегрирован для этой директории). React тестов нет, и они не
требуются до тех пор, пока решение `React deferred` остаётся в силе.
Команда gate: `bun install --frozen-lockfile; bun run typecheck;
bun run lint; bun run test:core` (×2 для стабильности).

### 9. Migration from Ink + React 18 (принято как strangler, не trivial)

**Migration is not trivial.** Текущий CLI — это большая,
покрытая-тестами code-base (Ink-компонентные тесты в `cli/` зелёные).
У него есть полезные pure seams и harness:

- `lib/multiline.ts` — pure routing логики enter/multi-line.
- `lib/keybindings.ts` — pure matching binding-chord в key-event.
- `lib/history.ts` — pure history-navigation.
- `lib/term.ts`, `lib/mouse.ts` — terminal capability detection.
- `hooks/useHomeEndKeys.ts`, `hooks/useMouse.ts`, `hooks/useCopyLastAnswer.ts` —
  side-channel hooks, обходящие ограничения Ink-а (см. §Контекст).
- `harness/` — pure harness reducer и селекторы, не зависящие от Ink.

Стратегия **strangler**, не переписывание:

1. **Сохраняем текущее поведение Ink-CLI до достижения parity.**
   Никакого «one-PR rewrite», никакой замены тестов без
   пошагового покрытия. Новый OpenTUI-host пишется как **host-adapter
   layer** к существующему domain-коду: `lib/multiline.ts`,
   `lib/keybindings.ts`, `lib/history.ts` остаются
   renderer-independent.
2. **Переносим только ClientKernel / форматтеры / projections.**
   Это core-domain operations — статус, transcripts, рендер
   формата — которые могут жить над renderer-agnostic surface.
   `commands/setup.tsx`, `commands/chat.tsx`, `commands/archive.ts` —
   постепенно.
3. **Сохраняем Ink-adapter в дереве** до тех пор, пока OpenTUI-host
   не достигнет feature parity. Это `cli/src/ink-adapter.tsx` (или
   аналогичный), который собирает существующие hooks и Ink-компоненты
   вокруг нового domain-API. Стратегия «выбросить Ink когда будет
   parity» — это явный re-open trigger, не milestone.
4. **Тесты остаются.** `ink-testing-library` тесты остаются на
   месте до тех пор, пока Ink-adapter живёт. Новый OpenTUI-host
   добавляет **отдельные** тесты (это `cli/spikes/opentui/tests/`
   сегодня). Удаление старых тестов — это отдельная задача после
   того, как OpenTUI-host достиг parity, не параллельно.
5. **Это не one-PR.** Это серия merge'ей: domain-API extraction →
   OpenTUI-host skeleton → постепенный command-by-command перенос
   → снятие Ink-adapter. Каждый merge маленький, обратносовместимый,
   тесты остаются зелёными.

### 10. Production direction (планируется, не реализовано в spike)

- **Домен (Core):** остаётся renderer-agnostic. `cli/spikes/opentui/src/core`
  — это proof-of-concept, не production код. Реальный CLI-домен живёт
  в `cli/src/`, OpenTUI-хост мигрирует туда отдельными merge'ями
  в strangler-стиле (см. §9).
- **Keymap:** переиспользуется как shared-registry; host пишет
  свой `host-*` layer (production keymap layer), поверх
  BUILTIN_COMMANDS.
- **Clipboard:** clipboard остаётся application-service (как и в
  Ink-CLI сейчас). OpenTUI **не** выпиливает clipboard-shim;
  встроенные примитивы (mouse, input, keymap, focus) устраняют
  текущие Ink-обходные пути (см. §Контекст), но migration
  clipboard-сервиса остаётся отдельной задачей.
- **Linear fallback:** требует отдельной работы (см. §4).

---

## Доказательства (только этот spike, Windows x64, Bun 1.3.10)

Сырые stdout-капчуры в репозиторий **не сохраняются** (см.
`cli/spikes/opentui/.gitignore`). ADR ссылается на скрипты, которые
перезапускаются в любой момент на этом же worktree. Численные
значения ниже — это **записанный decision sample** (single run) в
момент принятия ADR (см. колонку «Скрипт»). Скрипты теперь дают
разные значения при каждом запуске — таблица **не** обновляется
автоматически.

| Метрика | Значение | Скрипт |
|---|---|---|
| Cold start + first paint (80x24) | **94 ms** total (renderer 6 ms + paint 88 ms) | `bun run measure:cold-start` (одна проба, не benchmark) |
| First-frame byte size | **1 944 bytes** (25 split rows in the buffer, canonical 24-row canvas with trailing line) | same |
| Fixture build (1 000 entries) | **0.94 ms** build, **1 000 entries** (audit fix: cardinality now exact, was 1 091 with extra `code` rows), **1 360 flattened rows** | `bun run measure:fixture` |
| Fixture flatten at width 80 (focus-mode) | **0.71 ms** | same |
| Idle RSS delta over **5 s** window | **+55 MB** (process delta — spike **не** может изолировать причины; OpenTUI native test renderer, Bun runtime, и тестовый overhead могут всё вносить вклад) | `bun run measure:idle-memory` |
| Idle heap after 5 s | **14 MB** (тот же caveat — spike не может изолировать компоненты; наблюдаемое process-level значение) | same |
| Native frame stats (5 frames after 3 setDraft cycles) | `nativeLastFrameTime: 31814`, `nativeAverageFrameTime: 26727.4`, `nativeFrameCount: 5`, `cellsUpdated: 6`, `averageCellsUpdated: 396`, `nativeRenderTime: 8`, `nativeStdoutWriteTime: 0` (raw integer values; units **not** verified by API field names) | `bun run measure:native-stats` |
| Standalone `comuki-opentui-spike.exe` size | **120.64 MB** (126 496 768 bytes) | `bun run measure:standalone` |
| Standalone sha256 | `860e0a8430f1dec9c82e847138f49cf8df3fa9ace0b75a7803a58db2a11e6818` | same |
| Bun version | 1.3.10 | same |
| Platform / arch | win32 / x64 | same |
| Smoke-load (evidence that the public surface resolves) | BoxRenderable / InputRenderable / ScrollBoxRenderable / TextRenderable / SelectRenderable / TextareaRenderable / createCliRenderer — **все import'ятся** | `bun run smoke:load` (smoke, **не** behavioral proof) |
| `bun run test:core` × 2 | **34 pass / 0 fail / 416 expect** per run, no listener leaks, no native-allocation failures (`tests/commands.test.ts`, `tests/approval.test.ts`, `tests/resize.test.ts`, `tests/suspend.test.ts`, `tests/fixture-cardinality.test.ts`, `tests/i18n.test.ts`) — counts updated from 29/511 to 34/416 after real i18next integration + lifecycle-seam audit remediation. i18next (`createI18nFor`, `tr`, `buildBuiltinCommands`) and `TerminalLifecycle` injection (`suspend` → editor → `onRestore` → `resume`, plus throw-on-suspend path) are the additions. | manual |
| `bun run typecheck` | exit 0 | `bunx tsc --noEmit -p .` |
| `bun run lint` | exit 0 (zero warnings) | `bun run lint` |

> Источник всех measurement scripts: [`cli/spikes/opentui/scripts/`](../../../cli/spikes/opentui/scripts/).
> Они остаются в репозитории — перезапускаемы в любой момент на этом
> же worktree. Сырые stdout-капчуры в репо НЕ хранятся (ADR ссылается
> на скрипты, а не на дампы).

> **Honest framing of memory numbers.** `+55 MB` RSS delta за
> **5 секунд** — это process-level наблюдение, и spike **не**
> может изолировать, какая часть приходит от OpenTUI native
> test renderer, от Bun-runtime, от тестового harness, или от
> spike-хостa. В таблице записано наблюдаемое значение + явный
> caveat.
>
> **Honest framing of cold start.** `94 ms` — это **записанный
> decision sample** (single run) на этом worktree, **не** benchmark.
> `bun run measure:cold-start` подвержен JIT-warmup, GC, и
> OpenTUI first-frame allocation; повторный запуск даёт другие
> ±10–30 ms. Значения в таблице ниже — с одной пробой; см. ниже
> про «recorded decision sample».

---

## Отклонённые альтернативы

### A. Оставить Ink + React 18 как есть

`Ink@5.2.0` + `React@18.3.1` в `cli/package.json`. Текущая code-base
**уже** работает на этом стеке — Ink-компонентные тесты в `cli/`
зелёные. **Причины отклонения не через invented limitations**, а
через пять конкретных шимов, перечисленных в §Контекст (см.
выше): собственные hooks для Home/End / mouse-tracking / clipboard
потому что `useInput` fan-out и key-flag limitations Ink-а не
позволяют решить задачу декларативно. Это не «React-DOM-style
reconciler тормозит», это **fan-out + key-flag limits**,
документированные в нашем же коде. **Что это значит для решения:**
ADR не сравнивает производительность Ink ↔ OpenTUI в
миллисекундах (не измерено) и не претендует на это. Преимущество
OpenTUI — это структурное отсутствие тех пяти шимов через
встроенные примитивы (mouse, input, keymap, focus), а не «быстрее
на N ms».

### B. OpenTUI React сразу

`@opentui/react@0.5.11` даёт React 19 reconciler поверх OpenTUI
нативного движка. В Windows x64 под Bun 1.3.10 spike не смог
сделать React-путь детерминированным: state-update через
`act + flushSync + waitForVisualIdle` не доезжал до captured frame,
resize 80x24 → 48x16 терял draft, повторные runs давали разные
числа pass/fail (полный лог в
`cli/spikes/opentui/evidence/react-windows.md`). **Причины
отклонения:** (a) React reconciler в spike не прокидывает
`ref`-коллбэки intrinsic'ов (`<textarea>`, `<select>`, `<box>`), а
у этих intrinsic'ов нет in-place value sync из React state —
`initialValue` читается только при mount; (б) concurrent-mode
планирование расходится с OpenTUI paint-loop, `flushSync()` из
`@opentui/react` недостаточен; (в) `<select>` и `<textarea>`
auto-sized в parent flex-контейнере, без явного `height` второй
option клипается. **Это не значит, что OpenTUI React сломан в
целом** — это значит, что наш стек × эта ОС × этот test path не
даёт честных доказательств в iteration budget. **Re-open trigger:**
см. ниже.

### C. Bubble Tea (`bubbletea` / `tview`)

Production-grade Go-стек, проверенный годами. **Причины отклонения:**
это добавило бы **третий CLI язык и toolchain** (Go) к репо,
где CLI/agent surfaces — TypeScript под Bun, а платформа —
C#/.NET. ADR **не** утверждает, что Comuki «all-TS»: C#/.NET есть
и остаётся; только CLI/agent stack — TS. Добавлять Go только ради
TUI-стека не оправдано; (в) Bubbletea-компоненты не
переиспользуют наши domain-типы из `Comuki.Shared.Contracts`.

### D. Ratatui (`ratatui` / `tui-rs`)

Production-grade Rust-стек. **Причины отклонения:** те же, что у
Bubble Tea (третий язык/toolchain, C# остаётся), плюс ещё более
холодный toolchain в этом репо (Rust toolchain не установлен, CI
под bun).

---

## Re-open triggers

Этот ADR пересматривается, **когда любое** из:

1. **OpenTUI React становится стабильным на нашем стеке.**
   Сейчас в [`cli/spikes/opentui/evidence/react-windows.md`](../../../cli/spikes/opentui/evidence/react-windows.md)
   записаны конкретные воспроизведения и наблюдения. Если будущая
   итерация (новый `@opentui/react@^0.6.x`, новая ОС, новый React
   reconciler API) делает все шесть кейсов зелёными — ADR
   пересматривается.
2. **Production target добавляет новую ОС** (Linux server, macOS
   desktop). Сейчас spike выполнялся только на Windows x64. Любая
   production-готовность требует расширения платформенной матрицы
   (§5). Это отдельный sprint, не часть ADR.
3. **Появляется реальный screen-reader / a11y-кейс** для CLI.
   Сейчас linear fallback — это **решение, не реализация** (§4).
   a11y-аудит OpenTUI как таковой не делался.
4. **CLI вырастает за пределы focus-first UX.** §2 описывает, почему
   alternate-screen — это правильный выбор для focus-first. Если
   появятся use-cases, где main-screen важнее (например, embed в
   shell-prompt как `git`), screen-mode нужно будет пересмотреть.
5. **Standalone-размер 120 MB** становится узким местом
   дистрибуции (release-артефакты, CI bandwidth). Это уже сейчас
   пограничный размер; сжатие / split / native-binding через
   `bun add` addon — это отдельный инженерный проект, не часть ADR.

## Последствия

**Плюсы (на текущей ОС / стеке):**
- честные 34/34 тестов (416 expect) с двумя последовательными
  прогонами без listener-leak и native-allocation ошибок;
- structural-устранение пяти documented Ink-шимов
  (Home/End, mouse, clipboard, custom editor) — ADR не претендует
  на ms-метрики;
- единая keymap-поверхность для palette + keybindings (доказано
  counter-тестом).

**Минусы / честные неизвестные:**
- Linear / a11y / pipe-mode fallback — **planned, not implemented**
  (§4);
- Linux / macOS / SSH / tmux / screen / ручной TTY не тестировались;
- screen-reader поведение не измерено;
- 120.63 MB standalone — пограничный размер для релизов;
- React-путь deferred; если позже понадобится — это отдельная
  работа;
- нет production runtime-данных (v1 ещё не зарелижен);
- native-stats sample показывает 5 frames после 3 setDraft-циклов —
  это подтверждает, что рендер работает, но **не** даёт
  production-grade fps под нагрузкой (1000 transcript-entries,
  parallel feeds, sustained typing).

## Related

- Issue #72: https://github.com/dot-stbl/comuki/issues/72
- Issue #71: https://github.com/dot-stbl/comuki/issues/71 (epic — CLI
  presentation ADR driver)
- [`cli/spikes/opentui/evidence/react-windows.md`](../../../cli/spikes/opentui/evidence/react-windows.md) —
  отложенное React-решение с воспроизведениями.
- [`cli/spikes/opentui/README.md`](../../../cli/spikes/opentui/README.md) —
  spike contract: throwaway, не production dependency.
- [`.agents/rules/coding/TESTING-RULES.md`](../../rules/coding/TESTING-RULES.md) —
  testing convention в этом репо (integration-first, xUnit v3
  unit/integration pyramid, MTP не VSTest, coverage floor 70%).
- [`.agents/rules/coding/frontend-construct-rules.md`](../../rules/coding/frontend-construct-rules.md) —
  React conventions (для справки при будущем re-open React-пути).
- [`.agents/docs/architecture/comuki-stack.md`](./comuki-stack.md) —
  общий стек платформы, куда этот CLI integration встаёт.
- [`.agents/docs/architecture/adr-0001-ui-kit-react-aria.md`](./adr-0001-ui-kit-react-aria.md) —
  параллельное решение по UI-киту дашборда; это ADR про TUI-стек
  CLI.

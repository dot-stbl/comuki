/**
 * i18next integration for the OpenTUI Core host (production).
 *
 * Moved/adapted from the ADR-0002 spike (`cli/spikes/opentui/src/locales`)
 * — same contract, new `tui` namespace and the production command set:
 *
 * - `createI18nFor(locale)` returns a fully-initialized instance
 *   (`Promise<I18nInstance>`, `init()` awaited) with en + ru parity;
 * - `tr(instance, key)` throws on a missing/empty resolution, so key
 *   drift between locale resources fails loudly in tests;
 * - command **identity** (`TUI_COMMANDS[].name`) is never translated —
 *   it is the stable handle the keymap dispatches by; only labels,
 *   descriptions, placeholders and approval-card copy localize.
 */

import i18next, { type i18n } from "i18next"

export type I18nInstance = i18n

export type LocaleCode = "en" | "ru"

export const LOCALES: readonly LocaleCode[] = ["en", "ru"] as const

export const DEFAULT_LOCALE: LocaleCode = "en"

export const TUI_NAMESPACE = "tui" as const

/**
 * The closed set of dotted keys the TUI host uses. Every key MUST be
 * present in every locale — `locales/i18n.test.ts` asserts parity.
 */
export const REQUIRED_KEYS = [
  "chrome.title",
  "chrome.titleCompact",
  "chrome.budgets",
  "chrome.exportBundleHint",
  "chrome.versionMismatch",
  "connection.connected",
  "connection.connecting",
  "connection.reconnecting",
  "connection.disconnected",
  "connection.connectedShort",
  "connection.connectingShort",
  "connection.reconnectingShort",
  "connection.disconnectedShort",
  "composer.placeholder",
  "composer.placeholderCompact",
  "transcript.welcome.lead",
  "transcript.welcome.ctaAsk",
  "transcript.welcome.ctaSlash",
  "transcript.welcome.ctaPalette",
  "transcript.you",
  "transcript.comuki",
  "transcript.system",
  "transcript.tool",
  "transcript.thinking",
  "transcript.failed",
  "transcript.emptySession",
  "transcript.welcome.lead",
  "transcript.welcome.ctaAsk",
  "transcript.welcome.ctaSlash",
  "transcript.welcome.ctaPalette",
  "transcript.entry.thinking",
  "transcript.entry.tool",
  "transcript.entry.tools",
  "transcript.entry.code",
  "transcript.entry.diff",
  "transcript.entry.plan",
  "transcript.entry.handoff",
  "transcript.entry.stateOk",
  "transcript.entry.stateError",
  "transcript.entry.stateRunning",
  "transcript.entry.at",
  "transcript.entry.id",
  "transcript.entry.input",
  "transcript.entry.output",
  "transcript.entry.omitted",
  "transcript.entry.lineCount",
  "transcript.entry.nodeCount",
  "transcript.entry.approval",
  "transcript.entry.approval.stackedHeader",
  "transcript.palette.approve",
  "transcript.palette.reject",
  "transcript.palette.showReceipt",
  "transcript.swarm.canvasHeader",
  "transcript.swarm.canvasEmpty",
  "transcript.swarm.priority.p0",
  "transcript.swarm.priority.p1",
  "transcript.swarm.priority.p2",
  "transcript.swarm.empty",
  "transcript.swarm.stalled",
  "transcript.swarm.awaiting",
  "transcript.swarm.evidence",
  "transcript.swarm.failed",
  "transcript.swarm.active",
  "transcript.swarm.durationSeconds",
  "transcript.swarm.leaseHint",
  "transcript.swarm.unreadHint",
  "transcript.swarm.failureHint",
  "transcript.swarm.detailInspect",
  "transcript.swarm.sectionDiverging",
  "transcript.palette.swarmCanvas",
  "transcript.palette.swarmCanvasRefresh",
  "transcript.palette.swarmCanvasInspect",
  "transcript.linear.awaiting",
  "transcript.linear.approved",
  "transcript.linear.rejected",
  "transcript.linear.stalled",
  "transcript.linear.receiptWritten",
  "transcript.linear.canvasEmpty",
  "transcript.linear.mode",
  "transcript.linear.swarmHeader",
  "transcript.linear.transcriptHeader",
  "transcript.session.home.sessionsHeader",
  "transcript.session.home.empty",
  "transcript.session.home.offline",
  "transcript.session.home.recovering",
  "transcript.session.home.online",
  "transcript.session.palette.resume",
  "transcript.session.palette.rename",
  "transcript.session.palette.archive",
  "transcript.session.palette.fork",
  "transcript.draft.retained",
  "approval.intentPrefix",
  "approval.scopePrefix",
  "approval.riskPrefix",
  "approval.planPrefix",
  "approval.stepPrefix",
  "approval.diffPrefix",
  "approval.decideLabel",
  "approval.unreadablePlan",
  "approval.action.approve",
  "approval.action.reject",
  "cmd.submit-turn.label",
  "cmd.submit-turn.description",
  "cmd.cancel-turn.label",
  "cmd.cancel-turn.description",
  "cmd.approve.label",
  "cmd.approve.description",
  "cmd.reject.label",
  "cmd.reject.description",
  "cmd.new-session.label",
  "cmd.new-session.description",
  "cmd.close-session.label",
  "cmd.close-session.description",
  "cmd.exit.label",
  "cmd.exit.description",
  "cmd.help.label",
  "cmd.help.description",
  "cmd.rename-session.label",
  "cmd.rename-session.description",
  "cmd.rename-session.argsHint",
  "cmd.clear-draft.label",
  "cmd.clear-draft.description",
  "cmd.open-editor.label",
  "cmd.open-editor.description",
  "cmd.open-palette.label",
  "cmd.open-palette.description",
  "cmd.toggle-details-last.label",
  "cmd.toggle-details-last.description",
  "cmd.toggle-details.label",
  "cmd.toggle-details.description",
  "cmd.show-receipt.label",
  "cmd.show-receipt.description",
  "cmd.toggle-swarm-canvas.label",
  "cmd.toggle-swarm-canvas.description",
  "cmd.swarm-canvas-refresh.label",
  "cmd.swarm-canvas-refresh.description",
  "cmd.swarm-canvas-inspect.label",
  "cmd.swarm-canvas-inspect.description",
  "cmd.swarm-canvas-inspect.argsHint",
  "cmd.session-list.label",
  "cmd.session-list.description",
  "cmd.session-resume.label",
  "cmd.session-resume.description",
  "cmd.session-resume.argsHint",
  "cmd.session-rename.label",
  "cmd.session-rename.description",
  "cmd.session-rename.argsHint",
  "cmd.session-archive.label",
  "cmd.session-archive.description",
  "cmd.session-archive.argsHint",
  "cmd.session-fork.label",
  "cmd.session-fork.description",
  "cmd.session-fork.argsHint",
  "menu.commandsTitle",
  "menu.commandsHint",
  "palette.title",
  "palette.placeholder",
  "palette.empty",
  "help.title",
  "queue.prefix",
  "queue.moreSuffix",
  "cli.unknownTuiHost",
  "cli.tuiHostsAvailable",
] as const

export type RequiredKey = (typeof REQUIRED_KEYS)[number]

/** English resource — default, source of truth. */
export const EN = {
  chrome: {
    title: "  comuki · opentui (core) · focus-mode",
    titleCompact: " comuki",
    // Issue #81 — performance budgets surfaced through the REPL.
    // The first row mirrors `scripts/budgets.ts`; the rest are
    // host-level copy for `--explain-budgets` and the export
    // hint surfaced after a render / dispatch failure.
    budgets: "performance budgets",
    exportBundleHint:
      "render error — run `comuki export-bundle <path>` to attach a bug report",
    versionMismatch:
      "client {client} is incompatible with server {server} — please upgrade the CLI",
  },
  connection: {
    connected: "connected",
    connecting: "connecting",
    reconnecting: "reconnecting",
    disconnected: "offline",
    connectedShort: "live",
    connectingShort: "…",
    reconnectingShort: "retry",
    disconnectedShort: "off",
  },
  composer: {
    placeholder: "Ask Comuki. Enter to send, ctrl+n for a new session.",
    placeholderCompact: "ask ›",
  },
  transcript: {
    you: "you ›",
    comuki: "comuki ›",
    system: "system ›",
    tool: "tool ›",
    thinking: "comuki …",
    failed: "× turn failed:",
    emptySession: "no open session — ctrl+n to start one",
    welcome: {
      lead: "Describe the outcome you want. Comuki coordinates the work.",
      ctaAsk: "  > ask a question or describe a task",
      ctaSlash: "  / open an action by name",
      ctaPalette: "  ctrl+p browse actions · esc browse sessions",
    },
    entry: {
      thinking: "thinking",
      tool: "tool",
      tools: "tools",
      code: "code",
      diff: "diff",
      plan: "plan",
      handoff: "open",
      stateOk: "ok",
      stateError: "error",
      stateRunning: "running",
      at: "at",
      id: "id",
      input: "input",
      output: "output",
      omitted: "+{{lines}} lines omitted",
      lineCount: "{{lines}} lines",
      nodeCount: "{{nodes}} nodes",
      approval: {
        label: "approval",
        stackedHeader: "{{nodes}} nodes",
      },
    },
    palette: {
      approve: "approve",
      reject: "reject",
      showReceipt: "show receipt",
      swarmCanvas: "open swarm canvas",
      swarmCanvasRefresh: "refresh swarm canvas",
      swarmCanvasInspect: "inspect swarm item",
    },
    swarm: {
      canvasHeader: "swarm · attention",
      canvasEmpty: "swarm quiet — no workers need attention",
      priority: {
        p0: "p0 · needs you",
        p1: "p1 · review",
        p2: "p2 · running",
      },
      empty: "(none)",
      stalled: "stalled",
      awaiting: "awaiting approval",
      evidence: "evidence unread",
      failed: "failed",
      active: "running",
      durationSeconds: "{{seconds}}s",
      leaseHint: "no heartbeat for {{seconds}}s",
      unreadHint: "unread output",
      failureHint: "turn failed · {{code}}",
      detailInspect: "inspect",
      sectionDiverging: "·",
    },
    linear: {
      awaiting: "Awaiting your decision · scope: {{scope}}",
      approved: "Approved: {{scope}}",
      rejected: "Rejected: {{scope}}",
      stalled: "Worker stalled",
      receiptWritten: "Receipt written for session {{id}}",
      canvasEmpty: "Canvas empty — no workers need attention",
      mode: "Mode: {{flags}}",
      swarmHeader: "swarm",
      transcriptHeader: "transcript",
    },
    session: {
      home: {
        sessionsHeader: "sessions",
        empty: "no sessions — ctrl+n to start one",
        offline: "offline",
        recovering: "reconnecting…",
        online: "live",
      },
      palette: {
        resume: "Resume session",
        rename: "Rename session",
        archive: "Archive session",
        fork: "Fork session",
      },
    },
    draft: {
      retained: "draft retained — reconnecting",
    },
  },
  approval: {
    intentPrefix: "intent:",
    scopePrefix: "scope:",
    riskPrefix: "risk:",
    planPrefix: "plan:",
    stepPrefix: "›",
    diffPrefix: "diff:",
    decideLabel: "decide: y = approve, n = reject",
    unreadablePlan: "(plan payload unreadable)",
    action: {
      approve: "approve",
      reject: "reject",
    },
  },
  cmd: {
    "submit-turn": {
      label: "Submit turn",
      description: "Send the composer draft as a new turn.",
    },
    "cancel-turn": {
      label: "Cancel running turn",
      description: "Stop listening to the turn in flight (/stop).",
    },
    approve: {
      label: "Approve pending plan",
      description: "Approve the plan awaiting approval.",
    },
    reject: {
      label: "Reject pending plan",
      description: "Reject the plan awaiting approval.",
    },
    "new-session": {
      label: "New session",
      description: "Open a new pending session tab.",
    },
    "close-session": {
      label: "Close session",
      description: "Close the active session tab (the server keeps running).",
    },
    exit: {
      label: "Exit",
      description: "Stop the kernel, restore the terminal, quit.",
    },
    help: {
      label: "Help",
      description: "List every registered command with its binding.",
    },
    "rename-session": {
      label: "Rename session",
      description: "Rename the active session (/rename <title>).",
      argsHint: "<title>",
    },
    "clear-draft": {
      label: "Clear draft",
      description: "Empty the composer and drop the saved draft.",
    },
    "open-editor": {
      label: "Edit in $EDITOR",
      description: "Edit the draft in the external editor (ctrl+e).",
    },
    "open-palette": {
      label: "Command palette",
      description: "Open the fuzzy command palette (ctrl+p).",
    },
    "toggle-details-last": {
      label: "Toggle last entry details",
      description:
        "Expand or collapse the newest collapsed transcript entry (ctrl+o).",
    },
    "toggle-details": {
      label: "Toggle all entry details",
      description:
        "Expand every transcript entry, or collapse them all (ctrl+shift+o).",
    },
    "show-receipt": {
      label: "Show approval receipt",
      description:
        "Open the session's decision ledger (the NDJSON file of every approve/reject).",
    },
    "toggle-swarm-canvas": {
      label: "Toggle swarm canvas",
      description:
        "Show or hide the attention-first swarm canvas (ctrl+shift+a).",
    },
    "swarm-canvas-refresh": {
      label: "Refresh swarm canvas",
      description: "Re-derive the swarm attention list from the kernel snapshot.",
    },
    "swarm-canvas-inspect": {
      label: "Inspect swarm item",
      description:
        "Open the deep-link to a worker or approval id on the canvas.",
      argsHint: "<id>",
    },
    "session-list": {
      label: "List sessions",
      description: "Show the durable session list (the home view).",
    },
    "session-resume": {
      label: "Resume session",
      description: "Open one durable session by id.",
      argsHint: "<id>",
    },
    "session-rename": {
      label: "Rename session",
      description: "Rename one durable session by id.",
      argsHint: "<id> <new-name>",
    },
    "session-archive": {
      label: "Archive session",
      description: "Soft-delete one durable session by id.",
      argsHint: "<id>",
    },
    "session-fork": {
      label: "Fork session",
      description: "Fork one durable session into a new conversation.",
      argsHint: "<id>",
    },
  },
  menu: {
    commandsTitle: "commands",
    commandsHint: "arrows select · tab/enter complete · esc close",
  },
  palette: {
    title: "command palette",
    placeholder: "type to filter…",
    empty: "no matching commands",
  },
  help: {
    title: "commands · esc to close",
  },
  queue: {
    prefix: "queued:",
    moreSuffix: "more",
  },
  cli: {
    unknownTuiHost: "unknown --tui host:",
    tuiHostsAvailable: "available: opentui (default), ink",
  },
} as const

/** Russian resource — parallel to EN, same key set. */
export const RU = {
  chrome: {
    title: "  comuki · opentui (core) · focus-mode",
    titleCompact: " comuki",
    budgets: "бюджеты производительности",
    exportBundleHint:
      "ошибка отрисовки — выполните `comuki export-bundle <path>`, чтобы приложить отчёт",
    versionMismatch:
      "клиент {client} несовместим с сервером {server} — обновите CLI",
  },
  connection: {
    connected: "связь установлена",
    connecting: "подключение",
    reconnecting: "переподключение",
    disconnected: "нет связи",
    connectedShort: "live",
    connectingShort: "…",
    reconnectingShort: "retry",
    disconnectedShort: "off",
  },
  composer: {
    placeholder: "Спроси Comuki. Enter — отправить, ctrl+n — новая сессия.",
    placeholderCompact: "спросить ›",
  },
  transcript: {
    you: "вы ›",
    comuki: "comuki ›",
    system: "система ›",
    tool: "инструмент ›",
    thinking: "comuki …",
    failed: "× ход не удался:",
    emptySession: "нет открытых сессий — ctrl+n чтобы начать",
    welcome: {
      lead: "Опишите желаемый результат. Comuki координирует работу.",
      ctaAsk: "  > задайте вопрос или опишите задачу",
      ctaSlash: "  / откройте действие по имени",
      ctaPalette: "  ctrl+p действия · esc сессии",
    },
    entry: {
      thinking: "размышление",
      tool: "инструмент",
      tools: "инструменты",
      code: "код",
      diff: "diff",
      plan: "план",
      handoff: "открыть",
      stateOk: "ок",
      stateError: "ошибка",
      stateRunning: "выполняется",
      at: "в",
      id: "id",
      input: "вход",
      output: "выход",
      omitted: "опущено строк: {{lines}}",
      lineCount: "строк: {{lines}}",
      nodeCount: "узлов: {{nodes}}",
      approval: {
        label: "одобрение",
        stackedHeader: "{{nodes}} узлов",
      },
    },
    palette: {
      approve: "одобрить",
      reject: "отклонить",
      showReceipt: "показать запись",
      swarmCanvas: "открыть канвас роя",
      swarmCanvasRefresh: "обновить канвас роя",
      swarmCanvasInspect: "инспект элемента роя",
    },
    swarm: {
      canvasHeader: "рой · внимание",
      canvasEmpty: "рой тих — никому не нужно внимание",
      priority: {
        p0: "p0 · нужно вам",
        p1: "p1 · на ревью",
        p2: "p2 · в работе",
      },
      empty: "(нет)",
      stalled: "застрял",
      awaiting: "ждёт одобрения",
      evidence: "артефакт не прочитан",
      failed: "ошибка",
      active: "в работе",
      durationSeconds: "{{seconds}}с",
      leaseHint: "нет heartbeat {{seconds}}с",
      unreadHint: "непрочитанный вывод",
      failureHint: "ход не удался · {{code}}",
      detailInspect: "инспект",
      sectionDiverging: "·",
    },
    linear: {
      awaiting: "Ожидает вашего решения · обхват: {{scope}}",
      approved: "Одобрено: {{scope}}",
      rejected: "Отклонено: {{scope}}",
      stalled: "Воркер застрял",
      receiptWritten: "Запись сохранена для сессии {{id}}",
      canvasEmpty: "Канвас пуст — никому не нужно внимание",
      mode: "Режим: {{flags}}",
      swarmHeader: "рой",
      transcriptHeader: "транскрипт",
    },
    session: {
      home: {
        sessionsHeader: "сессии",
        empty: "нет сессий — ctrl+n чтобы начать",
        offline: "нет связи",
        recovering: "переподключение…",
        online: "live",
      },
      palette: {
        resume: "Возобновить сессию",
        rename: "Переименовать сессию",
        archive: "Архивировать сессию",
        fork: "Ответвить сессию",
      },
    },
    draft: {
      retained: "черновик сохранён — переподключение",
    },
  },
  approval: {
    intentPrefix: "замысел:",
    scopePrefix: "обхват:",
    riskPrefix: "риск:",
    planPrefix: "план:",
    stepPrefix: "›",
    diffPrefix: "diff:",
    decideLabel: "решение: y — одобрить, n — отклонить",
    unreadablePlan: "(план нечитаем)",
    action: {
      approve: "одобрить",
      reject: "отклонить",
    },
  },
  cmd: {
    "submit-turn": {
      label: "Отправить ход",
      description: "Отправить черновик композера как новый ход.",
    },
    "cancel-turn": {
      label: "Отменить текущий ход",
      description: "Перестать слушать идущий ход (/stop).",
    },
    approve: {
      label: "Одобрить ожидающий план",
      description: "Одобрить план, ожидающий решения.",
    },
    reject: {
      label: "Отклонить ожидающий план",
      description: "Отклонить план, ожидающий решения.",
    },
    "new-session": {
      label: "Новая сессия",
      description: "Открыть новую вложенную (pending) сессию.",
    },
    "close-session": {
      label: "Закрыть сессию",
      description: "Закрыть активную сессию (сервер продолжает работу).",
    },
    exit: {
      label: "Выход",
      description: "Остановить ядро, восстановить терминал, выйти.",
    },
    help: {
      label: "Справка",
      description: "Показать все зарегистрированные команды и их клавиши.",
    },
    "rename-session": {
      label: "Переименовать сессию",
      description: "Переименовать активную сессию (/rename <название>).",
      argsHint: "<название>",
    },
    "clear-draft": {
      label: "Очистить черновик",
      description: "Опустошить композер и убрать сохранённый черновик.",
    },
    "open-editor": {
      label: "Открыть в $EDITOR",
      description: "Править черновик во внешнем редакторе (ctrl+e).",
    },
    "open-palette": {
      label: "Палитра команд",
      description: "Открыть палитру команд с фильтром (ctrl+p).",
    },
    "toggle-details-last": {
      label: "Подробности последней записи",
      description:
        "Раскрыть или свернуть последнюю свёрнутую запись транскрипта (ctrl+o).",
    },
    "toggle-details": {
      label: "Подробности всех записей",
      description:
        "Раскрыть все записи транскрипта или свернуть их (ctrl+shift+o).",
    },
    "show-receipt": {
      label: "Показать запись об одобрении",
      description:
        "Открыть журнал решений сессии (NDJSON-файл каждого одобрения/отклонения).",
    },
    "toggle-swarm-canvas": {
      label: "Канвас роя",
      description:
        "Показать или скрыть канвас роя с приоритетами внимания (ctrl+shift+a).",
    },
    "swarm-canvas-refresh": {
      label: "Обновить канвас роя",
      description: "Перестроить список внимания роя из снимка ядра.",
    },
    "swarm-canvas-inspect": {
      label: "Инспект элемента роя",
      description:
        "Открыть deep-link на воркер или approval id в канвасе.",
      argsHint: "<id>",
    },
    "session-list": {
      label: "Список сессий",
      description: "Показать долговременный список сессий (главный экран).",
    },
    "session-resume": {
      label: "Возобновить сессию",
      description: "Открыть долговременную сессию по id.",
      argsHint: "<id>",
    },
    "session-rename": {
      label: "Переименовать сессию",
      description: "Переименовать долговременную сессию по id.",
      argsHint: "<id> <новое-название>",
    },
    "session-archive": {
      label: "Архивировать сессию",
      description: "Мягко удалить долговременную сессию по id.",
      argsHint: "<id>",
    },
    "session-fork": {
      label: "Ответвить сессию",
      description: "Ответвить долговременную сессию в новый диалог.",
      argsHint: "<id>",
    },
  },
  menu: {
    commandsTitle: "команды",
    commandsHint: "стрелки — выбор · tab/enter — подставить · esc — закрыть",
  },
  palette: {
    title: "палитра команд",
    placeholder: "фильтр…",
    empty: "нет подходящих команд",
  },
  help: {
    title: "команды · esc — закрыть",
  },
  queue: {
    prefix: "в очереди:",
    moreSuffix: "ещё",
  },
  cli: {
    unknownTuiHost: "неизвестный --tui хост:",
    tuiHostsAvailable: "доступно: opentui (по умолчанию), ink",
  },
} as const

export const RESOURCES = { en: EN, ru: RU } as const

/**
 * Create a fully-initialized i18next instance for the given locale.
 * `init()` is awaited — by the time the returned promise resolves, the
 * instance's `language` and resource lookups are ready for `tr`.
 */
export async function createI18nFor(locale: LocaleCode): Promise<I18nInstance> {
  const instance = i18next.createInstance()
  await instance.init({
    resources: {
      en: { [TUI_NAMESPACE]: EN },
      ru: { [TUI_NAMESPACE]: RU },
    },
    lng: locale,
    fallbackLng: DEFAULT_LOCALE,
    ns: [TUI_NAMESPACE],
    defaultNS: TUI_NAMESPACE,
    interpolation: { escapeValue: false },
    initImmediate: false,
  })
  return instance
}

/**
 * Return the localized value for `key` on the tui namespace. Throws if
 * the resolved value is empty or equal to the key itself — that signals
 * a missing key in the active locale and the fallback, which is a build
 * bug the parity tests catch.
 */
export function tr(instance: I18nInstance, key: string): string {
  const value = instance.t(key, { ns: TUI_NAMESPACE })
  if (typeof value === "string" && value.length > 0 && value !== key) {
    return value
  }
  throw new Error(
    `i18n: missing or empty key '${key}' in locale '${instance.language}' (namespace '${TUI_NAMESPACE}')`,
  )
}

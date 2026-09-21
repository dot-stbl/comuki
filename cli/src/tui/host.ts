/**
 * The production focus-mode OpenTUI Core host (issue #73).
 *
 * `createTuiHost(kernel, options)` mounts the shell on an injected
 * renderer (production: `createCliRenderer` alternate-screen; tests:
 * `@opentui/core/testing`). Layout adapts the ADR-0002 spike's proven
 * column-flex shell:
 *
 *   ┌──────────── 1 row ────────────┐ top bar (chrome + connection)
 *   │       viewport (flexGrow=1)   │ transcript from kernel snapshots
 *   ├──────────── composer ─────────┤ pinned TextareaRenderable
 *   └─ approval (overlay) ──────────┘ card while a turn awaits approval
 *
 * The host renders DIRECTLY from kernel snapshots via the pure
 * render-model in `./view.ts` — no mirror, no legacy tabs. All
 * keystrokes flow through `@opentui/keymap` and the named-command
 * registry in `./commands.ts`.
 *
 * Import direction: this module may import `../kernel`, `../harness`,
 * `../locales` and `@opentui/*` — never `../lib` (that composition
 * lives in `commands/opentui.ts`).
 */

import {
  bold,
  BoxRenderable,
  fg,
  italic,
  ScrollBoxRenderable,
  strikethrough,
  StyledText,
  TextRenderable,
  TextareaRenderable,
  type CliRenderer,
  type TextChunk,
  underline,
} from "@opentui/core"
import type { ClientKernel } from "../kernel"
import type { AttentionListener, AttentionSignal } from "../kernel/attention"
import { activeSession } from "../harness/selectors"
import type {
  HarnessMessage,
  HarnessSession,
  ProjectId,
  SessionId,
  SessionKey,
} from "../harness/state"
import { createI18nFor, tr, type I18nInstance, type LocaleCode } from "../locales"
import { spawnExternalEditor } from "./external-editor"
import {
  LIVE_RECALL,
  recallAfterEdit,
  recallArrowsActive,
  stepRecall,
  type HistoryDirection,
  type HistoryRecallState,
} from "./history"
import {
  allTuiCommands,
  commandAvailable,
  completeSlashCommand,
  createTuiKeymap,
  filterSlashCommands,
  paletteMatches,
  parseSlashInput,
  slashMenuQuery,
  TUI_UI_LAYER_PRIORITY,
  type TuiCommandContext,
  type TuiCommandPayload,
  type TuiCommandSpec,
  type TuiKeymapSurface,
  type TuiSlashSpec,
  type TuiUiBinding,
} from "./commands"
import {
  buildTranscriptStyledLines,
  queuedFollowUpLine,
  topBarContent,
} from "./view"
import {
  buildTranscriptEntries,
  collapsibleIds,
  lastCollapsibleId,
} from "./entries"
import {
  approvalFingerprint,
  pendingApprovalEntry,
  type ApprovalEntry,
} from "./approvals"
import {
  defaultStateDirectory,
  receiptsFilePath,
  type DecisionReceipt,
} from "../kernel/receipts"
import { TONE_HEX, type Segment, type StyledLine } from "./styled"
import { renderSwarmCanvas, type SwarmCanvasContext } from "./swarmcanvas"

/**
 * Terminal lifecycle seam (adapted from the spike). Resolution order:
 * injected seam → no-op in memory mode → adapter over the renderer.
 */
export interface TerminalLifecycle {
  suspend(): void
  resume(): void
}

/**
 * The external-editor seam: edit `draft`, resolve the adopted text,
 * throw on failure (the draft then stays untouched). Production
 * defaults to `spawnExternalEditor`; memory-mode tests inject fakes.
 */
export type ExternalEditor = (draft: string) => Promise<string>

export interface TuiHostOptions {
  readonly renderer: CliRenderer
  readonly width: number
  readonly height: number
  readonly locale?: LocaleCode
  /** Pre-initialized i18next instance; omit to build a fresh one. */
  readonly i18n?: I18nInstance
  /**
   * When `true` the caller owns terminal side-effects — the lifecycle
   * seam resolves to a no-op and no external editor is available
   * unless one is injected explicitly.
   */
  readonly memoryMode?: boolean
  /** Injected lifecycle seam; wins over memoryMode/renderer adapter. */
  readonly terminalLifecycle?: TerminalLifecycle
  /** Injected external editor; wins over the memoryMode default. */
  readonly externalEditor?: ExternalEditor
  /**
   * Project every NEW session opens in (resolved by the boot path
   * from --project/config); existing sessions keep their own.
   */
  readonly newSessionProjectId?: ProjectId | null
  /** Fires once after `close()` finished the shutdown sequence. */
  readonly onExit?: () => void
}

export interface TuiHost {
  readonly renderer: CliRenderer
  readonly keymap: TuiKeymapSurface
  /** Wait for the next visual idle; tests use it as a stable seam. */
  waitForIdle(): Promise<void>
  /** Resize the shell; layout re-renders from the last snapshot. */
  setSize(width: number, height: number): Promise<void>
  setDraft(text: string): void
  getDraft(): string
  /** Which composer surface (menu/palette/help) is open, if any. */
  getSurfaceKind(): TuiSurfaceKind
  /** Teardown without touching the kernel (test cleanup path). */
  destroy(): Promise<void>
  /**
   * Full shutdown sequence: unsubscribe → keymap → kernel.stop() →
   * await kernel.whenIdle() → renderer.destroy(). Idempotent.
   */
  close(): Promise<void>
  isClosed(): boolean
  isKernelStopped(): boolean
  /**
   * Suspend the terminal, run `editor`, restore — the spike's seam
   * contract: `suspend()` first (a throw skips editor/onRestore/
   * resume), `onRestore` exactly once, `resume()` exactly once on a
   * successful suspend.
   */
  suspendForEdit<T>(
    editor: () => Promise<T> | T,
    options?: { onRestore?: () => void }
  ): Promise<
    { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: Error }
  >
}

/** The overlay surface currently owning the composer's extra keys. */
export type TuiSurfaceKind = "none" | "slash-menu" | "palette" | "help"

interface Geometry {
  readonly topBarHeight: number
  readonly composerHeight: number
}

type LayoutMode = "regular" | "compact"

/** UI-layer priority for the dynamic history-recall layer. */
const HISTORY_LAYER_PRIORITY = 150

const MENU_MAX_ROWS = 6

function pickLayoutMode(width: number, height: number): LayoutMode {
  if (width < 60 || height < 18) {
    return "compact"
  }
  return "regular"
}

function computeGeometry(height: number, mode: LayoutMode): Geometry {
  if (mode === "compact") {
    return { topBarHeight: 1, composerHeight: height >= 6 ? 2 : 1 }
  }
  return { topBarHeight: 1, composerHeight: 3 }
}

function titleFromMessage(text: string): string {
  const first = text.split("\n")[0]?.trim() ?? ""
  return first.length > 48 ? `${first.slice(0, 45)}…` : first
}

export async function createTuiHost(
  kernel: ClientKernel,
  options: TuiHostOptions
): Promise<TuiHost> {
  const renderer = options.renderer
  const lifecycle: TerminalLifecycle =
    options.terminalLifecycle ??
    (options.memoryMode === true
      ? { suspend: () => {}, resume: () => {} }
      : {
          suspend: () => {
            renderer.suspend()
          },
          resume: () => {
            renderer.resume()
          },
        })
  const i18n: I18nInstance =
    options.i18n ?? (await createI18nFor(options.locale ?? "en"))

  let width = options.width
  let height = options.height
  let layoutMode = pickLayoutMode(width, height)
  let geometry = computeGeometry(height, layoutMode)
  let closed = false
  let kernelStopped = false
  let approvalUnregister: (() => void) | null = null
  let commandSeq = 0
  let lastState = kernel.snapshot().state
  const seededDrafts = new Set<string>()
  const specs: readonly TuiCommandSpec[] = allTuiCommands(i18n)

  // -- composer surface state (issue #75) ---------------------------------
  let surface: TuiSurfaceKind = "none"
  let surfaceOverlay: BoxRenderable | null = null
  let unregisterSurfaceLayer: (() => void) | null = null
  let slashDismissed = false
  let slashMatches: readonly TuiCommandSpec[] = []
  let slashIndex = 0
  let paletteQuery: TextareaRenderable | null = null
  let paletteRows: BoxRenderable | null = null
  let paletteIndex = 0
  let paletteWindowStart = 0
  let slashWindowStart = 0
  let programmaticTextChange = false
  let recall: HistoryRecallState = LIVE_RECALL
  let unregisterHistoryLayer: (() => void) | null = null

  // -- transcript progressive disclosure (issue #74) ------------------------
  // Entry ids revealed by ctrl+o / ctrl+shift+o, per session. The
  // kernel knows nothing about expansion — this is host-owned view
  // state keyed by stable entry ids (it survives re-renders).
  const expandedEntryIds = new Map<SessionKey, Set<string>>()
  /** Shared empty set — sessions with nothing expanded. */
  const EMPTY_ENTRY_SET: ReadonlySet<string> = new Set<string>()

  // -- swarm canvas (issue #78) ----------------------------------------------
  // Hidden by default; ctrl+shift+a or the palette toggles the surface.
  // While open, the viewport collapses to its last line so the canvas
  // takes the remaining vertical space — the right-pane-of-the-shell
  // metaphor from the issue, kept on the same column layout.
  let swarmSurfaceOpen = false
  let swarmSignal: AttentionSignal = kernel.attention()
  let swarmInspectedId: string | null = null
  const swarmAttentionListener: AttentionListener = (signal) => {
    swarmSignal = signal
    render()
  }
  kernel.addAttentionListener(swarmAttentionListener)
  const externalEditor: ExternalEditor | null =
    options.externalEditor ??
    (options.memoryMode === true ? null : spawnExternalEditor)

  function nextCommandId(prefix: string): string {
    commandSeq += 1
    return `${prefix}-${commandSeq}-${Date.now()}`
  }

  /** One styled transcript line → a chunk-per-segment StyledText. */
  function styledContent(line: StyledLine): StyledText {
    if (line.length === 0) {
      return new StyledText([fg(TONE_HEX.text)(" ")])
    }
    return new StyledText(line.map(segmentChunk))
  }

  function segmentChunk(segment: Segment): TextChunk {
    let chunk: TextChunk = fg(TONE_HEX[segment.tone])(segment.text)
    if (segment.style.bold) {
      chunk = bold(chunk)
    }
    if (segment.style.italic) {
      chunk = italic(chunk)
    }
    if (segment.style.underline) {
      chunk = underline(chunk)
    }
    if (segment.style.strike) {
      chunk = strikethrough(chunk)
    }
    return chunk
  }

  const root = renderer.root
  const shell = new BoxRenderable(renderer, {
    flexDirection: "column",
    width,
    height,
    backgroundColor: "#111114",
  })
  root.add(shell)

  const topBar = new TextRenderable(renderer, {
    content: "",
    bg: "#1c1c20",
    fg: "#b8b8bd",
    width,
    height: geometry.topBarHeight,
  })
  shell.add(topBar)

  const composer = new TextareaRenderable(renderer, {
    width,
    placeholder: "",
    backgroundColor: "#26262b",
    textColor: "#e8e8ee",
    focusedBackgroundColor: "#2b2b30",
  })

  const viewport = new ScrollBoxRenderable(renderer, {
    flexGrow: 1,
    flexBasis: 0,
    flexShrink: 1,
    scrollY: true,
    scrollX: false,
    stickyScroll: true,
    stickyStart: "bottom",
    width,
    rootOptions: { backgroundColor: "#0e0e12" },
  })
  shell.add(viewport)

  // Issue #78 — the swarm canvas pane, hidden by default. While open
  // it occupies the same vertical slot as the viewport (which
  // collapses to its tail line so the layout stays one-column).
  const swarmCanvas = new ScrollBoxRenderable(renderer, {
    flexGrow: 1,
    flexBasis: 0,
    flexShrink: 1,
    scrollY: true,
    scrollX: false,
    stickyScroll: true,
    stickyStart: "top",
    width,
    rootOptions: { backgroundColor: "#0e0e12" },
  })
  swarmCanvas.visible = false
  shell.add(swarmCanvas)

  // The queued-follow-up indicator — one row between the viewport and
  // the composer, visible only while the kernel queue is non-empty.
  const queueLine = new TextRenderable(renderer, {
    content: "",
    fg: "#d2d228",
    width,
    height: 1,
  })
  queueLine.visible = false
  shell.add(queueLine)

  composer.flexBasis = geometry.composerHeight
  composer.height = geometry.composerHeight
  composer.flexShrink = 0
  shell.add(composer)

  function composerPlaceholder(mode: LayoutMode): string {
    return tr(i18n, mode === "compact" ? "composer.placeholderCompact" : "composer.placeholder")
  }

  /**
   * Issue #76 — the pending approval for the active session, or
   * `null` when nothing awaits. The entry pipeline renders the
   * approval inline in the transcript; y/n and the palette drive
   * the same dispatch path through `decideCurrentApproval`.
   */
  function currentPendingApproval(): { readonly entry: ApprovalEntry; readonly sessionId: SessionId } | null {
    const session = activeSession(kernel.snapshot().state)
    if (session === null || session.identity.kind !== "remote") {
      return null
    }
    const last = lastAssistantMessage(session)
    const entry = pendingApprovalEntry({
      session,
      messageId: last?.id ?? "awaiting-approval",
      requester: last?.id ?? "",
      createdAtUnixMs: last?.createdAtUnixMs ?? 0,
      trailingMeta: last?.view?.meta ?? null,
    })
    if (entry === null) {
      return null
    }
    return { entry, sessionId: session.identity.id }
  }

  function lastAssistantMessage(session: HarnessSession): HarnessMessage | null {
    for (let index = session.transcript.length - 1; index >= 0; index -= 1) {
      const message = session.transcript[index]
      if (message !== undefined && message.role === "assistant") {
        return message
      }
    }
    return null
  }

  /**
   * Clear the approval layer (y/n keys) when nothing awaits. Called
   * by `render()` whenever the active session's awaiting-approval
   * state changes.
   */
  function ensureApprovalLayer(pending: { readonly entry: ApprovalEntry; readonly sessionId: SessionId } | null): void {
    if (pending === null) {
      if (approvalUnregister !== null) {
        approvalUnregister()
        approvalUnregister = null
      }
      return
    }
    if (approvalUnregister !== null) {
      // Already registered; the handlers close over the latest
      // `pending` via `decideCurrentApproval`, which looks the value
      // up on every press — no need to re-register.
      return
    }
    approvalUnregister = keymap.registerApprovalLayer({
      approve: () => decideCurrentApproval(true),
      reject: (payload) => decideCurrentApproval(false, payload.text),
    })
  }

  /**
   * Dispatch the approve/reject decision for the active session's
   * pending approval. Writes one row to the audit ledger first
   * (issue #76 — fire-and-forget into the kernel's writer queue),
   * then posts the kernel intent for the wire-level decision.
   */
  function decideCurrentApproval(approved: boolean, reason?: string): boolean {
    const pending = currentPendingApproval()
    if (pending === null) {
      return false
    }
    const receipt: DecisionReceipt = {
      decision: approved ? "approved" : "rejected",
      approvalId: pending.entry.approvalId,
      sessionId: pending.sessionId,
      scope: pending.entry.applicability.scope,
      requester: pending.entry.applicability.requester,
      fingerprint: approvalFingerprint(pending.entry),
      ...(reason !== undefined && reason.length > 0 ? { reason } : {}),
    }
    kernel.recordDecision(receipt)
    kernel.dispatch({
      kind: "decide-approval",
      sessionId: pending.sessionId,
      approved,
      ...(reason !== undefined && reason.length > 0 ? { reason } : {}),
      commandId: nextCommandId("approval"),
    })
    return true
  }

  /**
   * Issue #76 — palette handler for `show-receipt`. The full
   * receipt viewer is out of scope for this PR; we emit the
   * session's NDJSON path to stderr so the user knows where the
   * ledger lives.
   */
  function showReceipt(): boolean {
    const session = activeSession(kernel.snapshot().state)
    if (session === null || session.identity.kind !== "remote") {
      return false
    }
    const path = receiptsFilePath(defaultStateDirectory(), session.identity.id)
    console.error(`approvals ledger: ${path}`)
    return true
  }

  function refreshViewport(): void {
    while (viewport.getChildren().length > 0) {
      const child = viewport.getChildren()[0]
      if (child) {
        viewport.remove(child)
      }
    }
    const session = activeSession(lastState)
    const expanded =
      session !== null
        ? (expandedEntryIds.get(session.identity.id) ?? EMPTY_ENTRY_SET)
        : EMPTY_ENTRY_SET
    for (const line of buildTranscriptStyledLines(session, i18n, width, expanded)) {
      viewport.add(new TextRenderable(renderer, { content: styledContent(line) }))
    }
  }

  /**
   * Issue #78 — refresh the swarm canvas from the latest attention
   * signal. The canvas pane holds styled lines; we drop and refill
   * on each render so the surface stays a pure function of the
   * signal. When the signal is empty (focus-mode quiet case), the
   * pane is left empty — the host shows nothing in the right pane.
   */
  function refreshSwarmCanvas(): void {
    while (swarmCanvas.getChildren().length > 0) {
      const child = swarmCanvas.getChildren()[0]
      if (child) {
        swarmCanvas.remove(child)
      }
    }
    const sessions = new Map<string, HarnessSession>()
    for (const session of lastState.sessions) {
      sessions.set(session.identity.id, session)
    }
    const context: SwarmCanvasContext = {
      i18n,
      width,
      inspectedId: swarmInspectedId,
      sessions,
    }
    const lines = renderSwarmCanvas(swarmSignal, context)
    for (const line of lines) {
      swarmCanvas.add(new TextRenderable(renderer, { content: styledContent(line) }))
    }
  }

  /** Issue #78 — toggle the canvas pane and the viewport's collapse. */
  function toggleSwarmCanvas(): boolean {
    swarmSurfaceOpen = !swarmSurfaceOpen
    applySwarmSurfaceLayout()
    render()
    return true
  }

  /** Issue #78 — re-derive the attention signal from the kernel. */
  function refreshSwarmCanvasFromKernel(): boolean {
    swarmSignal = kernel.attention()
    render()
    return true
  }

  /**
   * Issue #78 — deep-link a row by id (worker or correlation). The
   * canvas pane must already be open; otherwise we open it first so
   * the inspect actually surfaces a row.
   */
  function inspectSwarmCanvas(id: string): boolean {
    const trimmed = id.trim()
    if (trimmed.length === 0) {
      return false
    }
    if (!swarmSurfaceOpen) {
      swarmSurfaceOpen = true
      applySwarmSurfaceLayout()
    }
    swarmInspectedId = trimmed
    render()
    return true
  }

  /**
   * Issue #78 — switch the layout between the full viewport and
   * the collapsed-viewport + canvas configuration. The viewport keeps
   * its scrollback; the canvas appears or disappears in the same
   * flex slot.
   */
  function applySwarmSurfaceLayout(): void {
    if (swarmSurfaceOpen) {
      // The viewport shrinks to a single summary line; the canvas
      // takes the rest. The viewport's flexGrow drops to 0 so the
      // canvas claims the space.
      viewport.flexGrow = 0
      viewport.height = 1
      swarmCanvas.visible = true
      swarmCanvas.flexGrow = 1
    } else {
      viewport.flexGrow = 1
      viewport.height = undefined as unknown as number
      swarmCanvas.visible = false
      swarmCanvas.flexGrow = 0
      swarmInspectedId = null
    }
  }

  // -- transcript progressive disclosure (issue #74) -------------------------

  /**
   * While a palette/slash/help surface is open its layer owns the
   * composer's extra keys — the disclosure toggles stay inert so
   * ctrl+o belongs to the menu, not the transcript (surface
   * ownership, the issue #75 pattern).
   */
  function disclosureAvailable(): boolean {
    return surface === "none"
  }

  /** ctrl+o — flip the LAST collapsible entry of the active session. */
  function toggleLastDetail(): boolean {
    if (!disclosureAvailable()) {
      return false
    }
    const session = activeSession(kernel.snapshot().state)
    if (session === null) {
      return false
    }
    const id = lastCollapsibleId(buildTranscriptEntries(session))
    if (id === null) {
      return false
    }
    const expanded = expandedEntryIds.get(session.identity.id) ?? new Set<string>()
    if (expanded.has(id)) {
      expanded.delete(id)
    } else {
      expanded.add(id)
    }
    expandedEntryIds.set(session.identity.id, expanded)
    render()
    return true
  }

  /** ctrl+shift+o — expand every entry, or collapse when all are open. */
  function toggleAllDetails(): boolean {
    if (!disclosureAvailable()) {
      return false
    }
    const session = activeSession(kernel.snapshot().state)
    if (session === null) {
      return false
    }
    const ids = collapsibleIds(buildTranscriptEntries(session))
    if (ids.length === 0) {
      return false
    }
    const expanded = expandedEntryIds.get(session.identity.id) ?? new Set<string>()
    const allExpanded = ids.every((id) => expanded.has(id))
    if (allExpanded) {
      expanded.clear()
    } else {
      for (const id of ids) {
        expanded.add(id)
      }
    }
    expandedEntryIds.set(session.identity.id, expanded)
    render()
    return true
  }

  function seedDraftOnce(): void {
    const session = activeSession(lastState)
    if (session === null) {
      return
    }
    const key = session.identity.id
    if (seededDrafts.has(key) || composer.plainText.length > 0) {
      return
    }
    const draft = lastState.drafts.find((entry) => entry.sessionId === key)
    if (draft && draft.text.length > 0) {
      setComposerText(draft.text)
    }
    seededDrafts.add(key)
  }

  function render(): void {
    if (closed) {
      return
    }
    seedDraftOnce()
    const session = activeSession(lastState)
    topBar.content = topBarContent(lastState, session, i18n, layoutMode === "compact")
    refreshViewport()
    refreshSwarmCanvas()

    const queuedLine = queuedFollowUpLine(session, i18n)
    if (queuedLine === null) {
      queueLine.visible = false
    } else {
      queueLine.content = queuedLine
      queueLine.visible = true
    }

    // Availability is state-derived: keep open surfaces' lists fresh.
    if (surface === "slash-menu") {
      refreshSlashMenu()
    } else if (surface === "palette") {
      refreshPaletteRows()
    }

    // Issue #76 — the approval is now an entry in the transcript;
    // register the y/n layer when something awaits, unregister
    // when it doesn't. The viewport keeps showing the entry even
    // while the composer has focus.
    ensureApprovalLayer(currentPendingApproval())
    renderer.requestRender()
  }

  function submitTurn(): boolean {
    const text = composer.plainText.trim()
    if (text.length === 0) {
      return false
    }
    // A registered `/command` dispatches through the named-command
    // registry instead of riding the wire; anything else is a message.
    const parsed = parseSlashInput(text, specs)
    if (parsed !== null) {
      // Unavailable in this state → inert: the draft stays so the
      // user sees the command did not run (e.g. /stop while idle).
      if (!commandAvailable(parsed.spec, commandContext())) {
        return false
      }
      setComposerText("")
      recall = LIVE_RECALL
      slashDismissed = false
      return keymap.dispatch(parsed.spec.name, { text: parsed.args })
    }
    let state = kernel.snapshot().state
    let activeKey = state.activeSessionId
    if (activeKey === null) {
      kernel.dispatch({
        kind: "open-session",
        projectId: options.newSessionProjectId ?? null,
      })
      state = kernel.snapshot().state
      activeKey = state.activeSessionId
      if (activeKey === null) {
        return false
      }
    }
    const session = state.sessions.find(
      (entry) => entry.identity.id === activeKey
    )
    const titleHint =
      session !== undefined &&
      session.identity.kind === "pending" &&
      session.title.length === 0
        ? titleFromMessage(text)
        : undefined
    kernel.dispatch({
      kind: "submit-turn",
      sessionId: activeKey,
      message: text,
      commandId: nextCommandId("tui"),
      echoText: text,
      ...(titleHint !== undefined ? { titleHint } : {}),
    })
    setComposerText("")
    recall = LIVE_RECALL
    slashDismissed = false
    return true
  }

  function cancelTurn(): boolean {
    const session = activeSession(kernel.snapshot().state)
    if (session === null || session.identity.kind !== "remote") {
      return false
    }
    if (session.turn.kind !== "thinking") {
      return false
    }
    kernel.dispatch({ kind: "cancel-turn", sessionId: session.identity.id })
    return true
  }

  function newSession(): boolean {
    kernel.dispatch({
      kind: "open-session",
      projectId: options.newSessionProjectId ?? null,
    })
    composer.focus()
    return true
  }

  function closeSession(): boolean {
    const session = activeSession(kernel.snapshot().state)
    if (session === null) {
      return false
    }
    kernel.dispatch({ kind: "close-session", sessionId: session.identity.id })
    return true
  }

  function renameSession(title: string): boolean {
    const session = activeSession(kernel.snapshot().state)
    if (session === null || title.length === 0) {
      return false
    }
    kernel.dispatch({
      kind: "rename-session",
      sessionId: session.identity.id,
      title,
    })
    return true
  }

  function clearDraft(): boolean {
    // The visible composer empties regardless; the kernel intent is
    // best-effort (nothing is persisted without a session).
    const session = activeSession(kernel.snapshot().state)
    setComposerText("")
    recall = LIVE_RECALL
    if (session !== null) {
      kernel.dispatch({ kind: "clear-draft", sessionId: session.identity.id })
    }
    return true
  }

  function openExternalEditor(): boolean {
    if (externalEditor === null) {
      return false
    }
    void suspendForEdit(() => externalEditor(composer.plainText))
    return true
  }

  // -- composer surfaces: slash menu, palette, help (issue #75) -----------

  function commandContext(): TuiCommandContext {
    const session = activeSession(kernel.snapshot().state)
    return {
      sessionOpen: session !== null,
      hasDraft: composer.plainText.trim().length > 0,
      turnKind: session?.turn.kind ?? "idle",
    }
  }

  function setComposerText(text: string): void {
    programmaticTextChange = true
    try {
      composer.setText(text)
      // setText resets the caret to the buffer start — recall and
      // completion both read best with the caret at the end.
      composer.gotoBufferEnd()
    } finally {
      programmaticTextChange = false
    }
    refreshSlashMenu()
    updateHistoryLayer()
  }

  function onComposerEdited(): void {
    recall = recallAfterEdit(recall, composer.plainText)
    // Any edit lifts a dismissal — typing after esc reopens the menu
    // for the new query (the Ink menuDismissed behavior).
    slashDismissed = false
    refreshSlashMenu()
    updateHistoryLayer()
  }

  function historyItems(): readonly string[] {
    return activeSession(kernel.snapshot().state)?.history ?? []
  }

  function stepHistory(direction: HistoryDirection): boolean {
    const items = historyItems()
    if (items.length === 0) {
      return false
    }
    const step = stepRecall(recall, direction, items)
    recall = step.state
    if (step.text !== null) {
      setComposerText(step.text)
    }
    updateHistoryLayer()
    return true
  }

  /**
   * The history layer owns ↑/↓ only while no surface is open and the
   * draft is empty or currently recalled; otherwise the arrows stay
   * with the composer (cursor movement) or the open surface.
   */
  function updateHistoryLayer(): void {
    const active =
      surface === "none" &&
      historyItems().length > 0 &&
      recallArrowsActive(recall, composer.plainText)
    if (active && unregisterHistoryLayer === null) {
      unregisterHistoryLayer = keymap.registerUiLayer(
        [
          { key: "up", run: () => stepHistory("older") },
          { key: "down", run: () => stepHistory("newer") },
        ],
        HISTORY_LAYER_PRIORITY
      )
    } else if (!active && unregisterHistoryLayer !== null) {
      unregisterHistoryLayer()
      unregisterHistoryLayer = null
    }
  }

  function refreshSlashMenu(): void {
    if (surface === "palette" || surface === "help") {
      return
    }
    const query = slashMenuQuery(composer.plainText)
    const matches =
      query === null || slashDismissed
        ? []
        : filterSlashCommands(specs, query, commandContext())
    const shouldOpen = matches.length > 0
    if (shouldOpen) {
      const reopened = surface !== "slash-menu"
      slashMatches = matches
      // A new query always restarts the selection at the top.
      slashIndex = 0
      slashWindowStart = 0
      if (reopened) {
        surface = "slash-menu"
        renderSurfaceOverlay()
        refreshSurfaceLayer()
      } else {
        renderSurfaceOverlay()
      }
      return
    }
    slashMatches = []
    if (surface === "slash-menu") {
      closeSurface()
    }
  }

  function currentPaletteMatches(): readonly TuiCommandSpec[] {
    return paletteMatches(
      specs,
      paletteQuery?.plainText ?? "",
      commandContext()
    )
  }

  function openPalette(): boolean {
    // While an approval awaits, the approval layer owns the keys —
    // the palette stays suppressed. Issue #76 moved the card from
    // an overlay into a transcript entry, so the gate checks the
    // pending approval directly.
    if (currentPendingApproval() !== null) {
      return false
    }
    if (surface === "palette") {
      return false
    }
    closeSurface()
    surface = "palette"
    paletteIndex = 0
    paletteWindowStart = 0
    renderSurfaceOverlay()
    refreshSurfaceLayer()
    return true
  }

  function openHelp(): boolean {
    if (surface === "help") {
      return false
    }
    closeSurface()
    surface = "help"
    renderSurfaceOverlay()
    refreshSurfaceLayer()
    return true
  }

  function closeSurface(): void {
    if (surfaceOverlay !== null) {
      shell.remove(surfaceOverlay)
      surfaceOverlay = null
    }
    paletteQuery = null
    paletteRows = null
    if (unregisterSurfaceLayer !== null) {
      unregisterSurfaceLayer()
      unregisterSurfaceLayer = null
    }
    const wasSurface = surface !== "none"
    surface = "none"
    if (wasSurface) {
      composer.focus()
      renderer.requestRender()
    }
  }

  /** Esc on a palette/help surface — the draft may warrant the /-menu. */
  function closeSurfaceAndRescan(): boolean {
    closeSurface()
    refreshSlashMenu()
    refreshSurfaceLayer()
    return true
  }

  function completeSlashSelection(): boolean {
    if (surface !== "slash-menu" || slashMatches.length === 0) {
      return false
    }
    const chosen = slashMatches[Math.min(slashIndex, slashMatches.length - 1)]
    if (chosen === undefined) {
      return false
    }
    // The trailing space closes the menu by itself (whitespace query).
    setComposerText(completeSlashCommand(chosen))
    return true
  }

  function runPaletteSelection(): boolean {
    if (surface !== "palette") {
      return false
    }
    const matches = currentPaletteMatches()
    const chosen = matches[Math.min(paletteIndex, matches.length - 1)]
    if (chosen === undefined) {
      return false
    }
    closeSurface()
    return keymap.dispatch(chosen.name)
  }

  function moveSelection(delta: number): boolean {
    if (surface === "slash-menu") {
      if (slashMatches.length === 0) {
        return false
      }
      slashIndex = (slashIndex + delta + slashMatches.length) % slashMatches.length
      slashWindowStart = windowStartFor(slashIndex, slashWindowStart)
      renderSurfaceOverlay()
      return true
    }
    if (surface === "palette") {
      const matches = currentPaletteMatches()
      if (matches.length === 0) {
        return false
      }
      paletteIndex = (paletteIndex + delta + matches.length) % matches.length
      paletteWindowStart = windowStartFor(paletteIndex, paletteWindowStart)
      refreshPaletteRows()
      return true
    }
    return false
  }

  /** Keep the visible window pinned to the selection (cap-sized). */
  function windowStartFor(index: number, currentStart: number): number {
    const maxStart = Math.max(0, index - MENU_MAX_ROWS + 1)
    if (index < currentStart) {
      return index
    }
    if (index > currentStart + MENU_MAX_ROWS - 1) {
      return maxStart
    }
    return currentStart
  }

  /** Refill the palette list in place — the query keeps focus + text. */
  function refreshPaletteRows(): void {
    if (paletteRows !== null) {
      fillPaletteRows(paletteRows)
    }
    renderer.requestRender()
  }

  function refreshSurfaceLayer(): void {
    if (unregisterSurfaceLayer !== null) {
      unregisterSurfaceLayer()
      unregisterSurfaceLayer = null
    }
    if (surface === "none") {
      updateHistoryLayer()
      return
    }
    const bindings: TuiUiBinding[] =
      surface === "slash-menu"
        ? [
            { key: "up", run: () => moveSelection(-1) },
            { key: "down", run: () => moveSelection(1) },
            { key: "tab", run: () => completeSlashSelection() },
            { key: "return", run: () => completeSlashSelection() },
            { key: "kpenter", run: () => completeSlashSelection() },
            { key: "escape", run: () => {
                slashDismissed = true
                closeSurface()
                return true
              } },
          ]
        : surface === "palette"
          ? [
              { key: "up", run: () => moveSelection(-1) },
              { key: "down", run: () => moveSelection(1) },
              { key: "return", run: () => runPaletteSelection() },
              { key: "kpenter", run: () => runPaletteSelection() },
              { key: "escape", run: () => closeSurfaceAndRescan() },
            ]
          : [
              { key: "escape", run: () => closeSurfaceAndRescan() },
              { key: "return", run: () => closeSurfaceAndRescan() },
            ]
    unregisterSurfaceLayer = keymap.registerUiLayer(bindings, TUI_UI_LAYER_PRIORITY)
    updateHistoryLayer()
  }

  function overlayHeight(needed: number): number {
    const reserved = geometry.topBarHeight + geometry.composerHeight + 2
    return Math.min(needed, Math.max(4, height - reserved))
  }

  function overlayWidth(): number {
    return Math.max(24, Math.min(width - 4, 80))
  }

  /**
   * Single-line row content: a wrapped row would blow the fixed
   * overlay height, so overlong rows clip with an ellipsis.
   */
  function clipRow(text: string): string {
    const usable = overlayWidth() - 5
    return text.length > usable ? `${text.slice(0, usable - 1)}…` : text
  }

  function insertOverlay(overlay: BoxRenderable): void {
    const composerIndex = shell.getChildren().indexOf(composer)
    shell.add(overlay, composerIndex >= 0 ? composerIndex : shell.getChildren().length)
  }

  function slashUsage(spec: TuiCommandSpec): string {
    const slash: TuiSlashSpec | null = spec.slash
    if (slash === null) {
      return spec.name
    }
    const hint =
      slash.argsHintKey !== null ? ` ${tr(i18n, slash.argsHintKey)}` : ""
    const aliases =
      slash.aliases.length > 0 ? ` [${slash.aliases.join(",")}]` : ""
    return `/${slash.name}${hint}${aliases}`
  }

  function renderSurfaceOverlay(): void {
    if (surfaceOverlay !== null) {
      shell.remove(surfaceOverlay)
      surfaceOverlay = null
    }
    paletteQuery = null
    paletteRows = null
    if (surface === "none") {
      return
    }
    if (surface === "slash-menu") {
      const rows = slashMatches.slice(
        slashWindowStart,
        slashWindowStart + MENU_MAX_ROWS
      )
      const usageWidth = Math.max(...rows.map((row) => slashUsage(row).length), 8)
      const overlay = new BoxRenderable(renderer, {
        flexDirection: "column",
        width: overlayWidth(),
        height: overlayHeight(rows.length + 3),
        flexShrink: 0,
        backgroundColor: "#1c1c20",
        borderStyle: "single",
        borderColor: "#8787f3",
      })
      overlay.add(
        new TextRenderable(renderer, {
          content: `  ${tr(i18n, "menu.commandsTitle")}  ${tr(i18n, "menu.commandsHint")}`,
          fg: "#d7d7ff",
          bg: "#1c1c20",
          width: overlayWidth(),
        })
      )
      rows.forEach((spec, index) => {
        const selected = index + slashWindowStart === slashIndex
        overlay.add(
          new TextRenderable(renderer, {
            content: clipRow(
              `${selected ? ">" : " "} ${slashUsage(spec).padEnd(usageWidth + 2)}${spec.description}`
            ),
            fg: selected ? "#d7d7ff" : "#b8b8bd",
            bg: "#1c1c20",
            width: overlayWidth(),
          })
        )
      })
      surfaceOverlay = overlay
      insertOverlay(overlay)
      return
    }

    if (surface === "palette") {
      const overlay = new BoxRenderable(renderer, {
        flexDirection: "column",
        width: overlayWidth(),
        height: overlayHeight(MENU_MAX_ROWS + 4),
        flexShrink: 0,
        backgroundColor: "#1c1c20",
        borderStyle: "single",
        borderColor: "#8787f3",
      })
      overlay.add(
        new TextRenderable(renderer, {
          content: `  ${tr(i18n, "palette.title")}`,
          fg: "#d7d7ff",
          bg: "#1c1c20",
          width: overlayWidth(),
        })
      )
      const query = new TextareaRenderable(renderer, {
        width: overlayWidth(),
        height: 1,
        placeholder: tr(i18n, "palette.placeholder"),
        backgroundColor: "#26262b",
        textColor: "#e8e8ee",
      })
      query.onContentChange = () => {
        paletteIndex = 0
        paletteWindowStart = 0
        refreshPaletteRows()
      }
      overlay.add(query)
      paletteQuery = query
      const rows = new BoxRenderable(renderer, {
        flexDirection: "column",
        width: overlayWidth(),
        flexShrink: 0,
      })
      overlay.add(rows)
      paletteRows = rows
      fillPaletteRows(rows)
      surfaceOverlay = overlay
      insertOverlay(overlay)
      query.focus()
      return
    }

    // help — every registered command with binding and description.
    const all = specs
    const usageWidth = Math.max(...all.map((spec) => slashUsage(spec).length), 8)
    const overlay = new BoxRenderable(renderer, {
      flexDirection: "column",
      width: overlayWidth(),
      height: overlayHeight(all.length + 3),
      flexShrink: 0,
      backgroundColor: "#1c1c20",
      borderStyle: "single",
      borderColor: "#8787f3",
    })
    overlay.add(
      new TextRenderable(renderer, {
        content: `  ${tr(i18n, "help.title")}`,
        fg: "#d7d7ff",
        bg: "#1c1c20",
        width: overlayWidth(),
      })
    )
    for (const spec of all) {
      const key = spec.key.length > 0 ? spec.key : "—"
      overlay.add(
        new TextRenderable(renderer, {
          content: clipRow(
            `  ${slashUsage(spec).padEnd(usageWidth + 2)}${key.padEnd(8)}${spec.description}`
          ),
          fg: "#e8e8ee",
          bg: "#1c1c20",
          width: overlayWidth(),
        })
      )
    }
    surfaceOverlay = overlay
    insertOverlay(overlay)
  }

  function fillPaletteRows(rows: BoxRenderable): void {
    while (rows.getChildren().length > 0) {
      const child = rows.getChildren()[0]
      if (child) {
        rows.remove(child)
      }
    }
    const matches = currentPaletteMatches().slice(
      paletteWindowStart,
      paletteWindowStart + MENU_MAX_ROWS
    )
    if (matches.length === 0) {
      rows.add(
        new TextRenderable(renderer, {
          content: `  ${tr(i18n, "palette.empty")}`,
          fg: "#8a8a8f",
          width: overlayWidth(),
        })
      )
      return
    }
    matches.forEach((spec, index) => {
      const selected = index + paletteWindowStart === paletteIndex
      const key = spec.key.length > 0 ? ` · ${spec.key}` : ""
      rows.add(
        new TextRenderable(renderer, {
          content: clipRow(`${selected ? ">" : " "} ${spec.label}${key} — ${spec.description}`),
          fg: selected ? "#d7d7ff" : "#b8b8bd",
          width: overlayWidth(),
        })
      )
    })
  }

  const keymap = createTuiKeymap(renderer, i18n, {
    "submit-turn": () => submitTurn(),
    "cancel-turn": () => cancelTurn(),
    "new-session": () => newSession(),
    "close-session": () => closeSession(),
    "rename-session": (payload: TuiCommandPayload) =>
      renameSession((payload.text ?? "").trim()),
    "clear-draft": () => clearDraft(),
    help: () => openHelp(),
    "open-editor": () => openExternalEditor(),
    "open-palette": () => openPalette(),
    "toggle-details-last": () => toggleLastDetail(),
    "toggle-details": () => toggleAllDetails(),
    // Issue #76 — palette-driven approve/reject reach the same
    // dispatch path as the y/n approval layer. `show-receipt` opens
    // the session's NDJSON ledger location.
    approve: () => decideCurrentApproval(true),
    reject: (payload: TuiCommandPayload) =>
      decideCurrentApproval(false, payload.text),
    "show-receipt": () => showReceipt(),
    // Issue #78 — swarm canvas surface.
    "toggle-swarm-canvas": () => toggleSwarmCanvas(),
    "swarm-canvas-refresh": () => refreshSwarmCanvasFromKernel(),
    "swarm-canvas-inspect": (payload: TuiCommandPayload) =>
      inspectSwarmCanvas(payload.text ?? ""),
    exit: () => {
      void close()
      return true
    },
  })

  // Typing into the composer drives the slash menu + history layer.
  // The content-change listener fires ASYNCHRONOUSLY (after the
  // programmatic flag drops), so recall-driven setText is recognized
  // by content instead: text equal to the recalled entry keeps the
  // recall position (editing AWAY from it breaks recall).
  composer.onContentChange = () => {
    if (programmaticTextChange) {
      return
    }
    const text = composer.plainText
    if (
      recall.index !== null &&
      text === (historyItems()[recall.index] ?? null)
    ) {
      return
    }
    onComposerEdited()
  }

  const unsubscribe = kernel.subscribe((snapshot) => {
    lastState = snapshot.state
    render()
  })

  async function setSize(nextWidth: number, nextHeight: number): Promise<void> {
    width = nextWidth
    height = nextHeight
    layoutMode = pickLayoutMode(width, height)
    geometry = computeGeometry(height, layoutMode)

    shell.width = width
    shell.height = height
    topBar.width = width
    topBar.height = geometry.topBarHeight
    viewport.width = width
    queueLine.width = width
    composer.width = width
    composer.flexBasis = geometry.composerHeight
    composer.height = geometry.composerHeight
    composer.placeholder = composerPlaceholder(layoutMode)

    // Issue #76 — the approval is a transcript entry; re-register
    // the y/n layer when something awaits. The viewport re-renders
    // below, picking up the entry at its new width.
    ensureApprovalLayer(currentPendingApproval())
    if (surface !== "none") {
      renderSurfaceOverlay()
    }
    refreshViewport()
    composer.focus()
    renderer.requestRender()
    await renderer.idle()
  }

  let closePromise: Promise<void> | null = null

  /**
   * The spike's lifecycle seam contract: `suspend()` first (a throw
   * skips editor/onRestore/resume), `onRestore` exactly once,
   * `resume()` exactly once on a successful suspend. A string result
   * adopts as the composer draft.
   */
  async function suspendForEdit<T>(
    editor: () => Promise<T> | T,
    suspendOptions?: { onRestore?: () => void }
  ): Promise<
    { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: Error }
  > {
    let suspendSucceeded = false
    try {
      lifecycle.suspend()
      suspendSucceeded = true
      try {
        const value = await editor()
        if (typeof value === "string") {
          setComposerText(value)
        }
        suspendOptions?.onRestore?.()
        return { ok: true as const, value }
      } catch (caught) {
        suspendOptions?.onRestore?.()
        return {
          ok: false as const,
          error: caught instanceof Error ? caught : new Error(String(caught)),
        }
      }
    } catch (caught) {
      return {
        ok: false as const,
        error: caught instanceof Error ? caught : new Error(String(caught)),
      }
    } finally {
      if (suspendSucceeded) {
        lifecycle.resume()
        composer.focus()
        renderer.requestRender()
      }
    }
  }

  async function close(): Promise<void> {
    // Memoized: a second call awaits the same shutdown sequence
    // instead of returning before the first finished.
    closePromise ??= runCloseSequence()
    await closePromise
  }

  async function runCloseSequence(): Promise<void> {
    if (closed) {
      return
    }
    closed = true
    unsubscribe()
    if (approvalUnregister !== null) {
      approvalUnregister()
      approvalUnregister = null
    }
    closeSurface()
    if (unregisterHistoryLayer !== null) {
      unregisterHistoryLayer()
      unregisterHistoryLayer = null
    }
    keymap.destroy()
    kernel.stop()
    kernelStopped = true
    await kernel.whenIdle()
    teardownRenderer()
    options.onExit?.()
  }

  /**
   * The single renderer-teardown path — both the graceful close
   * sequence and `destroy()` funnel here so a future teardown step
   * lands in exactly one place.
   */
  function teardownRenderer(): void {
    try {
      renderer.destroy()
    } catch {
      // idempotent
    }
  }

  lastState = kernel.snapshot().state
  composer.placeholder = composerPlaceholder(layoutMode)
  render()
  composer.focus()
  updateHistoryLayer()
  renderer.requestRender()

  return {
    renderer,
    keymap,
    async destroy() {
      if (closed) {
        return
      }
      closed = true
      unsubscribe()
      if (approvalUnregister !== null) {
        approvalUnregister()
        approvalUnregister = null
      }
      closeSurface()
      if (unregisterHistoryLayer !== null) {
        unregisterHistoryLayer()
        unregisterHistoryLayer = null
      }
      keymap.destroy()
      teardownRenderer()
    },
    async waitForIdle() {
      await renderer.idle()
    },
    async setSize(nextWidth, nextHeight) {
      await setSize(nextWidth, nextHeight)
    },
    setDraft(text: string) {
      setComposerText(text)
      renderer.requestRender()
    },
    getDraft(): string {
      return composer.plainText
    },
    getSurfaceKind(): TuiSurfaceKind {
      return surface
    },
    async close() {
      await close()
    },
    isClosed() {
      return closed
    },
    isKernelStopped() {
      return kernelStopped
    },
    async suspendForEdit<T>(
      editor: () => Promise<T> | T,
      suspendOptions?: { onRestore?: () => void }
    ): Promise<
      { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: Error }
    > {
      return suspendForEdit(editor, suspendOptions)
    },
  }
}

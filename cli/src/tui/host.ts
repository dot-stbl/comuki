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
  BoxRenderable,
  ScrollBoxRenderable,
  SelectRenderable,
  TextRenderable,
  TextareaRenderable,
  type CliRenderer,
} from "@opentui/core"
import type { ClientKernel } from "../kernel"
import { activeSession } from "../harness/selectors"
import type { ProjectId, SessionId } from "../harness/state"
import { createI18nFor, tr, type I18nInstance, type LocaleCode } from "../locales"
import {
  createTuiKeymap,
  type TuiKeymapSurface,
} from "./commands"
import {
  activeApprovalCard,
  buildTranscriptLines,
  topBarContent,
  type ApprovalCardModel,
} from "./view"

/**
 * Terminal lifecycle seam (adapted from the spike). Resolution order:
 * injected seam → no-op in memory mode → adapter over the renderer.
 */
export interface TerminalLifecycle {
  suspend(): void
  resume(): void
}

export interface TuiHostOptions {
  readonly renderer: CliRenderer
  readonly width: number
  readonly height: number
  readonly locale?: LocaleCode
  /** Pre-initialized i18next instance; omit to build a fresh one. */
  readonly i18n?: I18nInstance
  /**
   * When `true` the caller owns terminal side-effects — the lifecycle
   * seam resolves to a no-op (explicit, no constructor sniffing).
   */
  readonly memoryMode?: boolean
  /** Injected lifecycle seam; wins over memoryMode/renderer adapter. */
  readonly terminalLifecycle?: TerminalLifecycle
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

interface Geometry {
  readonly topBarHeight: number
  readonly composerHeight: number
}

type LayoutMode = "regular" | "compact"

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
  let approvalOverlay: BoxRenderable | null = null
  let commandSeq = 0
  let lastState = kernel.snapshot().state
  const seededDrafts = new Set<string>()

  function nextCommandId(prefix: string): string {
    commandSeq += 1
    return `${prefix}-${commandSeq}-${Date.now()}`
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

  composer.flexBasis = geometry.composerHeight
  composer.height = geometry.composerHeight
  composer.flexShrink = 0
  shell.add(composer)

  function composerPlaceholder(mode: LayoutMode): string {
    return tr(i18n, mode === "compact" ? "composer.placeholderCompact" : "composer.placeholder")
  }

  function approvalHeight(model: ApprovalCardModel): number {
    let rows = 1 // intent
    if (model.scope !== null) {
      rows += 1
    }
    if (model.risk !== null) {
      rows += 1
    }
    rows += 1 // plan header
    rows += model.steps.length > 0 ? model.steps.length : 1
    if (model.diff.length > 0) {
      rows += 1 + model.diff.length
    }
    rows += 1 // decide label
    rows += 4 // select
    return rows
  }

  function clearApproval(): void {
    if (approvalOverlay !== null) {
      shell.remove(approvalOverlay)
      approvalOverlay = null
    }
    if (approvalUnregister !== null) {
      approvalUnregister()
      approvalUnregister = null
    }
    viewport.visible = true
  }

  function renderApprovalCard(sessionId: SessionId, model: ApprovalCardModel): void {
    clearApproval()

    const overlayWidth = Math.max(20, Math.min(width - 4, 80))
    const nonApprovalRows = geometry.topBarHeight + geometry.composerHeight + 1
    const overlayHeight = Math.min(
      approvalHeight(model),
      Math.max(4, height - nonApprovalRows)
    )

    // The card replaces the viewport while the turn awaits approval —
    // `visible = false` collapses the viewport so the card fits.
    viewport.visible = false

    const overlay = new BoxRenderable(renderer, {
      flexDirection: "column",
      width: overlayWidth,
      height: overlayHeight,
      flexShrink: 0,
      backgroundColor: "#1c1c20",
      borderStyle: "single",
      borderColor: "#8787f3",
    })
    const composerIndex = shell.getChildren().indexOf(composer)
    shell.add(overlay, composerIndex >= 0 ? composerIndex : shell.getChildren().length)

    const row = (content: string, fg: string): void => {
      overlay.add(
        new TextRenderable(renderer, {
          content,
          fg,
          bg: "#1c1c20",
          width: overlayWidth,
        })
      )
    }

    row(`  ⏸ ${tr(i18n, "approval.intentPrefix")} ${model.intent ?? sessionId}`, "#d7d7ff")
    if (model.scope !== null) {
      row(`  ${tr(i18n, "approval.scopePrefix")} ${model.scope}`, "#b8b8bd")
    }
    if (model.risk !== null) {
      row(
        `  ${tr(i18n, "approval.riskPrefix")} ${model.risk}`,
        model.risk === "high" ? "#d2d228" : "#8a8a8f"
      )
    }
    const estimate =
      model.estimateMinutes !== null ? ` (~${model.estimateMinutes}m)` : ""
    row(`  ${tr(i18n, "approval.planPrefix")}${estimate}`, "#8a8a8f")
    if (model.steps.length > 0) {
      for (const step of model.steps) {
        row(`    ${tr(i18n, "approval.stepPrefix")} ${step}`, "#e8e8ee")
      }
    } else {
      row(`    ${tr(i18n, "approval.unreadablePlan")}`, "#8a8a8f")
    }
    if (model.diff.length > 0) {
      row(`  ${tr(i18n, "approval.diffPrefix")}`, "#8a8a8f")
      for (const line of model.diff) {
        row(`    ${line}`, "#d7d7ff")
      }
    }
    row(tr(i18n, "approval.decideLabel"), "#8a8a8f")
    overlay.add(
      new SelectRenderable(renderer, {
        options: [
          { name: tr(i18n, "approval.action.approve"), description: "" },
          { name: tr(i18n, "approval.action.reject"), description: "" },
        ],
        width: overlayWidth,
        height: 4,
        backgroundColor: "#26262b",
        focusedBackgroundColor: "#2b2b30",
        textColor: "#e8e8ee",
      })
    )
    approvalOverlay = overlay

    approvalUnregister = keymap.registerApprovalLayer({
      approve: () => decideApproval(true),
      reject: () => decideApproval(false),
    })
  }

  function decideApproval(approved: boolean): boolean {
    const card = activeApprovalCard(kernel.snapshot().state)
    if (card === null) {
      return false
    }
    kernel.dispatch({
      kind: "decide-approval",
      sessionId: card.sessionId,
      approved,
      commandId: nextCommandId("approval"),
    })
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
    for (const line of buildTranscriptLines(session, i18n, width)) {
      viewport.add(new TextRenderable(renderer, { content: line, fg: "#e8e8ee" }))
    }
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
      composer.setText(draft.text)
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

    const card = activeApprovalCard(lastState)
    if (card !== null) {
      renderApprovalCard(card.sessionId, card.card)
    } else {
      clearApproval()
    }
    renderer.requestRender()
  }

  function submitTurn(): boolean {
    const text = composer.plainText.trim()
    if (text.length === 0) {
      return false
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
    composer.setText("")
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

  const keymap = createTuiKeymap(renderer, i18n, {
    "submit-turn": () => submitTurn(),
    "cancel-turn": () => cancelTurn(),
    "new-session": () => newSession(),
    "close-session": () => closeSession(),
    exit: () => {
      void close()
      return true
    },
  })

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
    composer.width = width
    composer.flexBasis = geometry.composerHeight
    composer.height = geometry.composerHeight
    composer.placeholder = composerPlaceholder(layoutMode)

    // A card that exists at the old width re-lays-out to the new
    // geometry; clearing first restores viewport visibility for the
    // re-render decision below.
    const card = activeApprovalCard(lastState)
    if (card !== null) {
      renderApprovalCard(card.sessionId, card.card)
    } else {
      clearApproval()
    }
    refreshViewport()
    composer.focus()
    renderer.requestRender()
    await renderer.idle()
  }

  let closePromise: Promise<void> | null = null

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
    clearApproval()
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
  renderer.requestRender()

  return {
    renderer,
    keymap: {
      dispatch: keymap.dispatch,
      dispatchByKeymap: keymap.dispatchByKeymap,
      commandNames: keymap.commandNames,
      registerApprovalLayer: keymap.registerApprovalLayer,
      destroy: keymap.destroy,
    },
    async destroy() {
      if (closed) {
        return
      }
      closed = true
      unsubscribe()
      clearApproval()
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
      composer.setText(text)
      renderer.requestRender()
    },
    getDraft(): string {
      return composer.plainText
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
      let suspendSucceeded = false
      try {
        lifecycle.suspend()
        suspendSucceeded = true
        try {
          const value = await editor()
          if (typeof value === "string") {
            composer.setText(value)
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
    },
  }
}

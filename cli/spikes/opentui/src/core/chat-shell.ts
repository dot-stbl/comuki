/**
 * Chat shell — Core mode.
 *
 * Built directly on `@opentui/core` (no React layer). The renderables
 * the package ships — `BoxRenderable`, `ScrollBoxRenderable`,
 * `TextRenderable`, `TextareaRenderable`, `SelectRenderable` — are
 * assembled into a column-flex shell:
 *
 *   ┌──────────── 1 row ────────────┐ top bar (chrome)
 *   │                             │
 *   │       viewport (flexGrow=1) │ transcript + collapse
 *   │                             │
 *   ├──────────── composer ────────┤ composer (pinned; 3 rows
 *   │                             │  normally, 1 row in compact)
 *   └─ approval (overlay) ─────────┘ inline approval card
 *
 * All keystrokes flow through `@opentui/keymap` (the official
 * `@opentui/keymap/opentui` adapter) so palette and shortcuts share
 * one surface — the spike's command layer in `commands/registry.ts`.
 *
 * The spike is a *host*, not the production CLI. The acceptance
 * criterion for the spike is "domain/application packages have no
 * dependency on OpenTUI or React in the proposed dependency graph".
 * The reverse — that the TUI host has zero dependency on the
 * application domain — is also enforced here: the shell reads the
 * fixture directly, never `cli/src/lib/transcript.ts`.
 */

import {
  BoxRenderable,
  ScrollBoxRenderable,
  SelectRenderable,
  TextRenderable,
  TextareaRenderable,
  type CliRenderer,
} from "@opentui/core"
import {
  buildTranscript,
  flattenTranscript,
} from "../fixtures/transcript-1000.js"
import {
  createSpikeKeymap,
  type ApprovalPlan,
  type CommandPayload,
  type SpikeKeymap,
} from "../commands/registry.js"
import { createI18nFor, tr, type I18nInstance } from "../locales/index.js"

export interface ChatShellOptions {
  readonly width: number
  readonly height: number
  readonly focusMode: boolean
  /**
   * Optional pre-initialized i18next instance. When omitted,
   * `createChatShell` awaits a fresh `createI18nFor("en")` instance
   * inside the factory so the chat shell always reads from a
   * fully-initialized `I18nInstance`. Tests that exercise the `ru`
   * locale can pass an instance from `await createI18nFor("ru")`.
   */
  readonly i18n?: I18nInstance
}

/**
 * Terminal lifecycle seam. The chat shell's `suspendForEdit` calls
 * `suspend()` and `resume()` around an editor body. Production wires
 * the lifecycle to OpenTUI's `CliRenderer` (`renderer.suspend()` /
 * `renderer.resume()`); tests inject a spy to observe call order
 * without requiring a real TTY.
 *
 * `createChatShell` resolves the lifecycle to one of:
 *   1. `internals.terminalLifecycle` if supplied — the injected seam.
 *   2. A no-op object when `memoryMode === true`.
 *   3. An adapter that delegates to `renderer.suspend()` /
 *      `renderer.resume()` for a real terminal.
 *
 * `suspendForEdit` calls `suspend()` first; if it throws, the editor,
 * `onRestore`, and `resume` do not run and an error result is returned.
 * If `suspend()` succeeds, the editor body runs, `onRestore` fires
 * exactly once regardless of editor outcome, and the `finally` block
 * calls `resume()` exactly once when `suspend()` had succeeded.
 *
 * The seam is the lifecycle contract tested by `tests/suspend.test.ts`;
 * a real TTY is **not** exercised by these tests.
 */
export interface TerminalLifecycle {
  suspend(): void
  resume(): void
}

export interface ChatShell {
  readonly renderer: CliRenderer
  readonly keymap: SpikeKeymap
  destroy(): void
  /** Wait for the next visual idle; tests use it as a stable seam. */
  waitForIdle(): Promise<void>
  /** Resize the shell to fit a new viewport; layout re-renders. */
  setSize(width: number, height: number): Promise<void>
  setDraft(text: string): void
  getDraft(): string
  setApproval(plan: ApprovalPlan | null): void
  setFocusMode(focus: boolean): void
  focusMode(): boolean
  /**
   * Suspend the terminal, run `editor`, and restore. The resolved
   * `TerminalLifecycle` is called: `suspend()` first; if it throws
   * the editor, `onRestore`, and `resume` do not run and an error
   * result is returned. On editor success the result flows through
   * (and, if `editor` returned a string, the composer draft is set).
   * `onRestore` fires exactly once regardless of editor outcome.
   * The `finally` block calls `resume()`, `composer.focus()`, and
   * `renderer.requestRender()` once, but only when `suspend()` had
   * succeeded.
   */
  suspendForEdit<T>(
    editor: () => Promise<T> | T,
    options?: { onRestore?: () => void }
  ): Promise<
    { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: Error }
  >
}

export interface ChatShellInternals {
  readonly renderer: CliRenderer
  /**
   * When `true`, the caller is responsible for `setupTerminal()` and
   * terminal side-effects — the shell skips no terminal calls itself;
   * it just reads/writes through the renderer. Memory mode is
   * passed **explicitly** (no `constructor.name` sniffing).
   */
  readonly memoryMode: boolean
  /**
   * Optional seam for `suspendForEdit`. When supplied, the chat
   * shell uses this `TerminalLifecycle` in place of the no-op
   * (memoryMode) or renderer-adapter fallback. Tests inject a spy
   * here to observe call order without a real TTY.
   */
  readonly terminalLifecycle?: TerminalLifecycle
}

interface InternalHandles {
  readonly shell: BoxRenderable
  readonly topBar: TextRenderable
  readonly viewport: ScrollBoxRenderable
  readonly composer: TextareaRenderable
}

export async function createChatShell(
  options: ChatShellOptions,
  internals: ChatShellInternals
): Promise<ChatShell> {
  const renderer = internals.renderer
  // Terminal lifecycle resolution: injected seam wins, then no-op
  // for memoryMode, then adapter over the real renderer. The chosen
  // lifecycle is what `suspendForEdit` calls — see the contract on
  // `TerminalLifecycle` and `tests/suspend.test.ts` for the order
  // it is invoked.
  const lifecycle: TerminalLifecycle =
    internals.terminalLifecycle ??
    (internals.memoryMode
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
    options.i18n ?? (await createI18nFor("en"))
  const root = renderer.root

  const initialMode = pickLayoutMode(options.width, options.height)
  const initialGeometry = computeGeometry(options.width, options.height, initialMode)

  const shell = new BoxRenderable(renderer, {
    flexDirection: "column",
    width: options.width,
    height: options.height,
    backgroundColor: "#111114",
  })
  root.add(shell)

  const topBar = new TextRenderable(renderer, {
    content: topBarContent(i18n, initialMode),
    bg: "#1c1c20",
    fg: "#b8b8bd",
    width: options.width,
    height: initialGeometry.topBarHeight,
  })
  shell.add(topBar)

  // The composer carries a locale-sourced placeholder. The locale
  // resource (en default, ru parallel) is the single source of
  // truth for the prompt copy the user sees.
  const composer = new TextareaRenderable(renderer, {
    width: options.width,
    placeholder: composerPlaceholder(i18n, initialMode),
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
    width: options.width,
    rootOptions: { backgroundColor: "#0e0e12" },
  })
  shell.add(viewport)

  composer.flexBasis = initialGeometry.composerHeight
  composer.height = initialGeometry.composerHeight
  composer.flexShrink = 0
  shell.add(composer)

  const handles: InternalHandles = { shell, topBar, viewport, composer }

  // The viewport is hidden whenever an approval card is showing.
  // The shell column sums topBar (height 1) + composer (height 3)
  // + the overlay (height N) and gives the viewport the residual
  // flex space; setting viewport.visible = false collapses the
  // viewport to 0 rows so the overlay's full height fits.
  viewport.visible = true

  let focusMode = options.focusMode
  let draftBuffer = ""
  let approval: ApprovalPlan | null = null
  let approvalOverlay: BoxRenderable | null = null
  let layoutMode: LayoutMode = initialMode
  let geometry: Geometry = initialGeometry

  function refreshViewport() {
    while (viewport.getChildren().length > 0) {
      const child = viewport.getChildren()[0]
      if (child) viewport.remove(child)
    }
    const lines = flattenTranscript(buildTranscript(), {
      focusMode,
      width: options.width,
      expanded: false,
    })
    for (const line of lines) {
      viewport.add(
        new TextRenderable(renderer, { content: line, fg: "#e8e8ee" })
      )
    }
    renderer.requestRender()
  }

  function approvalHeight(plan: ApprovalPlan): number {
    const header = 5
    const steps = plan.planSteps.length
    const diffLines = Math.max(1, plan.diff.split("\n").length)
    const decisionLabel = 1
    const decision = 4
    return header + steps + diffLines + decisionLabel + decision
  }

  function renderApprovalCard(plan: ApprovalPlan) {
    if (approvalOverlay) {
      shell.remove(approvalOverlay)
      approvalOverlay = null
    }

    const overlayWidth = Math.max(20, Math.min(options.width - 4, 80))
    const desiredHeight = approvalHeight(plan)
    const nonApprovalRows = geometry.topBarHeight + geometry.composerHeight + 1
    const overlayHeight = Math.min(
      desiredHeight,
      Math.max(4, options.height - nonApprovalRows)
    )

    // The approval replaces the viewport while pending — the host
    // explicitly hides the viewport through `visible = false` so
    // yoga gives the card the rows it needs.
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
    // Insert the overlay immediately BEFORE the composer in the
    // shell's column. The viewport (visibility=hidden) takes 0 rows;
    // the overlay reserves its explicit height; the composer
    // keeps its 3 rows at the bottom.
    const composerIndex = shell.getChildren().indexOf(composer)
    shell.add(overlay, composerIndex >= 0 ? composerIndex : shell.getChildren().length)

    const INTENT_PREFIX = tr(i18n, "approval.intentPrefix")
    const SCOPE_PREFIX = tr(i18n, "approval.scopePrefix")
    const RISK_PREFIX = tr(i18n, "approval.riskPrefix")
    const PLAN_PREFIX = tr(i18n, "approval.planPrefix")
    const STEP_PREFIX = tr(i18n, "approval.stepPrefix")
    const DIFF_PREFIX = tr(i18n, "approval.diffPrefix")
    const DECIDE_LABEL = tr(i18n, "approval.decideLabel")

    overlay.add(
      new TextRenderable(renderer, {
        content: `  ⏸ ${INTENT_PREFIX} ${plan.intent}`,
        fg: "#d7d7ff",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    overlay.add(
      new TextRenderable(renderer, {
        content: `  ${SCOPE_PREFIX} ${plan.scope}`,
        fg: "#b8b8bd",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    overlay.add(
      new TextRenderable(renderer, {
        content: `  ${RISK_PREFIX} ${plan.risk}`,
        fg: plan.risk === "high" ? "#d2d228" : "#8a8a8f",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    overlay.add(
      new TextRenderable(renderer, {
        content: `  ${PLAN_PREFIX}`,
        fg: "#8a8a8f",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    for (const step of plan.planSteps) {
      overlay.add(
        new TextRenderable(renderer, {
          content: `    ${STEP_PREFIX} ${step}`,
          fg: "#e8e8ee",
          bg: "#1c1c20",
          width: overlayWidth,
        })
      )
    }
    overlay.add(
      new TextRenderable(renderer, {
        content: `  ${DIFF_PREFIX}`,
        fg: "#8a8a8f",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    for (const line of plan.diff.split("\n")) {
      overlay.add(
        new TextRenderable(renderer, {
          content: `    ${line}`,
          fg: "#d7d7ff",
          bg: "#1c1c20",
          width: overlayWidth,
        })
      )
    }
    // The approve and reject actions are real renderables —
    // a SelectRenderable with two options. The action labels are
    // locale-sourced so the same UI surface serves en + ru.
    overlay.add(
      new TextRenderable(renderer, {
        content: DECIDE_LABEL,
        fg: "#8a8a8f",
        bg: "#1c1c20",
        width: overlayWidth,
      })
    )
    overlay.add(
      new SelectRenderable(renderer, {
        options: [
          {
            name: tr(i18n, "approval.action.approve"),
            description: "APPROVAL-ACTION-APPROVE",
          },
          {
            name: tr(i18n, "approval.action.reject"),
            description: "APPROVAL-ACTION-REJECT",
          },
        ],
        width: overlayWidth,
        height: 4,
        backgroundColor: "#26262b",
        focusedBackgroundColor: "#2b2b30",
        textColor: "#e8e8ee",
      })
    )
    approvalOverlay = overlay
  }

  function clearApproval() {
    if (approvalOverlay) {
      shell.remove(approvalOverlay)
      approvalOverlay = null
    }
    viewport.visible = true
  }

  async function setSize(width: number, height: number) {
    // Detect a mode shift before the resize so chrome wording,
    // composer height and approval compactness all update together.
    layoutMode = pickLayoutMode(width, height)
    geometry = computeGeometry(width, height, layoutMode)

    handles.shell.width = width
    handles.shell.height = height
    handles.topBar.width = width
    handles.topBar.content = topBarContent(i18n, layoutMode)
    handles.topBar.height = geometry.topBarHeight
    handles.viewport.width = width
    handles.composer.width = width
    handles.composer.flexBasis = geometry.composerHeight
    handles.composer.height = geometry.composerHeight
    handles.composer.placeholder = composerPlaceholder(i18n, layoutMode)

    // An approval that exists at the old width must be re-laid-out
    // to fit the new geometry — its height is bounded by the shell's
    // available rows. Re-rendering also restores viewport visibility
    // for the duration of the resize before deciding the new state.
    if (approval) {
      viewport.visible = true
      renderApprovalCard(approval)
    } else {
      viewport.visible = true
    }
    refreshViewport()
    composer.focus()
    renderer.requestRender()
    await renderer.idle()
  }

  const keymap = createSpikeKeymap(renderer, i18n, {
    "open-palette": () => {
      if (approval) return
      const plan: ApprovalPlan = {
        intent: "re-run integration suite",
        scope: "tests/Unit.Identity.Oidc.*",
        risk: "medium",
        planSteps: [
          "1. snapshot queue depth",
          "2. dispatch 4 claimers",
          "3. abort on stall > 60s",
        ],
        diff: "+ tests/Unit.Identity.Oidc* --ff\n- tests/Unit.Kafka* --ff",
      }
      approval = plan
      renderApprovalCard(plan)
      renderer.requestRender()
    },
    "close-palette": () => {
      approval = null
      clearApproval()
      renderer.requestRender()
    },
    "save-snippet": () => {
      draftBuffer = ""
      composer.setText("")
      renderer.requestRender()
    },
    "approve-plan": (payload: CommandPayload) => {
      approval = null
      clearApproval()
      draftBuffer = payload.text ?? draftBuffer
      renderer.requestRender()
    },
    "reject-plan": (payload: CommandPayload) => {
      approval = null
      clearApproval()
      draftBuffer = payload.reason ?? draftBuffer
      renderer.requestRender()
    },
    "queue-followup": (payload: CommandPayload) => {
      draftBuffer = payload.text ?? draftBuffer
      renderer.requestRender()
    },
    "submit-turn": () => {
      draftBuffer = ""
      composer.setText("")
      renderer.requestRender()
    },
    "copy-last-answer": () => {
      // OSC 52 stub — the spike doesn't ship a clipboard bridge
    },
    "open-status": () => {
      // Status overlay is wired through the same `keymap` surface
    },
  })

  refreshViewport()
  composer.focus()
  renderer.requestRender()

  return {
    renderer,
    keymap,
    async destroy() {
      keymap.destroy()
      clearApproval()
      try {
        renderer.destroy()
      } catch {
        // idempotent
      }
    },
    async waitForIdle() {
      await renderer.idle()
    },
    async setSize(width, height) {
      await setSize(width, height)
    },
    setDraft(text: string) {
      draftBuffer = text
      composer.setText(text)
      renderer.requestRender()
    },
    getDraft() {
      return composer.plainText || draftBuffer
    },
    setApproval(plan) {
      approval = plan
      if (plan) {
        renderApprovalCard(plan)
      } else {
        clearApproval()
      }
      renderer.requestRender()
    },
    setFocusMode(focus: boolean) {
      focusMode = focus
      refreshViewport()
    },
    focusMode() {
      return focusMode
    },
    async suspendForEdit<T>(
      editor: () => Promise<T> | T,
      options?: { onRestore?: () => void }
    ): Promise<
      { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: Error }
    > {
      // Lifecycle contract (also asserted by `tests/suspend.test.ts`):
      //   1. lifecycle.suspend() runs first. If it throws, the editor,
      //      onRestore, and lifecycle.resume() do not run and an error
      //      result is returned.
      //   2. If suspend() succeeded, the editor body runs.
      //   3. onRestore() fires exactly once regardless of editor outcome.
      //   4. The finally block calls lifecycle.resume(),
      //      composer.focus(), and renderer.requestRender() once, but
      //      only when suspend() had succeeded.
      let suspendSucceeded = false
      try {
        lifecycle.suspend()
        suspendSucceeded = true
        try {
          const value = await editor()
          if (typeof value === "string") {
            composer.setText(value)
          }
          options?.onRestore?.()
          return { ok: true as const, value }
        } catch (caught) {
          options?.onRestore?.()
          return {
            ok: false as const,
            error:
              caught instanceof Error ? caught : new Error(String(caught)),
          }
        }
      } catch (caught) {
        return {
          ok: false as const,
          error:
            caught instanceof Error ? caught : new Error(String(caught)),
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

/* -------------------------------------------------------------------------- */
/* Layout helpers                                                              */
/* -------------------------------------------------------------------------- */

type LayoutMode = "regular" | "compact"

interface Geometry {
  topBarHeight: number
  composerHeight: number
}

function pickLayoutMode(width: number, height: number): LayoutMode {
  if (width < 60 || height < 18) return "compact"
  return "regular"
}

function computeGeometry(
  _width: number,
  height: number,
  mode: LayoutMode
): Geometry {
  // Both modes reserve 1 row for the top bar. The composer
  // collapses to 1 row in compact mode so a 16-row viewport still
  // has room for at least 13 rows of transcript.
  void _width
  if (mode === "compact") {
    return {
      topBarHeight: 1,
      composerHeight: height >= 6 ? 2 : 1,
    }
  }
  return {
    topBarHeight: 1,
    composerHeight: 3,
  }
}

function topBarContent(i18n: I18nInstance, mode: LayoutMode): string {
  if (mode === "compact") return tr(i18n, "chrome.titleCompact")
  return tr(i18n, "chrome.title")
}

function composerPlaceholder(i18n: I18nInstance, mode: LayoutMode): string {
  if (mode === "compact") return tr(i18n, "composer.placeholderCompact")
  return tr(i18n, "composer.placeholder")
}

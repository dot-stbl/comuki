/** Pure responsive geometry for the full-screen agent harness. */

export type HarnessMode = "compact" | "standard" | "wide"

export interface HarnessLayout {
  readonly mode: HarnessMode
  readonly topBarRows: number
  readonly dividerWidth: number
  readonly workspaceWidth: number
  readonly workbenchWidth: number
}

/**
 * Conversation owns the terminal. Width changes text wrapping until a
 * wide terminal also has active operational context to show; sessions alone
 * never reserve space.
 */
export function resolveHarnessLayout(
  columns: number,
  hasContextualWorkbench: boolean
): HarnessLayout {
  const safeColumns = Math.max(1, columns)
  const mode: HarnessMode =
    safeColumns < 60 ? "compact" : safeColumns < 110 ? "standard" : "wide"
  const workbenchWidth = mode === "wide" && hasContextualWorkbench ? 34 : 0
  const dividerWidth = workbenchWidth > 0 ? 1 : 0
  return {
    mode,
    topBarRows: 1,
    dividerWidth,
    workspaceWidth: Math.max(1, safeColumns - workbenchWidth - dividerWidth),
    workbenchWidth,
  }
}

export interface WorkbenchContext {
  readonly status?: string
  readonly awaitingApproval?: boolean
  readonly pendingPlan?: unknown
  readonly runsFeed?: unknown
}

/** Operational context is visible only while the active session owns it. */
export function hasContextualWorkbench(
  session: WorkbenchContext | null | undefined
): boolean {
  return Boolean(
    session &&
      (session.awaitingApproval === true ||
        session.pendingPlan != null ||
        session.runsFeed != null ||
        session.status === "running")
  )
}

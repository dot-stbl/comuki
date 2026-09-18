/** Pure responsive geometry for the full-screen agent harness. */

export type HarnessMode = "compact" | "standard" | "wide"

export interface HarnessLayout {
  readonly mode: HarnessMode
  readonly topBarRows: number
  readonly navigationRows: number
  readonly railWidth: number
  readonly dividerWidth: number
  readonly workspaceWidth: number
}

/**
 * The terminal has three intentional compositions, not one layout that
 * progressively crushes itself. Wide terminals gain a session rail;
 * standard terminals use a session strip; compact terminals retain the
 * strip but simplify the chrome around it.
 */
export function resolveHarnessLayout(
  columns: number,
  hasSessions: boolean
): HarnessLayout {
  const safeColumns = Math.max(1, columns)
  const mode: HarnessMode =
    safeColumns < 60 ? "compact" : safeColumns < 110 ? "standard" : "wide"
  const railWidth = mode === "wide" && hasSessions ? 24 : 0
  const dividerWidth = railWidth > 0 ? 1 : 0
  return {
    mode,
    topBarRows: 2,
    navigationRows: hasSessions && mode !== "wide" ? 1 : 0,
    railWidth,
    dividerWidth,
    workspaceWidth: Math.max(1, safeColumns - railWidth - dividerWidth),
  }
}

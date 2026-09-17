/**
 * Live terminal dimensions: { columns, rows } from `useStdout()`, updated
 * on every stdout `resize` event (SIGWINCH on POSIX, the equivalent on
 * Windows console resize).
 *
 * Ink's renderer already redraws on resize, but child components need
 * the new dimensions to re-derive layout (e.g. a `<Box width={cols}>`
 * that wants to fill the screen). A naive `stdout.columns` read in the
 * parent re-render is enough for the first paint; the subscription here
 * keeps children consistent after a resize without polling.
 *
 * Returns a defensive fallback (80×24) when the hook is invoked outside
 * of an Ink render tree (tests, SSR) so callers don't have to null-check.
 */
import { useStdout } from "ink"
import { useEffect, useState } from "react"

export interface TerminalDimensions {
  readonly columns: number
  readonly rows: number
}

const FALLBACK: TerminalDimensions = { columns: 80, rows: 24 }
export const FALLBACK_DIMENSIONS = FALLBACK

/**
 * The smallest shape we need: dimensions + EventEmitter-style on/off
 * for the `resize` event. Both `process.stdout` and a fake
 * EventEmitter satisfy it.
 */
export interface DimensionsStream {
  readonly columns: number | undefined
  readonly rows: number | undefined
  on(event: "resize", listener: () => void): unknown
  off(event: "resize", listener: () => void): unknown
}

/**
 * Pure subscribe/unsubscribe against any stdout-like stream (the real
 * `process.stdout` in production, a fake EventEmitter in tests). Emits
 * the current snapshot on subscribe so callers always have a value.
 */
export function subscribeDimensions(
  stdout: DimensionsStream | null,
  emit: (dims: TerminalDimensions) => void
): () => void {
  const snapshot = (): TerminalDimensions => ({
    columns: stdout?.columns ?? FALLBACK.columns,
    rows: stdout?.rows ?? FALLBACK.rows,
  })
  emit(snapshot())
  if (!stdout) {
    return () => {}
  }
  const onResize = () => emit(snapshot())
  stdout.on("resize", onResize)
  return () => {
    stdout.off("resize", onResize)
  }
}

export function useStdoutDimensions(): TerminalDimensions {
  const { stdout } = useStdout()
  const [dims, setDims] = useState<TerminalDimensions>(() => ({
    columns: stdout?.columns ?? FALLBACK.columns,
    rows: stdout?.rows ?? FALLBACK.rows,
  }))

  useEffect(() => subscribeDimensions(stdout, setDims), [stdout])

  return dims
}

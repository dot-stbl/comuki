/**
 * Scroll state for the transcript viewport — the irssi/htop follow
 * model on top of the pure math in `lib/viewport.ts`.
 *
 * `offset 0` = follow bottom: the newest line stays visible and new
 * output auto-scrolls. Any scroll-up suspends follow; while suspended,
 * appended output shifts the offset with it (the visible content stays
 * anchored — no auto-jump) and lights the `newBelow` indicator until
 * the user comes back to the bottom (PgDn / End / ↓).
 *
 * The append/anchor adjustment runs during render (the React
 * "adjusting state when props change" pattern), not in an effect — an
 * effect would paint one intermediate frame with the stale offset on
 * every streamed chunk, visible as flicker.
 */
import { useCallback, useRef, useState } from "react"
import { maxOffset } from "../lib/viewport"

export interface TranscriptScroll {
  /** Lines hidden below the viewport; 0 = following the bottom. */
  readonly offset: number
  /** New output arrived while scrolled up — show the dim indicator. */
  readonly newBelow: boolean
  /** True while the view is scrolled back (arrows scroll, not recall). */
  readonly scrolledUp: boolean
  readonly pageUp: () => void
  readonly pageDown: () => void
  readonly lineUp: () => void
  readonly lineDown: () => void
  /** Home — jump to the top of the transcript. */
  readonly toTop: () => void
  /** End — jump to the bottom and resume following. */
  readonly toBottom: () => void
}

interface ScrollState {
  readonly offset: number
  readonly newBelow: boolean
}

const BOTTOM: ScrollState = { offset: 0, newBelow: false }

export function useTranscriptScroll(
  lineCount: number,
  height: number,
  resetKey: unknown
): TranscriptScroll {
  const [state, setState] = useState<ScrollState>(BOTTOM)
  const meta = useRef({ lineCount, resetKey })

  if (meta.current.resetKey !== resetKey) {
    // Tab switch — a different transcript; follow its bottom.
    meta.current = { lineCount, resetKey }
    if (state.offset !== 0 || state.newBelow) {
      setState(BOTTOM)
    }
  } else if (meta.current.lineCount !== lineCount) {
    const delta = lineCount - meta.current.lineCount
    meta.current = { lineCount, resetKey }
    if (delta > 0 && state.offset > 0) {
      // Anchored scroll-back: keep the same content in view.
      setState({ offset: state.offset + delta, newBelow: true })
    } else if (delta < 0 && state.offset > 0) {
      // /clear or a hydration replace — the anchor is gone.
      setState(BOTTOM)
    }
  }

  const offset = Math.min(state.offset, maxOffset(lineCount, height))
  const scrolledUp = offset > 0

  const move = useCallback(
    (delta: number) => {
      setState((current) => {
        const next = Math.min(
          Math.max(0, current.offset + delta),
          maxOffset(lineCount, height)
        )
        return {
          offset: next,
          // Reaching the bottom resumes follow — the indicator clears.
          newBelow: next === 0 ? false : current.newBelow,
        }
      })
    },
    [lineCount, height]
  )

  const pageUp = useCallback(() => move(Math.max(1, height)), [move, height])
  const pageDown = useCallback(
    () => move(-Math.max(1, height)),
    [move, height]
  )
  const lineUp = useCallback(() => move(1), [move])
  const lineDown = useCallback(() => move(-1), [move])
  const toTop = useCallback(() => {
    setState((current) => ({
      offset: maxOffset(lineCount, height),
      newBelow: current.newBelow,
    }))
  }, [lineCount, height])
  const toBottom = useCallback(() => setState(BOTTOM), [])

  return {
    offset,
    newBelow: scrolledUp && state.newBelow,
    scrolledUp,
    pageUp,
    pageDown,
    lineUp,
    lineDown,
    toTop,
    toBottom,
  }
}

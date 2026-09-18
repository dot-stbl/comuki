/**
 * SGR mouse tracking hook.
 *
 * Ink 5's `useInput` never surfaces mouse reports, so this hook:
 *   1. writes DECSET 1000/1006/1002 on mount (and the matching `l` on
 *      unmount) so the terminal starts emitting CSI `<b;x;yM` reports;
 *   2. listens on the same internal `input` emitter `useHomeEndKeys`
 *      uses and forwards parsed left-button *presses* to `onClick`.
 *
 * Terminals that don't speak mouse tracking ignore the DECSET bytes
 * and never emit reports — the keyboard path is untouched. Releases
 * and motion (button ≥ 32, or `m` terminator) are parsed then dropped
 * so a click is one event, not a press+release pair.
 */
import { useEffect, useRef } from "react"
import { useStdin } from "ink"
import {
  DISABLE_MOUSE,
  ENABLE_MOUSE,
  parseSgrMouse,
  type SgrMouseEvent,
} from "../lib/mouse"
import { writeTerminal } from "../lib/term"

export type MouseClick = Pick<SgrMouseEvent, "x" | "y" | "button">

export type MouseWriter = (sequence: string) => void

export function useMouse(
  onClick: (click: MouseClick) => void,
  active: boolean = true,
  writer: MouseWriter = writeTerminal
): void {
  const { internal_eventEmitter } = useStdin()
  const handler = useRef(onClick)
  useEffect(() => {
    handler.current = onClick
  }, [onClick])

  useEffect(() => {
    if (!active) {
      return
    }
    writer(ENABLE_MOUSE)
    return () => {
      writer(DISABLE_MOUSE)
    }
  }, [active, writer])

  useEffect(() => {
    if (!active || !internal_eventEmitter) {
      return
    }
    const onInput = (chunk: unknown) => {
      const data = typeof chunk === "string" ? chunk : String(chunk)
      const event = parseSgrMouse(data)
      if (event === null || !event.press || event.button !== 0) {
        return
      }
      handler.current({ x: event.x, y: event.y, button: event.button })
    }
    internal_eventEmitter.on("input", onInput)
    return () => {
      internal_eventEmitter.off("input", onInput)
    }
  }, [active, internal_eventEmitter])
}

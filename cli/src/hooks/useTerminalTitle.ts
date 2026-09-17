/**
 * Keeps the terminal window title in sync with `title` (OSC 2) and
 * resets it to the bare app name on unmount — save-none: the previous
 * title is never queried, so there is nothing real to restore. The
 * writer is injectable so tests capture the emitted bytes instead of
 * racing a real stdout.
 */
import { useEffect } from "react"
import { TITLE_APP, titleSequence, writeTerminal } from "../lib/term"

export type TitleWriter = (sequence: string) => void

export function useTerminalTitle(
  title: string,
  writer: TitleWriter = writeTerminal
): void {
  useEffect(() => {
    writer(titleSequence(title))
  }, [title, writer])

  useEffect(() => {
    return () => {
      writer(titleSequence(TITLE_APP))
    }
  }, [writer])
}

/**
 * One transcript message. Formatting lives in `lib/format.ts` (pure,
 * tested); this component only feeds the wire view through it and
 * renders the finished lines. Assistant prose is markdown, wrapped to
 * the live terminal width (`useStdoutDimensions`), overridable via the
 * `width` prop for tests. `expanded` carries the session's ctrl+o
 * toggle: collapsed (default) thinking/tool parts render as one dim
 * summary line each.
 */
import { Text } from "ink"
import React from "react"
import type { ChatMessageView } from "../lib/client"
import { blankRow, renderMessage } from "../lib/format"
import { useStdoutDimensions } from "../hooks/useStdoutDimensions"

export interface ChatMessageProps {
  readonly message: ChatMessageView
  /** Wrap width override; defaults to the live stdout columns. */
  readonly width?: number
  /** ctrl+o — expand thinking/tool parts instead of summary lines. */
  readonly expanded?: boolean
}

export function ChatMessage({
  message,
  width,
  expanded = false,
}: ChatMessageProps) {
  const { columns } = useStdoutDimensions()
  const lines = renderMessage(message, width ?? columns, { expanded })
  return (
    <>
      {lines.map((line, index) => (
        <Text key={`${message.id}-${index}`}>{blankRow(line)}</Text>
      ))}
    </>
  )
}

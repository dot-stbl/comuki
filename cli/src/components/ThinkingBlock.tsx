/**
 * One collapsible transcript block — a completed `thinking` or `tool`
 * part of an assistant message. Collapsed (the transcript default) it
 * is a single dim summary line: `◌ thinking · 3.4s`,
 * `⚙ memory.recall(args) → ok`. Expanded it renders the full body —
 * thinking markdown dimmed, tool status line plus pretty-printed args
 * and result.
 *
 * Line generation stays pure in `lib/format.ts` (`collapsedSummary` +
 * `renderPart`); this component only mounts the finished lines. A
 * block still streaming never passes through here — the live chunk
 * tail renders via `LiveMessage` and collapses only once the turn
 * completes and the finalized parts arrive.
 */
import { Text } from "ink"
import React from "react"
import type { MessagePart } from "../lib/client"
import { DEFAULT_MARKDOWN_WIDTH } from "../lib/markdown"
import { renderPart } from "../lib/format"
export type CollapsiblePart = Extract<
  MessagePart,
  { kind: "thinking" } | { kind: "tool" }
>

export function isCollapsiblePart(part: MessagePart): part is CollapsiblePart {
  return part.kind === "thinking" || part.kind === "tool"
}

export interface ThinkingBlockProps {
  readonly part: CollapsiblePart
  /** false → the single summary line; true → the full block body. */
  readonly expanded: boolean
  /** Wrap width for the expanded thinking markdown; defaults to 80. */
  readonly width?: number
}

export function ThinkingBlock({ part, expanded, width }: ThinkingBlockProps) {
  const lines = renderPart(part, width ?? DEFAULT_MARKDOWN_WIDTH, { expanded })
  return (
    <>
      {lines.map((line, index) => (
        <Text key={index}>{line}</Text>
      ))}
    </>
  )
}

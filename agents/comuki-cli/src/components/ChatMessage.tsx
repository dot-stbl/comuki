/**
 * One transcript message. Formatting lives in `lib/format.ts` (pure,
 * tested); this component only feeds the wire view through it and renders
 * the finished lines.
 */
import { Text } from "ink"
import React from "react"
import type { ChatMessageView } from "../lib/client"
import { renderMessage } from "../lib/format"

export function ChatMessage({ message }: { message: ChatMessageView }) {
  const lines = renderMessage(message)
  return (
    <>
      {lines.map((line, index) => (
        <Text key={`${message.id}-${index}`}>{line}</Text>
      ))}
    </>
  )
}

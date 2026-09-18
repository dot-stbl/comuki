/**
 * The framed alert the REPL pins above the prompt (connect-time
 * 401/403, unreachable host). Line generation lives in `lib/alerts.ts`
 * so the transcript can reuse the same bytes as a `lines` block.
 */
import { Text } from "ink"
import React from "react"
import {
  renderAlertCard,
  type AlertCardModel,
} from "../lib/alerts"
import { useStdoutDimensions } from "../hooks/useStdoutDimensions"

export type { AlertKind, AlertCardModel } from "../lib/alerts"

export interface AlertCardProps extends AlertCardModel {
  /** Wrap width override; defaults to the live stdout columns. */
  readonly width?: number
}

export function AlertCard({
  kind,
  code,
  title,
  detail,
  hints,
  width,
}: AlertCardProps) {
  const { columns } = useStdoutDimensions()
  const lines = renderAlertCard(
    { kind, code, title, detail, hints },
    width ?? columns
  )
  return (
    <>
      {lines.map((line, index) => (
        <Text key={index}>{line}</Text>
      ))}
    </>
  )
}

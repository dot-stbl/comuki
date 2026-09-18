import React from "react"
import { palette } from "../theme"
import { SurfaceLine, type SurfaceTextSegment } from "./SurfaceLine"

export interface HarnessFooterProps {
  readonly width: number
  readonly mode: string
  readonly model?: string
  readonly project?: string
  readonly workers: number
  readonly context?: string
  readonly queue: number
  readonly busy?: string
  readonly signedOut?: boolean
  readonly approval?: boolean
}

export function footerSegments({
  width,
  mode,
  model,
  project,
  workers,
  context,
  queue,
  busy,
  signedOut = false,
  approval = false,
}: HarnessFooterProps): readonly SurfaceTextSegment[] {
  const compact = width < 72
  const values = compact
    ? [
        mode,
        model,
        project,
        workers > 0 ? `${workers}w` : undefined,
        queue > 0 ? `q${queue}` : undefined,
        busy,
      ]
    : [
        `mode ${mode}`,
        model ? `model ${model}` : undefined,
        project ? `project ${project}` : undefined,
        `workers ${workers}`,
        context ? `context ${context}` : undefined,
        `queue ${queue}`,
        busy,
      ]
  const text = values.filter((value): value is string => Boolean(value)).join(" / ")
  const hint = approval
    ? " / approval: /approve or /reject"
    : signedOut
      ? " / sign in with /login"
      : queue > 0
        ? " / queued"
        : busy
          ? " / /stop interrupts"
          : ""
  return [
    { text: `  ${text}`, dim: true },
    ...(hint ? [{ text: hint, color: approval ? palette.waiting : palette.text }] : []),
  ]
}

export function HarnessFooter(props: HarnessFooterProps) {
  return (
    <SurfaceLine
      width={props.width}
      background={palette.rail}
      segments={footerSegments(props)}
    />
  )
}

import { Box } from "ink"
import React from "react"
import type { ActivityItem, ActivityStatus } from "../lib/activity"
import { palette, stripAnsi } from "../theme"
import { SurfaceLine, type SurfaceTextSegment } from "./SurfaceLine"

export interface ActivityStreamProps {
  readonly width: number
  readonly items: readonly ActivityItem[]
  readonly now: number
  readonly frame: number
  readonly expanded: boolean
}

const frames = ["|", "/", "-", "\\"] as const
const SHELL_MERGE_GAP = " · "

export function activityMark(status: ActivityStatus, frame: number): string {
  if (status === "running") {
    return `[${frames[((frame % frames.length) + frames.length) % frames.length]}]`
  }
  if (status === "succeeded") {
    return "[ok]"
  }
  if (status === "failed") {
    return "[x]"
  }
  if (status === "queued") {
    return "[..]"
  }
  return "[-]"
}

export function formatActivityDuration(durationMs: number): string {
  const seconds = Math.max(0, Math.floor(durationMs / 1_000))
  const minutes = Math.floor(seconds / 60)
  const remainder = seconds % 60
  return minutes > 0 ? `${minutes}m ${remainder}s` : `${remainder}s`
}

/**
 * One shell line as a self-contained sentence: `$ cmd · 4.2s · ok`.
 * Verb/duration/status are derived from the wire shape so the renderer
 * stays a thin mapper — the file does not invent a vocabulary.
 */
function shellSentence(
  item: Extract<ActivityItem, { kind: "shell" }>
): readonly SurfaceTextSegment[] {
  const elapsed =
    typeof item.durationMs === "number"
      ? formatActivityDuration(item.durationMs)
      : null
  const statusTail =
    item.status === "running"
      ? "running"
      : item.status === "succeeded"
        ? "ok"
        : item.status === "failed"
          ? "failed"
          : item.status === "queued"
            ? "queued"
            : "stopped"
  return [
    { text: "  + ", color: tone(item.status) },
    { text: `$ ${item.command}`, bold: item.status === "running" },
    ...(elapsed !== null
      ? [{ text: ` · ${elapsed}`, dim: true } as SurfaceTextSegment]
      : []),
    { text: ` · ${statusTail}`, color: tone(item.status) },
  ]
}

function segmentWidth(segments: readonly SurfaceTextSegment[]): number {
  let total = 0
  for (const segment of segments) {
    total += stripAnsi(segment.text).length
  }
  return total
}

export function activityLines(
  items: readonly ActivityItem[],
  now: number,
  frame: number,
  expanded: boolean
): readonly SurfaceTextSegment[][] {
  const group = items.find((item) => item.kind === "group")
  const children = items.filter((item) => item.kind !== "group")
  const lines: SurfaceTextSegment[][] = []

  if (group?.kind === "group") {
    const verb = group.status === "running" ? "Running" : group.status === "succeeded" ? "Ran" : "Stopped"
    const duration = formatActivityDuration(Math.max(0, now - group.startedAt))
    lines.push([
      { text: `${activityMark(group.status, frame)} `, color: tone(group.status), bold: true },
      { text: `${verb} ${group.total} ${group.label} · ${duration}${group.status === "running" ? "..." : ""}`, bold: true },
      ...(group.status !== "running" && !expanded
        ? [{ text: " · ctrl+o expands details", dim: true }]
        : []),
    ])
  }

  if (group?.kind === "group" && group.status !== "running" && !expanded) {
    return lines
  }

  const shells = children.filter(
    (item): item is Extract<ActivityItem, { kind: "shell" }> => item.kind === "shell"
  )
  const nonShells = children.filter((item) => item.kind !== "shell")

  if (!expanded && shells.length > 1) {
    const sentences = shells.map(shellSentence)
    const firstStatus = shells[0]?.status ?? "running"
    const merged: SurfaceTextSegment[] = [
      { text: "  + ", color: tone(firstStatus) },
    ]
    let visible = 2
    let fitCount = 0
    for (const sentence of sentences) {
      const gap = fitCount === 0 ? [] : [{ text: SHELL_MERGE_GAP, dim: true } as SurfaceTextSegment]
      const gapWidth = gap.reduce((sum, segment) => sum + stripAnsi(segment.text).length, 0)
      const sentenceWidth = segmentWidth(sentence)
      if (visible + gapWidth + sentenceWidth > 64) {
        break
      }
      merged.push(...gap, ...sentence)
      visible += gapWidth + sentenceWidth
      fitCount += 1
    }
    if (fitCount < sentences.length) {
      const omitted = sentences.length - fitCount
      merged.push({ text: SHELL_MERGE_GAP, dim: true })
      merged.push({ text: `+${omitted}`, dim: true })
    }
    lines.push(merged)
  } else {
    for (const item of shells) {
      lines.push([...shellSentence(item)])
      if (expanded && item.outputPreview) {
        lines.push([{ text: `  | ${item.outputPreview}`, dim: true }])
      }
    }
  }

  for (const item of nonShells) {
    if (item.kind === "file") {
      const action = item.action === "loaded" ? "Loaded" : item.action === "read" ? "Read" : "Wrote"
      lines.push([
        { text: "  \\_ ", color: tone(item.status) },
        { text: `${action} ${item.path}` },
      ])
    } else if (item.kind === "tool") {
      lines.push([
        { text: "  \\_ ", color: tone(item.status) },
        { text: `${item.name} · ${item.summary}` },
      ])
    }
  }

  return lines
}

export function ActivityStream({
  width,
  items,
  now,
  frame,
  expanded,
}: ActivityStreamProps) {
  const lines = activityLines(items, now, frame, expanded)
  return (
    <Box flexDirection="column" width={width}>
      {lines.map((segments, index) => (
        <SurfaceLine
          key={index}
          width={width}
          background={index === 0 ? palette.rail : palette.lane}
          segments={segments}
        />
      ))}
    </Box>
  )
}

function tone(status: ActivityStatus): string {
  if (status === "succeeded") {
    return palette.ok
  }
  if (status === "failed") {
    return palette.error
  }
  if (status === "queued") {
    return palette.waiting
  }
  return palette.brand
}

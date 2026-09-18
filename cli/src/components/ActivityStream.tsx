import { Box } from "ink"
import React from "react"
import type { ActivityItem, ActivityStatus } from "../lib/activity"
import { palette } from "../theme"
import { SurfaceLine, type SurfaceTextSegment } from "./SurfaceLine"

export interface ActivityStreamProps {
  readonly width: number
  readonly items: readonly ActivityItem[]
  readonly now: number
  readonly frame: number
  readonly expanded: boolean
}

const frames = ["|", "/", "-", "\\"] as const

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
  for (const item of children) {
    if (item.kind === "shell") {
      lines.push([
        { text: "  + ", color: tone(item.status) },
        { text: `$ ${item.command}` },
        ...(item.durationMs === undefined
          ? []
          : [{ text: ` · ${formatActivityDuration(item.durationMs)}`, dim: true }]),
      ])
      if (item.outputPreview) {
        lines.push([{ text: `  | ${item.outputPreview}`, dim: true }])
      }
    } else if (item.kind === "file") {
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

import type { ChatBlock } from "./sessions"

export type ActivityStatus =
  | "queued"
  | "running"
  | "succeeded"
  | "failed"
  | "cancelled"

export type ActivityItem =
  | {
      readonly kind: "group"
      readonly id: string
      readonly label: string
      readonly total: number
      readonly startedAt: number
      readonly status: ActivityStatus
    }
  | {
      readonly kind: "shell"
      readonly id: string
      readonly command: string
      readonly status: ActivityStatus
      readonly durationMs?: number
      readonly outputPreview?: string
    }
  | {
      readonly kind: "file"
      readonly id: string
      readonly path: string
      readonly action: "read" | "write" | "loaded"
      readonly status: ActivityStatus
    }
  | {
      readonly kind: "tool"
      readonly id: string
      readonly name: string
      readonly summary: string
      readonly status: ActivityStatus
      readonly durationMs?: number
    }

const shellNames = new Set(["shell", "bash", "powershell", "terminal", "exec"])
const readNames = new Set(["read", "load", "open"])
const writeNames = new Set(["write", "edit", "patch"])

export function activityItemsFromTranscript(
  blocks: readonly ChatBlock[],
  thinking: boolean,
  now: number
): readonly ActivityItem[] {
  const items: ActivityItem[] = []
  let lastUserIndex = -1
  for (let index = blocks.length - 1; index >= 0; index -= 1) {
    const block = blocks[index]
    if (block?.kind === "message" && block.message.role === "user") {
      lastUserIndex = index
      break
    }
  }
  for (const block of blocks.slice(lastUserIndex + 1)) {
    if (block.kind !== "message" || block.message.parts == null) {
      continue
    }
    for (let index = 0; index < block.message.parts.length; index += 1) {
      const part = block.message.parts[index]
      if (!part || part.kind !== "tool") {
        continue
      }
      const id = `${block.message.id}-${index}`
      const name = part.name.toLowerCase()
      const input = parseInput(part.inputJson)
      const status = normalizeActivityStatus(part.status)
      // The wire integer fields arrive as number | string (OpenAPI
      // marks int64/int32 string-tolerant) — coerce to a plain number.
      const durationMs =
        part.durationMs == null ? undefined : Number(part.durationMs)
      if (shellNames.has(name)) {
        items.push({
          kind: "shell",
          id,
          command: stringField(input, "command") ?? part.inputJson,
          status,
          ...(durationMs === undefined ? {} : { durationMs }),
          ...(part.outputJson ? { outputPreview: preview(part.outputJson) } : {}),
        })
      } else if (readNames.has(name) || writeNames.has(name)) {
        items.push({
          kind: "file",
          id,
          path: stringField(input, "path") ?? part.inputJson,
          action: writeNames.has(name) ? "write" : name === "load" ? "loaded" : "read",
          status,
        })
      } else {
        items.push({
          kind: "tool",
          id,
          name: part.name,
          summary: summaryFromInput(input, part.inputJson),
          status,
          ...(durationMs === undefined ? {} : { durationMs }),
        })
      }
    }
  }
  if (thinking && items.length === 0) {
    items.push({
      kind: "group",
      id: "current-thinking",
      label: "working",
      total: 1,
      startedAt: now,
      status: "running",
    })
  }
  return items.slice(-8)
}

export function groupForActivity(
  items: readonly ActivityItem[],
  startedAt: number
): ActivityItem | null {
  const members = items.filter((item) => item.kind !== "group")
  if (members.length === 0) {
    return items.find((item) => item.kind === "group") ?? null
  }
  const shells = members.filter((item) => item.kind === "shell").length
  const status: ActivityStatus = members.some((item) => item.status === "running")
    ? "running"
    : members.some((item) => item.status === "queued")
      ? "queued"
    : members.some((item) => item.status === "failed")
      ? "failed"
      : members.every((item) => item.status === "cancelled")
        ? "cancelled"
      : "succeeded"
  return {
    kind: "group",
    id: "current-activity",
    label: shells === members.length ? "shell commands" : "agent operations",
    total: members.length,
    startedAt,
    status,
  }
}

export function normalizeActivityStatus(status: string): ActivityStatus {
  const normalized = status.toLowerCase()
  if (["success", "succeeded", "complete", "completed", "ok"].includes(normalized)) {
    return "succeeded"
  }
  if (["error", "failed", "failure"].includes(normalized)) {
    return "failed"
  }
  if (["cancelled", "canceled"].includes(normalized)) {
    return "cancelled"
  }
  if (["queued", "pending"].includes(normalized)) {
    return "queued"
  }
  return "running"
}

function parseInput(value: string): unknown {
  try {
    return JSON.parse(value)
  } catch {
    return value
  }
}

function stringField(value: unknown, field: string): string | undefined {
  if (typeof value !== "object" || value === null) {
    return undefined
  }
  const candidate = (value as Record<string, unknown>)[field]
  return typeof candidate === "string" ? candidate : undefined
}

function summaryFromInput(value: unknown, fallback: string): string {
  if (typeof value === "string") {
    return preview(value)
  }
  const summary = stringField(value, "summary") ?? stringField(value, "query")
  return preview(summary ?? fallback)
}

function preview(value: string): string {
  const oneLine = value.replace(/\s+/g, " ").trim()
  return oneLine.length <= 80 ? oneLine : `${oneLine.slice(0, 77)}...`
}

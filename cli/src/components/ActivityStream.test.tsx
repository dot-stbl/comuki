import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import React from "react"
import { stripAnsi } from "../theme"
import { ActivityStream, activityLines, activityMark } from "./ActivityStream"
import type { ActivityItem } from "../lib/activity"

const running: readonly ActivityItem[] = [
  {
    kind: "group",
    id: "g",
    label: "shell commands",
    total: 3,
    startedAt: 0,
    status: "running",
  },
  {
    kind: "shell",
    id: "s",
    command: "bun run test",
    outputPreview: "tests running",
    status: "running",
  },
  {
    kind: "file",
    id: "f",
    path: "src/index.ts",
    action: "loaded",
    status: "succeeded",
  },
]

describe("ActivityStream", () => {
  test("renders running hierarchy with discrete animation frame", () => {
    const { lastFrame, unmount } = render(
      <ActivityStream
        width={64}
        items={running}
        now={72_000}
        frame={3}
        expanded={false}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("[\\] Running 3 shell commands · 1m 12s...")
    expect(frame).toContain("+ $ bun run test · running")
    expect(frame).not.toContain("| tests running")
    expect(frame).toContain("\\_ Loaded src/index.ts")
    unmount()
  })

  test("expansion reveals the output preview under the shell line", () => {
    const { lastFrame, unmount } = render(
      <ActivityStream
        width={64}
        items={running}
        now={72_000}
        frame={3}
        expanded={true}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("+ $ bun run test · running")
    expect(frame).toContain("| tests running")
    unmount()
  })

  test("cycles only through the four ASCII frames", () => {
    expect([0, 1, 2, 3, 4].map((frame) => activityMark("running", frame))).toEqual([
      "[|]",
      "[/]",
      "[-]",
      "[\\]",
      "[|]",
    ])
  })

  test("collapses completed detail until ctrl+o state expands it", () => {
    const completed = running.map((item) => ({
      ...item,
      status: "succeeded" as const,
    }))

    expect(activityLines(completed, 78_000, 0, false)).toHaveLength(1)
    expect(activityLines(completed, 78_000, 0, true).length).toBeGreaterThan(1)
  })

  test("each shell line is a complete sentence with duration and status", () => {
    const items: readonly ActivityItem[] = [
      {
        kind: "group",
        id: "g",
        label: "shell commands",
        total: 2,
        startedAt: 0,
        status: "running",
      },
      {
        kind: "shell",
        id: "s1",
        command: "bun run lint",
        status: "succeeded",
        durationMs: 4_200,
      },
      {
        kind: "shell",
        id: "s2",
        command: "bun run test",
        status: "running",
      },
    ]
    const lines = activityLines(items, 16_000, 0, true)
    const flat = lines.map((segments) =>
      segments.map((segment) => segment.text).join("")
    )

    expect(flat.some((line) => line.includes("$ bun run lint · 4s · ok"))).toBe(true)
    expect(flat.some((line) => line.includes("$ bun run test · running"))).toBe(true)
  })

  test("running group with short adjacent shells collapses to one line when it fits", () => {
    const items: readonly ActivityItem[] = [
      {
        kind: "group",
        id: "g",
        label: "shell commands",
        total: 2,
        startedAt: 0,
        status: "running",
      },
      {
        kind: "shell",
        id: "s1",
        command: "bun run lint",
        status: "succeeded",
        durationMs: 4_200,
      },
      {
        kind: "shell",
        id: "s2",
        command: "bun run test",
        status: "succeeded",
        durationMs: 12_000,
      },
    ]
    const lines = activityLines(items, 16_000, 0, false)
    const flat = lines.map((segments) =>
      segments.map((segment) => segment.text).join("")
    )

    expect(lines.length).toBeGreaterThanOrEqual(2)
    const merged = flat[1] ?? ""
    expect(merged).toContain("$ bun run lint")
    expect(merged).toContain("$ bun run test")
    expect(merged).toContain(" · ok")
  })
})

import { describe, expect, test } from "bun:test"
import { activityItemsFromTranscript, groupForActivity } from "./activity"
import type { ChatBlock } from "./sessions"

const blocks: readonly ChatBlock[] = [
  {
    kind: "message",
    key: "m1",
    message: {
      id: "m1",
      role: "assistant",
      content: "",
      toolName: null,
      meta: null,
      createdAt: "2026-09-18T00:00:00Z",
      parts: [
        {
          kind: "tool",
          name: "shell",
          inputJson: '{"command":"bun run test"}',
          outputJson: "42 tests passed",
          status: "running",
        },
        {
          kind: "tool",
          name: "read",
          inputJson: '{"path":"src/index.ts"}',
          status: "success",
        },
      ],
    },
  },
]

describe("activityItemsFromTranscript", () => {
  test("maps current tool parts into semantic shell and file items", () => {
    const items = activityItemsFromTranscript(blocks, true, 1_000)

    expect(items).toEqual([
      {
        kind: "shell",
        id: "m1-0",
        command: "bun run test",
        outputPreview: "42 tests passed",
        status: "running",
      },
      {
        kind: "file",
        id: "m1-1",
        path: "src/index.ts",
        action: "read",
        status: "succeeded",
      },
    ])
  })

  test("derives a running group from member state", () => {
    const items = activityItemsFromTranscript(blocks, true, 1_000)

    expect(groupForActivity(items, 2_000)).toEqual({
      kind: "group",
      id: "current-activity",
      label: "agent operations",
      total: 2,
      startedAt: 2_000,
      status: "running",
    })
  })
})

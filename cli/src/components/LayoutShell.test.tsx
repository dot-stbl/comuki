import { describe, expect, test } from "bun:test"
import { Box, Text } from "ink"
import { render } from "ink-testing-library"
import React from "react"
import { resolveHarnessLayout } from "../lib/harness-layout"
import { stripAnsi } from "../theme"
import { PromptInput } from "./PromptInput"
import { TopBar } from "./TopBar"
import { TranscriptViewport } from "./TranscriptViewport"

interface ConversationShellProps {
  readonly columns: number
  readonly rows: number
  readonly contextual?: boolean
}

function ConversationShell({
  columns,
  rows,
  contextual = false,
}: ConversationShellProps) {
  const layout = resolveHarnessLayout(columns, contextual)
  const composerRows = 2
  const viewportRows = rows - layout.topBarRows - composerRows
  const lines = Array.from(
    { length: viewportRows },
    (_, index) => `transcript-${index + 1}`
  )
  return (
    <Box width={columns} height={rows} flexDirection="column">
      <TopBar
        width={columns}
        mode={layout.mode}
        session="conversation first"
      />
      <Box flexDirection="row" height={rows - layout.topBarRows}>
        <Box
          width={layout.workspaceWidth}
          height={rows - layout.topBarRows}
          flexDirection="column"
        >
          <TranscriptViewport
            lines={lines}
            height={viewportRows}
            offset={0}
            newBelow={false}
            width={layout.workspaceWidth}
          />
          <PromptInput
            onSubmit={() => {}}
            active={false}
            label="project comuki / profile implement"
          />
        </Box>
        {layout.workbenchWidth > 0 ? (
          <Text>{"workbench".padEnd(layout.workbenchWidth)}</Text>
        ) : null}
      </Box>
    </Box>
  )
}

describe("conversation-first shell", () => {
  test("normal wide layout has no session rail, tabs, or footer actions", () => {
    const { lastFrame, unmount } = render(
      <ConversationShell columns={120} rows={30} />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).not.toContain("sessions")
    expect(frame).not.toContain("[1]")
    expect(frame).not.toContain("ctrl+/ actions")
    expect(frame).not.toContain("workbench")
    expect(frame).toContain("conversation first")
    unmount()
  })

  test("transcript receives every row not used by header and composer", () => {
    const rows = 30
    const { lastFrame, unmount } = render(
      <ConversationShell columns={100} rows={rows} />
    )
    const frameRows = stripAnsi(lastFrame() ?? "").split("\n")

    expect(frameRows).toHaveLength(rows)
    expect(frameRows).toContain("transcript-27")
    expect(frameRows.at(-1)).toContain(">")
    unmount()
  })

  test("80x24 keeps the composer visible with at least 15 transcript rows", () => {
    const { lastFrame, unmount } = render(
      <ConversationShell columns={80} rows={24} />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("transcript-21")
    expect(frame).toContain("project comuki / profile implement")
    expect(frame.split("\n").at(-1)).toContain(">")
    unmount()
  })

  test("wide workbench appears only when contextual work exists", () => {
    const ordinary = render(<ConversationShell columns={120} rows={30} />)
    const contextual = render(
      <ConversationShell columns={120} rows={30} contextual />
    )

    expect(stripAnsi(ordinary.lastFrame() ?? "")).not.toContain("workbench")
    expect(stripAnsi(contextual.lastFrame() ?? "")).toContain("workbench")
    ordinary.unmount()
    contextual.unmount()
  })
})

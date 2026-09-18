import { describe, expect, test } from "bun:test"
import { render } from "ink-testing-library"
import React from "react"
import { stripAnsi } from "../theme"
import { NotificationCenter } from "./NotificationCenter"

function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

describe("NotificationCenter", () => {
  test("renders semantic marks and invokes keyboard dismissal", async () => {
    const dismissed: string[] = []
    const { lastFrame, stdin, unmount } = render(
      <NotificationCenter
        width={52}
        terminalTop={1}
        state={{
          items: [
            {
              id: "notice",
              tone: "warning",
              title: "approval waiting",
              createdAt: 0,
              action: { label: "open", command: "/plan" },
            },
          ],
        }}
        onDismiss={(id) => dismissed.push(id)}
        onAction={() => {}}
      />
    )

    expect(stripAnsi(lastFrame() ?? "")).toContain("[!] approval waiting")
    await settle()
    stdin.write("\u0004")
    await settle()
    expect(dismissed).toEqual(["notice"])
    unmount()
  })
})

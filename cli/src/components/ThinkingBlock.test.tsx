/**
 * Smoke renders for the collapsible block component: collapsed shows a
 * single dim summary line (icon + label + badge) and never the body;
 * expanded shows the full thinking markdown / tool args and result.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { render } from "ink-testing-library"
import { ThinkingBlock } from "./ThinkingBlock"
import type { MessagePart } from "../lib/client"
import { stripAnsi } from "../theme"

type Thinking = Extract<MessagePart, { kind: "thinking" }>
type Tool = Extract<MessagePart, { kind: "tool" }>

describe("ThinkingBlock — collapsed / expanded", () => {
  test("collapsed thinking renders one summary line without the body", () => {
    const part: Thinking = {
      kind: "thinking",
      text: "the secret reasoning chain",
      tokens: 3_400,
    }
    const { lastFrame, unmount } = render(
      <ThinkingBlock part={part} expanded={false} width={60} />
    )
    const frame = lastFrame() ?? ""
    expect(stripAnsi(frame)).toBe("  * thinking  3.4k tok")
    expect(frame).not.toContain("secret reasoning")
    unmount()
  })

  test("expanded thinking renders the full body", () => {
    const part: Thinking = { kind: "thinking", text: "visible reasoning" }
    const { lastFrame, unmount } = render(
      <ThinkingBlock part={part} expanded={true} width={60} />
    )
    expect(stripAnsi(lastFrame() ?? "")).toContain("visible reasoning")
    unmount()
  })

  test("collapsed tool renders the call line with a result badge", () => {
    const part: Tool = {
      kind: "tool",
      name: "memory.recall",
      inputJson: `{"query":"identity"}`,
      status: "succeeded",
      outputJson: `{"facts":[1,2]}`,
    }
    const { lastFrame, unmount } = render(
      <ThinkingBlock part={part} expanded={false} width={60} />
    )
    expect(stripAnsi(lastFrame() ?? "")).toContain(
      `* memory.recall("identity")  ok`
    )
    unmount()
  })

  test("expanded tool renders full args and result", () => {
    const part: Tool = {
      kind: "tool",
      name: "memory.recall",
      inputJson: `{"query":"identity"}`,
      status: "succeeded",
      outputJson: `{"facts":[1,2]}`,
    }
    const { lastFrame, unmount } = render(
      <ThinkingBlock part={part} expanded={true} width={60} />
    )
    const frame = stripAnsi(lastFrame() ?? "")
    expect(frame).toContain(`memory.recall("identity")`)
    expect(frame).toContain(`"query": "identity"`)
    expect(frame).toContain(`"facts": [`)
    unmount()
  })
})

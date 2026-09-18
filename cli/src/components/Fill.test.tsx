/**
 * Fill paints a width×height slab of background spaces under children.
 */
import { describe, expect, test } from "bun:test"
import React from "react"
import { Text } from "ink"
import { render } from "ink-testing-library"
import { Fill } from "./Fill"
import { stripAnsi } from "../theme"

describe("Fill", () => {
  test("keeps children visible over the filled band", () => {
    const { lastFrame, unmount } = render(
      <Fill width={20} height={2} color="#222226">
        <Text>hello</Text>
      </Fill>
    )
    const frame = lastFrame() ?? ""
    expect(stripAnsi(frame)).toContain("hello")
    unmount()
  })
})

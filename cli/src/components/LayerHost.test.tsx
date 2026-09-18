import { describe, expect, test } from "bun:test"
import { Text, useInput } from "ink"
import { render } from "ink-testing-library"
import React from "react"
import { stripAnsi } from "../theme"
import { LayerHost } from "./LayerHost"

function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function InputProbe({ active, name, events }: {
  readonly active: boolean
  readonly name: string
  readonly events: string[]
}) {
  useInput(() => events.push(name), { isActive: active })
  return <Text>{name}</Text>
}

describe("LayerHost", () => {
  test("paints notifications after an opaque overlay", () => {
    const { lastFrame, unmount } = render(
      <LayerHost
        width={30}
        height={5}
        base={<Text>base</Text>}
        overlay={<Text>modal</Text>}
        notifications={<Text>notice</Text>}
      />
    )
    const frame = stripAnsi(lastFrame() ?? "")

    expect(frame).toContain("modal")
    expect(frame).toContain("notice")
    unmount()
  })

  test("lets the modal capture input while base stays mounted", async () => {
    const events: string[] = []
    const { stdin, rerender, unmount } = render(
      <LayerHost
        width={30}
        height={5}
        base={<InputProbe active={false} name="base" events={events} />}
        overlay={<InputProbe active name="overlay" events={events} />}
      />
    )

    await settle()
    stdin.write("x")
    await settle()
    expect(events).toEqual(["overlay"])

    rerender(
      <LayerHost
        width={30}
        height={5}
        base={<InputProbe active name="base" events={events} />}
      />
    )
    await settle()
    stdin.write("y")
    await settle()
    expect(events).toEqual(["overlay", "base"])
    unmount()
  })
})

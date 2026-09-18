/**
 * TranscriptSearch tests through ink-testing-library's mock stdin:
 * query editing (insert/backspace/arrows), enter/shift+enter cycling
 * callbacks, esc close, ctrl-combos never reaching the buffer, and
 * the `3/7` counter rendering. The host keeps the query in state —
 * controlled exactly like the chat shell drives it.
 */
import { describe, expect, test } from "bun:test"
import React, { useState } from "react"
import { render } from "ink-testing-library"
import { TranscriptSearch } from "./TranscriptSearch"

function settle(ms = 60): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

const ENTER = "\r"
const ESC = "\x1b"
const LEFT = "\x1b[D"
const BACKSPACE = "\x7f"

interface Recorder {
  value: string
  next: number
  previous: number
  close: number
}

interface HostProps {
  readonly initialValue?: string
  readonly matchCount?: number
  readonly matchIndex?: number
  readonly events: Recorder
}

function Host({
  initialValue = "",
  matchCount = 7,
  matchIndex = 2,
  events,
}: HostProps) {
  const [value, setValue] = useState(initialValue)
  return (
    <TranscriptSearch
      value={value}
      matchCount={matchCount}
      matchIndex={matchIndex}
      onChange={(next) => {
        setValue(next)
        events.value = next
      }}
      onNext={() => {
        events.next += 1
      }}
      onPrevious={() => {
        events.previous += 1
      }}
      onClose={() => {
        events.close += 1
      }}
    />
  )
}

async function harness(
  props: Partial<HostProps> = {}
): Promise<{
  instance: ReturnType<typeof render>
  events: Recorder
}> {
  const events: Recorder = { value: "", next: 0, previous: 0, close: 0 }
  const instance = render(<Host events={events} {...props} />)
  await settle()
  return { instance, events }
}

describe("TranscriptSearch", () => {
  test("renders the prompt, the counter and the key hints", async () => {
    const { instance } = await harness()
    const frame = instance.lastFrame() ?? ""
    expect(frame).toContain("find")
    expect(frame).toContain("3/7")
    expect(frame).toContain("enter next")
    expect(frame).toContain("esc close")
    instance.unmount()
  })

  test("typing builds the query through onChange", async () => {
    const { instance, events } = await harness()
    instance.stdin.write("hel")
    await settle()
    expect(events.value).toBe("hel")
    instance.unmount()
  })

  test("backspace removes the tail char, left arrow then backspace the middle", async () => {
    const { instance, events } = await harness({
      initialValue: "abc",
    })
    instance.stdin.write(BACKSPACE)
    await settle()
    expect(events.value).toBe("ab")
    instance.stdin.write(LEFT)
    await settle()
    instance.stdin.write(BACKSPACE)
    await settle()
    // Cursor sat between `a` and `b` — the backspace ate the `a`.
    expect(events.value).toBe("b")
    instance.unmount()
  })

  test("enter fires next; shift+enter fires previous", async () => {
    const { instance, events } = await harness()
    instance.stdin.write(ENTER)
    await settle()
    expect(events.next).toBe(1)
    instance.stdin.write("\u001b[13;2u")
    await settle()
    expect(events.previous).toBe(1)
    instance.unmount()
  })

  test("esc closes without touching the query", async () => {
    const { instance, events } = await harness({
      initialValue: "keep",
    })
    instance.stdin.write(ESC)
    await settle()
    expect(events.close).toBe(1)
    expect(events.value).toBe("")
    instance.unmount()
  })

  test("ctrl combos never leak into the query buffer", async () => {
    const { instance, events } = await harness()
    // ctrl+f (`\x06` with the ctrl flag), ctrl+n, ctrl+o, ctrl+w.
    instance.stdin.write("\x06")
    await settle()
    instance.stdin.write("\x0e")
    await settle()
    instance.stdin.write("\x0f")
    await settle()
    instance.stdin.write("\x17")
    await settle()
    expect(events.value).toBe("")
    expect(events.close).toBe(0)
    expect(events.next).toBe(0)
    instance.unmount()
  })

  test("zero matches render a 0/0 counter", async () => {
    const { instance } = await harness({
      matchCount: 0,
      matchIndex: 0,
    })
    expect(instance.lastFrame() ?? "").toContain("0/0")
    instance.unmount()
  })
})

/**
 * Pure subscribe/unsubscribe logic for the dimensions hook — tests the
 * resize event plumbing without spinning up an Ink render tree. The
 * React hook itself is exercised via the component tests.
 */
import { describe, expect, test } from "bun:test"
import { EventEmitter } from "node:events"
import {
  FALLBACK_DIMENSIONS,
  subscribeDimensions,
  type DimensionsStream,
} from "./useStdoutDimensions"

/** Mutable fake stdout: tty-shaped with EventEmitter plumbing. */
class FakeStream extends EventEmitter implements DimensionsStream {
  columns: number
  rows: number
  constructor(initial: { columns: number; rows: number }) {
    super()
    this.columns = initial.columns
    this.rows = initial.rows
  }
  resize(): void {
    this.emit("resize")
  }
}

describe("subscribeDimensions", () => {
  test("emits the current snapshot on subscribe", () => {
    const stream = new FakeStream({ columns: 120, rows: 40 })
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(stream, (dims) => {
      seen.push(dims)
    })
    unsubscribe()
    expect(seen).toEqual([{ columns: 120, rows: 40 }])
  })

  test("re-emits when the stream fires resize", () => {
    const stream = new FakeStream({ columns: 80, rows: 24 })
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(stream, (dims) => {
      seen.push(dims)
    })

    stream.columns = 132
    stream.rows = 50
    stream.resize()

    expect(seen.length).toBe(2)
    expect(seen[0]).toEqual({ columns: 80, rows: 24 })
    expect(seen[1]).toEqual({ columns: 132, rows: 50 })
    unsubscribe()
  })

  test("falls back when columns / rows are undefined", () => {
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(
      {
        columns: undefined,
        rows: undefined,
        on: () => {},
        off: () => {},
      },
      (dims) => {
        seen.push(dims)
      }
    )
    unsubscribe()
    expect(seen[0]).toEqual(FALLBACK_DIMENSIONS)
  })

  test("null stream → emits fallback and returns a noop unsubscribe", () => {
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(null, (dims) => {
      seen.push(dims)
    })
    expect(typeof unsubscribe).toBe("function")
    expect(() => unsubscribe()).not.toThrow()
    expect(seen[0]).toEqual(FALLBACK_DIMENSIONS)
  })

  test("multiple resize events update in order", () => {
    const stream = new FakeStream({ columns: 80, rows: 24 })
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(stream, (dims) => {
      seen.push(dims)
    })

    stream.columns = 100
    stream.resize()
    stream.rows = 30
    stream.resize()
    stream.columns = 200
    stream.rows = 60
    stream.resize()

    expect(seen.map((s) => `${s.columns}x${s.rows}`)).toEqual([
      "80x24",
      "100x24",
      "100x30",
      "200x60",
    ])
    unsubscribe()
  })

  test("unsubscribe stops further resize updates", () => {
    const stream = new FakeStream({ columns: 80, rows: 24 })
    const seen: { columns: number; rows: number }[] = []
    const unsubscribe = subscribeDimensions(stream, (dims) => {
      seen.push(dims)
    })

    stream.columns = 100
    stream.resize()
    expect(seen.length).toBe(2)
    unsubscribe()

    stream.columns = 200
    stream.resize()
    expect(seen.length).toBe(2)
  })
})

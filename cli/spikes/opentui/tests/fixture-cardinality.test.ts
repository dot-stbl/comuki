/**
 * Fixture cardinality test.
 *
 * The audit requires `buildTranscript(count)` to produce exactly
 * `count` final entries. The original version emitted an extra
 * `code` row every 11th iteration, giving 1091 from a 1000-count
 * call. This test pins the corrected behaviour.
 */
import { describe, expect, test } from "bun:test"
import { buildTranscript } from "../src/fixtures/transcript-1000.js"

describe("transcript fixture cardinality", () => {
  test("buildTranscript(1000) returns exactly 1000 entries", () => {
    const entries = buildTranscript(1000)
    expect(entries.length).toBe(1000)
  })

  test("buildTranscript(n) returns exactly n entries for small n", () => {
    expect(buildTranscript(0).length).toBe(0)
    expect(buildTranscript(1).length).toBe(1)
    expect(buildTranscript(10).length).toBe(10)
    expect(buildTranscript(50).length).toBe(50)
  })

  test("every outer iteration emits exactly one entry — no extras, no omissions", () => {
    for (const n of [0, 1, 11, 50, 100, 250, 1000]) {
      const ids = buildTranscript(n).map((e) => e.id)
      expect(ids.length).toBe(n)
      // ids are unique within the run
      expect(new Set(ids).size).toBe(n)
    }
  })
})

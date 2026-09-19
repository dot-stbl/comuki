/**
 * 1,000-entry fixture measurement for the Core spike.
 *
 * Loads the spike's transcript fixture (the same one the chat shell
 * renders), measures:
 *   - build time (1 000 entries)
 *   - flatten time at 80 cols with focusMode: true
 *   - flattened row count
 *   - longest row (cell width)
 *
 * Pure — no renderer, no TTY. Deterministic.
 */
import { performance } from "node:perf_hooks"
import { buildTranscript, flattenTranscript } from "../src/fixtures/transcript-1000.js"

function main() {
  const buildStart = performance.now()
  const entries = buildTranscript()
  const buildMs = performance.now() - buildStart

  const flattenStart = performance.now()
  const lines = flattenTranscript(entries, {
    focusMode: true,
    width: 80,
    expanded: false,
  })
  const flattenMs = performance.now() - flattenStart

  let maxLen = 0
  for (const row of lines) {
    if (row.length > maxLen) maxLen = row.length
  }

  console.log(
    JSON.stringify(
      {
        fixture: "transcript-1000",
        entryCount: entries.length,
        buildMs: round(buildMs),
        flattenMs80: round(flattenMs),
        flattenedRows: lines.length,
        longestRowLen: maxLen,
      },
      null,
      2,
    ),
  )

  process.exit(0)
}

function round(n: number): number {
  return Math.round(n * 100) / 100
}

main()

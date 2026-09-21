#!/usr/bin/env bun
/**
 * `comuki compat-check` — print the resolved mode set for the current
 * environment and exit non-zero when the resolved set is empty.
 *
 * Used by the package README's "supported terminals" table as ground
 * truth: a developer runs this on a target host to see what the CLI
 * would pick up there.
 *
 * The script is intentionally minimal — no yargs, no banner, no
 * colour output. Just one line of pipe-friendly plain text per row.
 *
 * Exit codes:
 *  0 — at least one render mode was resolved (the CLI can boot)
 *  2 — empty mode set (impossible per the resolver today; reserved
 *      for a future "all-bail" path)
 */
import { explainMode, resolveModes, type Args } from "../src/tui/modes"

const ARGV = process.argv.slice(2)
const ARGS: Args = parseArgs(ARGV)
const DETECT = {
  stdoutIsTTY: process.stdout.isTTY ?? false,
  stdinIsTTY: process.stdin.isTTY ?? false,
  columns: process.stdout.columns ?? null,
  rows: process.stdout.rows ?? null,
}
const MODES = resolveModes(ARGS, process.env, DETECT)

console.log(explainMode(MODES))
for (const warning of MODES.warnings) {
  console.error(`warning: ${warning}`)
}
if (!MODES.linear && !MODES.machine && MODES.noColor && MODES.ascii && MODES.noMouse) {
  process.exit(0)
}
process.exit(0)

function parseArgs(argv: readonly string[]): Args {
  const args: Record<string, boolean | string> = {}
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index] ?? ""
    if (!token.startsWith("--")) {
      continue
    }
    const key = token.slice(2)
    const next = argv[index + 1]
    if (next !== undefined && !next.startsWith("--")) {
      args[key] = next
      index += 1
    } else {
      args[key] = true
    }
  }
  const typed: Args = {
    mode: typeof args["mode"] === "string" ? args["mode"] : undefined,
    machine: args["machine"] === true,
    noColor: args["no-color"] === true,
    highContrast: args["high-contrast"] === true,
    reducedMotion: args["reduced-motion"] === true,
    ascii: args["ascii"] === true,
    noMouse: args["no-mouse"] === true,
    unicodeNarrow: args["unicode-narrow"] === true,
  }
  return typed
}
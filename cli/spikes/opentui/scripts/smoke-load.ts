/**
 * Smoke check — can the prototype load @opentui/core without exploding?
 *
 * Run with:
 *   cd cli/spikes/opentui && bun run scripts/smoke-load.ts
 *
 * Does NOT create a renderer (which would touch the terminal). It imports
 * the public surface and asserts the names we plan to use in the
 * prototype actually exist.
 */
import {
  BoxRenderable,
  InputRenderable,
  ScrollBoxRenderable,
  TextRenderable,
  SelectRenderable,
  TextareaRenderable,
  createCliRenderer,
} from "@opentui/core"

const required = {
  BoxRenderable,
  InputRenderable,
  ScrollBoxRenderable,
  TextRenderable,
  SelectRenderable,
  TextareaRenderable,
  createCliRenderer,
} as const

let ok = true
for (const [name, value] of Object.entries(required)) {
  if (typeof value === "undefined") {
    console.error(`MISSING: ${name}`)
    ok = false
  } else {
    console.log(`OK ${name}`)
  }
}

if (!ok) {
  process.exit(1)
}

console.log("---")
console.log(`@opentui/core loaded. node runtime OK.`)
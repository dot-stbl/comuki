/**
 * Build a standalone executable from the Core-mode spike.
 *
 * Mirrors `cli/scripts/build.ts` (which builds `comuki` from the Ink
 * stack): `Bun.build({ compile: true, target: "bun" })`. The spike has
 * no react-devtools shim because the Core path doesn't import React.
 *
 * Output:
 *   - `cli/spikes/opentui/comuki-opentui-spike.exe` (Windows)
 *   - `cli/spikes/opentui/comuki-opentui-spike` (POSIX)
 *
 * This is the **evidence** the ADR's "packaging" section asks for:
 * can the host produce a single-file binary on Bun's compile pipeline
 * with the OpenTUI native module bundled?
 */
import { existsSync } from "node:fs"

const isWindows = process.platform === "win32"
const outfile = `comuki-opentui-spike${isWindows ? ".exe" : ""}`

console.log(`Building ${outfile} (target=bun, compile=true)…`)

const result = await Bun.build({
  entrypoints: ["bin/opentui-spike.ts"],
  target: "bun",
  compile: {
    outfile,
  },
})

if (!result.success) {
  for (const message of result.logs) {
    console.error(message)
  }
  process.exit(1)
}

const exists = existsSync(outfile)
console.log(`OK — wrote ${outfile}${exists ? " (verified)" : " (build succeeded but file not found)"}`)
process.exit(exists ? 0 : 1)
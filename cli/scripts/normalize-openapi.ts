/**
 * Normalize the freshly-built `artifacts/openapi.json` to LF and write the
 * result next to it as `artifacts/openapi.normalized.json`. The kubb CLI
 * reads JSON description text verbatim, so the .NET OpenAPI emitter's
 * `Environment.NewLine` (CRLF on Windows, LF on Linux) propagates straight
 * into the generated tree and the drift gate drifts by host platform.
 *
 * Both `scripts/contracts-drift.ts` (gate) and `bun run generate:contracts`
 * (manual regen) use this script. The drift gate additionally sets
 * `KUBB_INPUT_SPEC` to point kubb at the normalized file; the manual regen
 * relies on `kubb.config.ts` reading the env override.
 *
 * The destination file is overwritten on every run; it lives in the
 * gitignored `artifacts/` tree next to the source spec.
 */

import { readFileSync, writeFileSync } from "node:fs"
import { dirname, resolve } from "node:path"

const cliCwd = dirname(import.meta.dir)
const sourcePath = resolve(cliCwd, "../artifacts/openapi.json")
const destPath = resolve(cliCwd, "../artifacts/openapi.normalized.json")

const text = readFileSync(sourcePath, "utf8")
const normalized = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n")

writeFileSync(destPath, normalized, "utf8")
console.error(`[normalize-openapi] wrote ${destPath}`)

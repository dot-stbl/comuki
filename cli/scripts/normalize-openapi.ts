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
const specPath = resolve(cliCwd, "../artifacts/openapi.json")

const text = readFileSync(specPath, "utf8")
// Two passes:
//   1. Strip actual CR+LF bytes (Windows line endings inside string
//      values, e.g. when an emitter preserved Environment.NewLine).
//   2. Strip the literal 4-char text `\r\n` that the .NET OpenAPI
//      emitter writes into JSON description strings. The escape
//      sequence is *text* at this point (no actual CR/LF bytes), so
//      step 1 misses it; without step 2 the kubb regen output drifts
//      by host because Linux contributors' `dotnet build` produces
//      the shorter `\n` text instead.
const normalized = text
  .replace(/\r\n/g, "\n")
  .replace(/\r/g, "\n")
  .replace(/\\r\\n/g, "\\n")

// Overwrite the spec in place. Both the spec and this helper live
// under `artifacts/`, which is gitignored, so writing here doesn't
// risk polluting a tracked file. The drift gate and `generate:contracts`
// both run kubb right after this script, so the on-disk spec is
// always the normalized one by the time kubb opens it.
writeFileSync(specPath, normalized, "utf8")
console.error(`[normalize-openapi] wrote ${specPath}`)

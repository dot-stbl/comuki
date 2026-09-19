import { describe, expect, it } from "bun:test"
import { readdirSync, readFileSync, statSync } from "node:fs"
import { join, relative } from "node:path"

/**
 * Dependency-direction invariant (issue #83): kernel/domain modules
 * import no Ink, no React, no OpenTUI, no ANSI helpers, no terminal
 * dimensions. A violation here fails CI before it reaches a renderer.
 */
const GUARDED_ROOTS = [join(import.meta.dir, "..", "harness"), import.meta.dir]

/**
 * Renderer-adjacent modules the kernel must never touch — by package
 * name or by path suffix. `import type` counts too: even type-level
 * coupling would leak renderer vocabulary into the domain.
 */
const BANNED_PACKAGES = new Set(["react", "ink", "react-dom", "@opentui/core"])
const BANNED_PATH_PATTERNS: readonly RegExp[] = [
  /(^|\/)\.\.\/theme(\.ts)?$/,
  /(^|\/)\.\.(\/\.\.)?\/theme(\.ts)?$/,
  /theme\/themes?(\.ts)?$/,
  /(^|\/)\.\.\/(\.\.\/)?lib\/(format|term|mouse|markdown|queue|transcript)(\.ts)?$/,
  /(^|\/)\.\.\/(\.\.\/)?hooks\//,
  /(^|\/)\.\.\/(\.\.\/)?components\//,
  /(^|\/)\.\.\/(\.\.\/)?commands\//,
  /(^|\/)spikes\//,
]

function listFiles(root: string): string[] {
  const entries: string[] = []
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory)) {
      const full = join(directory, entry)
      if (statSync(full).isDirectory()) {
        walk(full)
        continue
      }
      if (entry.endsWith(".ts") || entry.endsWith(".tsx")) {
        entries.push(full)
      }
    }
  }
  walk(root)
  return entries
}

const IMPORT_PATTERN = /(?:from\s+|import\s*\(\s*)["']([^"']+)["']/g

function bannedSpecifier(specifier: string): string | null {
  if (BANNED_PACKAGES.has(specifier)) {
    return `package "${specifier}"`
  }
  for (const pattern of BANNED_PATH_PATTERNS) {
    if (pattern.test(specifier)) {
      return `module "${specifier}"`
    }
  }
  return null
}

describe("kernel dependency direction guard", () => {
  it("kernel and harness modules import no renderer dependencies", () => {
    const violations: string[] = []
    let scanned = 0

    for (const root of GUARDED_ROOTS) {
      for (const file of listFiles(root)) {
        scanned += 1
        const contents = readFileSync(file, "utf8")
        for (const match of contents.matchAll(IMPORT_PATTERN)) {
          const specifier = match[1] as string
          const banned = bannedSpecifier(specifier)
          if (banned !== null) {
            violations.push(
              `${relative(root, file)} imports ${banned}`
            )
          }
        }
      }
    }

    // The scan must actually see the kernel — an empty glob would
    // otherwise make this test pass vacuously.
    expect(scanned).toBeGreaterThan(10)
    expect(violations).toEqual([])
  })

  it("resolves every relative import inside the guarded roots to a real file", () => {
    // Catches a renamed/moved module leaving a dangling kernel import.
    for (const root of GUARDED_ROOTS) {
      for (const file of listFiles(root)) {
        const contents = readFileSync(file, "utf8")
        for (const match of contents.matchAll(IMPORT_PATTERN)) {
          const specifier = match[1] as string
          if (!specifier.startsWith(".")) {
            continue
          }
          const resolved = resolveSpecifier(file, specifier)
          if (resolved === null) {
            expect(`${relative(root, file)} → ${specifier}`).toBe("resolvable")
          }
        }
      }
    }
  })
})

function resolveSpecifier(fromFile: string, specifier: string): string | null {
  const candidates = [
    join(fromFile, "..", specifier),
    join(fromFile, "..", `${specifier}.ts`),
    join(fromFile, "..", `${specifier}.tsx`),
    join(fromFile, "..", specifier, "index.ts"),
  ]
  for (const candidate of candidates) {
    try {
      if (statSync(candidate).isFile()) {
        return candidate
      }
    } catch {
      // Candidate does not exist — try the next extension.
    }
  }
  return null
}

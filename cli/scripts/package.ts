/**
 * `bun run package` — single-file CLI build per target (issue #81).
 *
 * Re-uses the existing `scripts/build.ts` for the host Bun target
 * (the spike-approved entrypoint stays the same), and adds
 * cross-compilation for `linux-x64`, `darwin-arm64`, and
 * `windows-x64`. Each binary lands in `cli/dist/<target>/comuki[.exe]`.
 *
 * Why a script instead of `bun build --compile` directly?
 *
 *   - The `react-devtools-core` shim (see `scripts/build.ts`) is a
 *     ink@5 requirement; it must stay identical across targets.
 *   - We want a stable, predictable output directory so the
 *     release workflow's `actions/upload-artifact` step finds every
 *     binary without globbing.
 *   - `minify: false` keeps the source map stack traces inside the
 *     `diagnostics.log` readable to a maintainer — a panic in
 *     production is the one moment we cannot afford opaque frames.
 *
 * The non-mock `package` script runs on the agent's machine in CI;
 * the release workflow re-runs the same targets on a fresh runner.
 */

import { mkdir, rm, stat } from "node:fs/promises"
import { join, resolve } from "node:path"

// ---------------------------------------------------------------------------
// Target matrix — pin the three supported platforms
// ---------------------------------------------------------------------------

interface PackageTarget {
  readonly os: "linux" | "darwin" | "windows"
  readonly arch: "x64" | "arm64"
  /** `comuki` or `comuki.exe`. */
  readonly binary: string
}

// ---------------------------------------------------------------------------
// react-devtools-core shim — copied verbatim from build.ts; see that
// file for the rationale.
// ---------------------------------------------------------------------------

const stubReactDevtools: import("bun").BunPlugin = {
  name: "stub-react-devtools-core",
  setup(builder) {
    builder.onResolve({ filter: /^react-devtools-core$/ }, () => ({
      path: "stub:react-devtools-core",
      namespace: "stub",
    }))
    builder.onLoad({ filter: /.*/, namespace: "stub" }, () => ({
      contents: "export default {};",
      loader: "js",
    }))
  },
}

interface PackageOptions {
  /**
   * Subset of TARGETS to build. Default = every target the agent's
   * machine can cross-compile. CI overrides per workflow run.
   */
  readonly targets?: ReadonlyArray<PackageTarget>
  /**
   * Override the output directory. Default = `cli/dist`.
   */
  readonly outputDirectory?: string
  /**
   * Skip a clean rebuild of the dist directory. Default = clean.
   * Set `false` for inner-loop iteration where a previous build is
   * still on disk.
   */
  readonly clean?: boolean
}

export interface PackageReport {
  readonly outputDirectory: string
  readonly binaries: ReadonlyArray<{
    readonly target: PackageTarget
    readonly absolute: string
    readonly size: number
  }>
}

/**
 * Build every requested target. Each build runs sequentially —
 * parallel builds trip the `--compile` cache on macOS and confuse
 * the staging directory. The release workflow runs on a single
 * runner so the wall-clock cost is acceptable.
 *
 * Cross-compilation note: Bun cannot cross-compile native binaries
 * from Windows to Linux/Darwin. The release workflow runs each
 * target on a runner of the matching platform; local development
 * falls back to the host target only (see `defaultTargetsForHost`).
 */
export async function packageCli(options: PackageOptions = {}): Promise<PackageReport> {
  const cliRoot = resolve(import.meta.dir, "..")
  const outputDirectory = options.outputDirectory ?? join(cliRoot, "dist")
  const targets = options.targets ?? defaultTargetsForHost()
  const clean = options.clean ?? true
  if (clean) {
    await rm(outputDirectory, { recursive: true, force: true })
  }
  await mkdir(outputDirectory, { recursive: true })

  const binaries: Array<{
    target: PackageTarget
    absolute: string
    size: number
  }> = []
  for (const target of targets) {
    const targetDir = join(outputDirectory, `${target.os}-${target.arch}`)
    await mkdir(targetDir, { recursive: true })
    const absolute = join(targetDir, target.binary)
    process.stderr.write(`[package] building ${target.os}-${target.arch} → ${absolute}\n`)
    const result = await Bun.build({
      entrypoints: [join(cliRoot, "bin", "comuki.ts")],
      target: `bun-${target.os}-${target.arch}` as never,
      compile: true,
      outfile: target.binary,
      plugins: [stubReactDevtools],
      minify: false,
    })
    if (!result.success) {
      for (const message of result.logs ?? []) {
        process.stderr.write(`[package] log: ${JSON.stringify(message)}\n`)
      }
      throw new Error(
        `bun build failed for ${target.os}-${target.arch}`
      )
    }
    // Bun.build writes `outfile` relative to cwd; move it to the
    // target directory so the release workflow's artifact upload
    // finds every binary under `dist/<target>/`.
    const cwdOutfile = join(process.cwd(), target.binary)
    try {
      await Bun.$`mv ${cwdOutfile} ${absolute}`.quiet()
    } catch (moveError: unknown) {
      process.stderr.write(
        `[package] move failed: ${moveError instanceof Error ? moveError.message : String(moveError)}\n`
      )
      throw new Error(
        `could not move ${cwdOutfile} to ${absolute}`
      )
    }
    const info = await stat(absolute)
    binaries.push({ target, absolute, size: info.size })
  }

  return { outputDirectory, binaries }
}

export const TARGETS: ReadonlyArray<PackageTarget> = [
  { os: "linux", arch: "x64", binary: "comuki" },
  { os: "darwin", arch: "arm64", binary: "comuki" },
  { os: "windows", arch: "x64", binary: "comuki.exe" },
]

/**
 * Default target set when no `targets` are passed. On a developer
 * machine we build only the host target — Bun cannot cross-compile
 * a Windows runner into Linux/Darwin. The release workflow's three
 * platform jobs each run the host target.
 */
export function defaultTargetsForHost(): ReadonlyArray<PackageTarget> {
  const hostTarget: PackageTarget = {
    os: process.platform === "win32" ? "windows" : process.platform === "darwin" ? "darwin" : "linux",
    arch: process.arch === "arm64" ? "arm64" : "x64",
    binary: process.platform === "win32" ? "comuki.exe" : "comuki",
  }
  return [hostTarget]
}

// ---------------------------------------------------------------------------
// Script entry — prints a one-line report per target and exits 0.
// ---------------------------------------------------------------------------

if (import.meta.main) {
  packageCli()
    .then((report) => {
      for (const binary of report.binaries) {
        const mb = (binary.size / (1024 * 1024)).toFixed(2)
        process.stdout.write(
          `${binary.target.os}-${binary.target.arch} → ${binary.absolute} (${mb} MiB)\n`
        )
      }
      process.exit(0)
    })
    .catch((error: unknown) => {
      process.stderr.write(
        `package: ${error instanceof Error ? error.message : String(error)}\n`
      )
      process.exit(1)
    })
}

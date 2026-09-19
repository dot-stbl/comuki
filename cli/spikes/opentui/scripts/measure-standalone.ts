/**
 * Build the standalone host binary and report its size + SHA-256.
 *
 * This script runs `scripts/build-standalone.ts` and additionally
 * hashes the produced artefact so the ADR can record exact numbers
 * without committing the binary itself. The binary is gitignored.
 */
import { createHash } from "node:crypto"
import { existsSync, readFileSync, statSync } from "node:fs"
import { spawn } from "node:child_process"

const isWindows = process.platform === "win32"
const outfile = `comuki-opentui-spike${isWindows ? ".exe" : ""}`

function run(cmd: string, args: readonly string[]): Promise<{ code: number | null; stdout: string; stderr: string }> {
  return new Promise((resolve) => {
    const child = spawn(cmd, args, { stdio: ["ignore", "pipe", "pipe"], shell: isWindows })
    let stdout = ""
    let stderr = ""
    child.stdout?.on("data", (b) => (stdout += b.toString()))
    child.stderr?.on("data", (b) => (stderr += b.toString()))
    child.on("close", (code) => resolve({ code, stdout, stderr }))
  })
}

async function main() {
  console.log(`Building ${outfile}…`)
  const buildResult = await run("bun", ["run", "scripts/build-standalone.ts"])
  if (buildResult.code !== 0) {
    console.error("build failed:")
    console.error(buildResult.stderr)
    process.exit(1)
  }
  console.log(buildResult.stdout.trimEnd())

  if (!existsSync(outfile)) {
    console.error(`Build did not produce ${outfile}`)
    process.exit(1)
  }

  const stat = statSync(outfile)
  const bytes = readFileSync(outfile)
  const sha = createHash("sha256").update(bytes).digest("hex")

  console.log(JSON.stringify({
    outfile,
    sizeBytes: stat.size,
    sizeMB: Number((stat.size / 1024 / 1024).toFixed(2)),
    sha256: sha,
    platform: process.platform,
    arch: process.arch,
    bunVersion: (await run("bun", ["--version"])).stdout.trim(),
  }, null, 2))

  console.log("")
  console.log("To remove the binary:")
  console.log(`  rm ${outfile}`)
}

main().catch((err) => {
  console.error("measure-standalone failed:", err)
  process.exit(1)
})

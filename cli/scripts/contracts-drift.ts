/**
 * Drift gate for the committed CLI contract tree (issue #84).
 *
 * Two phases:
 *   1. Regenerate every artifact: dotnet build re-emits the OpenAPI spec,
 *      kubb rewrites src/contracts/_generated/http, the realtime codegen
 *      rewrites src/contracts/_generated/realtime.ts.
 *   2. Fail if `git status`/`git diff` sees any change under
 *      src/contracts/_generated/. The contract is committed and must
 *      match what the server contract emits; a stale tree means a server
 *      API moved without a matching client regen.
 *
 * Read-only git usage — never `git add`, `git stash`, or anything that
 * mutates the worktree.
 *
 * The dotnet emitter writes JSON description text with platform-specific
 * line endings (CRLF on Windows hosts, LF on Linux). Kubb then stringifies
 * that text into the generated schema JSON, so the bytes diverge by
 * platform. We normalize the openapi spec in place before kubb reads
 * it. The normalize script lives under `artifacts/` (gitignored), so the
 * workspace stays clean for the next `dotnet build`.
 */

import { dirname } from "node:path"

const cliCwd = dirname(import.meta.dir)

const driftPath = "src/contracts/_generated"

interface SpawnStep {
  readonly name: string
  readonly cmd: ReadonlyArray<string>
}

const regenSteps: ReadonlyArray<SpawnStep> = [
  {
    name: "dotnet build",
    cmd: ["dotnet", "build", "../comuki.slnx", "-c", "Debug"],
  },
  {
    name: "kubb generate",
    cmd: ["bunx", "@kubb/cli@4.39.2", "generate"],
  },
  {
    name: "realtime codegen",
    cmd: [
      "dotnet",
      "run",
      "--project",
      "../tools/Comuki.Codegen.Realtime",
      "-c",
      "Debug",
      "--no-build",
      "--",
      "--out",
      `${driftPath}/realtime.ts`,
    ],
  },
]

/**
 * Run the standalone normalize-openapi helper. The helper handles its own
 * logging and exit codes; we delegate so the same normalization is used
 * by `bun run generate:contracts` and the drift gate.
 */
async function normalizeOpenapiSpec(): Promise<void> {
  const proc = Bun.spawn(["bun", "scripts/normalize-openapi.ts"], {
    cwd: cliCwd,
    stdio: inheritStdio,
    env: process.env,
  })
  const code = await proc.exited
  if (code !== 0) {
    throw new Error(`normalize-openapi.ts exited with code ${code}`)
  }
}

type Stdio = [Bun.SpawnOptions.Stdio, Bun.SpawnOptions.Stdio, Bun.SpawnOptions.Stdio]

const inheritStdio: Stdio = ["inherit", "inherit", "inherit"]

async function runRegenStep(
  name: string,
  cmd: ReadonlyArray<string>,
  env: Record<string, string | undefined>,
): Promise<number> {
  console.error(`\n[contracts-drift] step: ${name}`)
  let proc: Bun.Subprocess
  try {
    proc = Bun.spawn([...cmd], {
      cwd: cliCwd,
      stdio: inheritStdio,
      env,
    })
  } catch (error) {
    console.error(`[contracts-drift] FAIL: failed to spawn ${name}:`, error)
    return 1
  }
  return await proc.exited
}

async function runGitDiff(): Promise<number> {
  console.error(`\n[contracts-drift] step: git diff --exit-code -- ${driftPath}`)
  const proc = Bun.spawn(
    ["git", "diff", "--exit-code", "--", driftPath],
    { cwd: cliCwd, stdio: inheritStdio },
  )
  return await proc.exited
}

async function runGitStatusPorcelain(): Promise<string> {
  console.error(
    `\n[contracts-drift] step: git status --porcelain -- ${driftPath}`,
  )
  const proc = Bun.spawn(
    ["git", "status", "--porcelain", "--", driftPath],
    { cwd: cliCwd, stdio: ["inherit", "pipe", "inherit"] },
  )
  const stdout = await new Response(proc.stdout).text()
  await proc.exited
  return stdout
}

export async function main(): Promise<number> {
  const env: Record<string, string | undefined> = {
    ...process.env,
    KUBB_DISABLE_TELEMETRY: "1",
  }

  for (const step of regenSteps) {
    if (step.name === "kubb generate") {
      try {
        await normalizeOpenapiSpec()
      } catch (error) {
        console.error(
          `[contracts-drift] FAIL: openapi spec normalization failed:`,
          error,
        )
        return 1
      }
    }

    const code = await runRegenStep(step.name, step.cmd, env)
    if (code !== 0) {
      console.error(
        `[contracts-drift] FAIL: ${step.name} exited with code ${code}; ` +
          "fix the failure above, then re-run `bun run test:contracts`.",
      )
      return 1
    }
  }

  const diffCode = await runGitDiff()
  const statusOut = await runGitStatusPorcelain()
  const statusTrimmed = statusOut.trim()

  if (diffCode !== 0 || statusTrimmed !== "") {
    console.error(
      `\n[contracts-drift] FAIL: ${driftPath} is out of sync with the server contract.`,
    )
    if (statusTrimmed !== "") {
      console.error("[contracts-drift] porcelain output:")
      console.error(statusOut)
    }
    console.error(
      `[contracts-drift] remediation: run \`bun run generate:contracts\` from cli/ ` +
        "and commit the result.",
    )
    return 1
  }

  console.log("contracts in sync")
  return 0
}

if (import.meta.main) {
  process.exit(await main())
}

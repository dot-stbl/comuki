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

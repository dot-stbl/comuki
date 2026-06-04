#!/usr/bin/env bun
/**
 * Boots Comuki.Platform.Api.Public, fetches /openapi/v1.json, writes
 * openapi.v1.json, then kills the dotnet process. Pure dev ergonomics —
 * no MSBuild gymnastics, no Swashbuckle, just process lifecycle over
 * the runtime-emitted spec.
 *
 * Called by `bun run generate-api` (which then runs Kubb on the result).
 * Also runs implicitly via `predev` before `bun run dev`.
 */

import { spawn, spawnSync } from "node:child_process"
import { existsSync, writeFileSync } from "node:fs"
import { setTimeout as sleep } from "node:timers/promises"
import { platform } from "node:process"

const PORT = Number(process.env.COMUKI_API_PORT ?? 5599)
const READY_TIMEOUT_MS = 45_000
const HEALTH_POLL_MS = 500
const API_PROJECT =
  "../platform/src/application/api/Comuki.Platform.Api.Public"
const OUTPUT = "./openapi.v1.json"
const IS_WIN = platform === "win32"

const isPortServing = async (port: number): Promise<boolean> => {
  try {
    const res = await fetch(`http://localhost:${port}/health`, {
      signal: AbortSignal.timeout(HEALTH_POLL_MS),
    })
    return res.ok
  } catch {
    return false
  }
}

const fetchSpec = async (port: number): Promise<string> => {
  const res = await fetch(`http://localhost:${port}/openapi/v1.json`)
  if (!res.ok) {
    throw new Error(
      `GET /openapi/v1.json returned HTTP ${res.status} ${res.statusText}. ` +
        "Is ASPNETCORE_ENVIRONMENT=Development? OpenAPI is dev-only.",
    )
  }
  return res.text()
}

const startDotnet = () => {
  console.log(`→ Starting dotnet run on :${PORT} (env=Development)`)
  return spawn(
    "dotnet",
    [
      "run",
      "--project",
      API_PROJECT,
      "--no-launch-profile",
      "-c",
      "Debug",
    ],
    {
      detached: !IS_WIN,
      env: {
        ...process.env,
        ASPNETCORE_URLS: `http://localhost:${PORT}`,
        ASPNETCORE_ENVIRONMENT: "Development",
      },
      stdio: ["ignore", "inherit", "inherit"],
    },
  )
}

const killByPid = (pid: number) => {
  if (IS_WIN) {
    spawnSync("taskkill", ["/F", "/T", "/PID", String(pid)], { stdio: "ignore" })
  } else {
    try {
      process.kill(-pid, "SIGTERM")
    } catch {
      // child already gone
    }
  }
}

const waitForReady = async (port: number) => {
  const deadline = Date.now() + READY_TIMEOUT_MS
  while (Date.now() < deadline) {
    if (await isPortServing(port)) return
    await sleep(HEALTH_POLL_MS)
  }
  throw new Error(`dotnet did not become ready on :${port} within ${READY_TIMEOUT_MS}ms`)
}

const main = async () => {
  let ownedChild: ReturnType<typeof startDotnet> | undefined
  let alreadyRunning = false

  if (await isPortServing(PORT)) {
    console.log(`→ Port ${PORT} already serving — using it, won't spawn a second dotnet.`)
    alreadyRunning = true
  } else {
    ownedChild = startDotnet()
    if (!ownedChild.pid) {
      throw new Error("dotnet child has no PID — abort.")
    }
    try {
      await waitForReady(PORT)
    } catch (err) {
      killByPid(ownedChild.pid)
      throw err
    }
  }

  try {
    console.log(`→ GET http://localhost:${PORT}/openapi/v1.json`)
    const spec = await fetchSpec(PORT)
    writeFileSync(OUTPUT, spec, "utf-8")
    const sizeKb = (spec.length / 1024).toFixed(1)
    console.log(`  wrote ${OUTPUT} (${sizeKb} KiB, ${existsSync(OUTPUT) ? "ok" : "missing!"})`)
  } finally {
    if (ownedChild?.pid && !alreadyRunning) {
      console.log(`→ Stopping dotnet (pid ${ownedChild.pid})`)
      killByPid(ownedChild.pid)
    }
  }
}

await main()

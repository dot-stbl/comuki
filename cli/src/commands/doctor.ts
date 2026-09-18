/**
 * `comuki doctor` — a non-Ink health check: host reachable, auth
 * works, config.json present, theme valid. Pure formatter plus fetch
 * glue; the process exits 1 when any check fails.
 */
import { existsSync } from "node:fs"
import { homedir } from "node:os"
import {
  ComukiApiError,
  ComukiClient,
  type ClientOptions,
  type MeView,
} from "../lib/client"
import {
  ConfigError,
  configFilePath,
  configStore,
  resolveConfig,
  type ConfigFileContents,
  type ConfigOverrides,
} from "../lib/config"
import { whoFromMe } from "../lib/auth"
import { displayPath } from "./config"
import { DEFAULT_THEME_CHOICE, isThemeChoice } from "../themes"
import { colors } from "../theme"

export type DoctorCheckName = "url" | "auth" | "config" | "theme"

export interface DoctorCheck {
  readonly name: DoctorCheckName
  readonly ok: boolean
  readonly detail: string
}

export type DoctorFetch = NonNullable<ClientOptions["fetchImpl"]>

export interface DoctorCollectInput {
  readonly overrides: ConfigOverrides
  readonly env: Record<string, string | undefined>
  readonly file: ConfigFileContents
  readonly configPath: string
  readonly configExists: boolean
  readonly fetchImpl: DoctorFetch
  readonly home?: string
}

const LABEL_WIDTH = 8
const STATUS_WIDTH = 4

/** GET `<url>/api/v1/health` — any 2xx counts as reachable. */
export async function probeDoctorHealth(
  url: string,
  fetchImpl: DoctorFetch,
  timeoutMs = 4000
): Promise<boolean> {
  try {
    const response = await fetchImpl(`${url.replace(/\/+$/, "")}/api/v1/health`, {
      signal: AbortSignal.timeout(timeoutMs),
    })
    return response.ok
  } catch {
    return false
  }
}

/** Aligned `name  ok|fail  detail` rows, one per check. */
export function formatDoctorReport(
  checks: readonly DoctorCheck[]
): string {
  return checks
    .map((check) => {
      const status = check.ok ? "ok" : "fail"
      const painted = check.ok
        ? `${colors.ok}${status}${colors.reset}`
        : `${colors.error}${status}${colors.reset}`
      const pad = " ".repeat(STATUS_WIDTH - status.length)
      return `${check.name.padEnd(LABEL_WIDTH)}${painted}${pad}  ${check.detail}`
    })
    .join("\n")
}

export function doctorFailed(checks: readonly DoctorCheck[]): boolean {
  return checks.some((check) => !check.ok)
}

/** Runs the four checks. Fetch and filesystem are injected. */
export async function collectDoctorChecks(
  input: DoctorCollectInput
): Promise<readonly DoctorCheck[]> {
  const home = input.home ?? homedir()
  const url = resolveDoctorUrl(input.overrides, input.env, input.file)
  const healthOk =
    url === undefined ? false : await probeDoctorHealth(url, input.fetchImpl)

  const urlCheck: DoctorCheck =
    url === undefined
      ? {
          name: "url",
          ok: false,
          detail: "missing — pass --url or set COMUKI_URL",
        }
      : healthOk
        ? { name: "url", ok: true, detail: url }
        : { name: "url", ok: false, detail: `${url} unreachable` }

  const authCheck = await collectAuthCheck(input, url)

  const configCheck: DoctorCheck = {
    name: "config",
    ok: input.configExists,
    detail: input.configExists
      ? displayPath(input.configPath, home)
      : `${displayPath(input.configPath, home)} (missing)`,
  }

  const themeCheck = collectThemeCheck(input.overrides, input.file)

  return [urlCheck, authCheck, configCheck, themeCheck]
}

/** `comuki doctor` entry: gather, print, return the process exit code. */
export async function printDoctor(
  overrides: ConfigOverrides
): Promise<number> {
  const file = await configStore.read()
  const configPath = configFilePath()
  const checks = await collectDoctorChecks({
    overrides,
    env: process.env,
    file,
    configPath,
    configExists: existsSync(configPath),
    fetchImpl: (input, init) => fetch(input, init),
  })
  console.log(formatDoctorReport(checks))
  return doctorFailed(checks) ? 1 : 0
}

function resolveDoctorUrl(
  overrides: ConfigOverrides,
  env: Record<string, string | undefined>,
  file: ConfigFileContents
): string | undefined {
  try {
    return resolveConfig(env, {}, overrides).url
  } catch (error) {
    if (error instanceof ConfigError) {
      const fromFile = file.url?.trim().replace(/\/+$/, "")
      return fromFile || undefined
    }
    throw error
  }
}

async function collectAuthCheck(
  input: DoctorCollectInput,
  url: string | undefined
): Promise<DoctorCheck> {
  if (url === undefined) {
    return { name: "auth", ok: false, detail: "skipped (no url)" }
  }
  try {
    const config = resolveConfig(input.env, input.file, {
      ...input.overrides,
      url,
    })
    const client = new ComukiClient(config, { fetchImpl: input.fetchImpl })
    const me: MeView = await client.me()
    const who = whoFromMe(me)
    return {
      name: "auth",
      ok: true,
      detail: `${who.kind} · ${who.label}`,
    }
  } catch (error) {
    if (error instanceof ComukiApiError) {
      return {
        name: "auth",
        ok: false,
        detail: error.status === 401 ? "unauthorized" : error.message,
      }
    }
    return {
      name: "auth",
      ok: false,
      detail: error instanceof Error ? error.message : "request failed",
    }
  }
}

function collectThemeCheck(
  overrides: ConfigOverrides,
  file: ConfigFileContents
): DoctorCheck {
  const raw = overrides.theme?.trim() || file.theme?.trim()
  if (!raw) {
    return {
      name: "theme",
      ok: true,
      detail: `${DEFAULT_THEME_CHOICE} (default)`,
    }
  }
  if (isThemeChoice(raw)) {
    return { name: "theme", ok: true, detail: raw }
  }
  return { name: "theme", ok: false, detail: `unknown: ${raw}` }
}

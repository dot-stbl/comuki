/**
 * Config resolution for the CLI: one precedence chain, one directory.
 *
 *   CLI flags (--url / --api-key / --project)
 *     > environment (COMUKI_URL / COMUKI_API_KEY / COMUKI_PROJECT)
 *       > ~/.config/comuki/config.json (cookie / tenant / api-key / project)
 *
 * The URL is REQUIRED — without an explicit arg or env, `resolveConfig`
 * throws a `ConfigError` so the user never silently hits the wrong host.
 * The config file only restores the session cookie, tenant, api-key and
 * project after `comuki login`; the host always comes from arg or env.
 *
 * `resolveConfig` is pure (env + file contents in, config out) so tests
 * cover the whole matrix without touching the filesystem.
 */
import { homedir } from "node:os"
import { join } from "node:path"
import { readJsonFile, writeJsonFile } from "./json"

export interface ConfigFileContents {
  url?: string
  apiKey?: string
  tenant?: string
  /** Session cookie captured by `comuki login` (`name=value`). */
  cookie?: string
  defaultProject?: string
  /** Terminal theme choice (`<theme>-<dark|light>`), e.g. `graphite-light`. */
  theme?: string
  /** BEL on turn completion (OSC 9 toasts are always on). */
  bell?: boolean
}

export interface ResolvedConfig {
  url: string
  apiKey?: string
  tenant?: string
  cookie?: string
  defaultProject?: string
  theme?: string
  bell: boolean
}

export interface ConfigOverrides {
  url?: string
  apiKey?: string
  project?: string
  theme?: string
}

/**
 * Thrown when `resolveConfig` cannot satisfy a required field (currently
 * only `url`). The message doubles as a usage hint — `comuki` surfaces
 * it directly without a stack trace.
 */
export class ConfigError extends Error {
  constructor(message: string) {
    super(message)
    this.name = "ConfigError"
  }
}

/** Pure precedence chain — the seam every config test drives. */
export function resolveConfig(
  env: Record<string, string | undefined> = {},
  file: ConfigFileContents = {},
  overrides: ConfigOverrides = {}
): ResolvedConfig {
  const url = overrides.url?.trim() || env.COMUKI_URL?.trim()
  if (!url) {
    throw new ConfigError(
      "missing Comuki host URL — pass --url <host> or set COMUKI_URL."
    )
  }
  const apiKey =
    overrides.apiKey?.trim() ||
    env.COMUKI_API_KEY?.trim() ||
    file.apiKey?.trim()
  const tenant = env.COMUKI_TENANT?.trim() || file.tenant?.trim()
  const defaultProject =
    overrides.project?.trim() ||
    env.COMUKI_PROJECT?.trim() ||
    file.defaultProject?.trim()
  const theme = overrides.theme?.trim() || file.theme?.trim()

  return {
    url: url.replace(/\/+$/, ""),
    apiKey: apiKey || undefined,
    tenant: tenant || undefined,
    cookie: file.cookie || undefined,
    defaultProject: defaultProject || undefined,
    theme: theme || undefined,
    // Absent = on: the bell is the point of the feature, opt-out only.
    bell: file.bell !== false,
  }
}

/**
 * `~/.config/comuki/` — the only directory the CLI keeps on disk
 * (XDG layout; `XDG_CONFIG_HOME` wins when set).
 */
export function configDir(
  xdgConfigHome: string | undefined = process.env.XDG_CONFIG_HOME
): string {
  const base = xdgConfigHome?.trim() || join(homedir(), ".config")
  return join(base, "comuki")
}

/** `~/.config/comuki/config.json` — connection + identity state. */
export function configFilePath(): string {
  return join(configDir(), "config.json")
}

/** `~/.config/comuki/sessions.json` — open tabs restored on next start. */
export function sessionsFilePath(): string {
  return join(configDir(), "sessions.json")
}

/** Reads the config file; missing or malformed → empty contents (first run). */
export async function readConfigFile(
  path: string = configFilePath()
): Promise<ConfigFileContents> {
  return (await readJsonFile<ConfigFileContents>(path)) ?? {}
}

/** Writes the config file with owner-only permissions (0o600). */
export async function writeConfigFile(
  contents: ConfigFileContents,
  path: string = configFilePath()
): Promise<void> {
  await writeJsonFile(path, contents)
}

/**
 * Config resolution for the CLI: one precedence chain, one directory.
 *
 *   CLI flags (--url/--api-key/--project)
 *     > environment (COMUKI_URL / COMUKI_API_KEY / COMUKI_TENANT)
 *       > ~/.config/comuki/config.json (written by `comuki login`)
 *         > default (http://localhost:8080)
 *
 * `resolveConfig` is pure (env + file contents in, config out) so tests
 * cover the whole matrix without touching the filesystem.
 */
import { homedir } from "node:os"
import { join } from "node:path"
import { readJsonFile, writeJsonFile } from "./json"

/** URL of the Comuki host (API + SignalR share the base). */
export const DEFAULT_URL = "http://localhost:8080"

export interface ConfigFileContents {
  url?: string
  apiKey?: string
  tenant?: string
  /** Session cookie captured by `comuki login` (`name=value`). */
  cookie?: string
  defaultProject?: string
}

export interface ResolvedConfig {
  url: string
  apiKey?: string
  tenant?: string
  cookie?: string
  defaultProject?: string
}

export interface ConfigOverrides {
  url?: string
  apiKey?: string
  project?: string
}

/** Pure precedence chain — the seam every config test drives. */
export function resolveConfig(
  env: Record<string, string | undefined> = {},
  file: ConfigFileContents = {},
  overrides: ConfigOverrides = {}
): ResolvedConfig {
  const url =
    overrides.url?.trim() ||
    env.COMUKI_URL?.trim() ||
    file.url?.trim() ||
    DEFAULT_URL
  const apiKey =
    overrides.apiKey?.trim() ||
    env.COMUKI_API_KEY?.trim() ||
    file.apiKey?.trim()
  const tenant = env.COMUKI_TENANT?.trim() || file.tenant?.trim()
  const defaultProject =
    overrides.project?.trim() ||
    env.COMUKI_PROJECT?.trim() ||
    file.defaultProject?.trim()

  return {
    url: url.replace(/\/+$/, ""),
    apiKey: apiKey || undefined,
    tenant: tenant || undefined,
    cookie: file.cookie || undefined,
    defaultProject: defaultProject || undefined,
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

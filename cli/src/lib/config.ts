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
import { DEFAULT_CONTEXT_WINDOW } from "./context"
import {
  decoded,
  invalid,
  isJsonObject,
  readJsonFile,
  writeJsonFile,
  type DecodeResult,
} from "./json"

export interface ConfigFileContents {
  readonly [key: string]: unknown
  readonly url?: string
  readonly apiKey?: string
  readonly tenant?: string
  /** Session cookie captured by `comuki login` (`name=value`). */
  readonly cookie?: string
  readonly defaultProject?: string
  /** Terminal theme choice (`<theme>-<dark|light>`), e.g. `graphite-light`. */
  readonly theme?: string
  /** BEL on turn completion (OSC 9 toasts are always on). */
  readonly bell?: boolean
  /**
   * Preferred worker-profile key (`implement`, `explore-readonly`, …).
   * Stored locally only — `createSession` has no profile field.
   */
  readonly preferredProfile?: string
  /** Context-window size for the status-line meter. Default 128000. */
  readonly contextWindow?: number
}

const CONFIG_STRING_FIELDS = [
  "url",
  "apiKey",
  "tenant",
  "cookie",
  "defaultProject",
  "theme",
  "preferredProfile",
] as const

/**
 * Decodes config JSON while retaining extension fields owned by newer CLI
 * versions. Known fields with invalid types are ignored rather than leaked.
 */
export function decodeConfigFile(value: unknown): DecodeResult<ConfigFileContents> {
  if (!isJsonObject(value)) {
    return invalid
  }

  const contents: Record<string, unknown> = { ...value }
  for (const field of CONFIG_STRING_FIELDS) {
    if (field in contents && typeof contents[field] !== "string") {
      delete contents[field]
    }
  }
  if ("bell" in contents && typeof contents.bell !== "boolean") {
    delete contents.bell
  }
  if (
    "contextWindow" in contents &&
    (typeof contents.contextWindow !== "number" ||
      !Number.isFinite(contents.contextWindow))
  ) {
    delete contents.contextWindow
  }

  return decoded(contents)
}

export interface ResolvedConfig {
  readonly url: string
  readonly apiKey?: string
  readonly tenant?: string
  readonly cookie?: string
  readonly defaultProject?: string
  readonly theme?: string
  readonly bell: boolean
  readonly preferredProfile?: string
  /** Absent → StatusLine uses DEFAULT_CONTEXT_WINDOW (128k). */
  readonly contextWindow?: number
}

export interface ConfigOverrides {
  readonly url?: string
  readonly apiKey?: string
  readonly project?: string
  readonly theme?: string
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
  const preferredProfile = file.preferredProfile?.trim()
  const contextWindow =
    typeof file.contextWindow === "number" && file.contextWindow > 0
      ? file.contextWindow
      : DEFAULT_CONTEXT_WINDOW

  return {
    url: url.replace(/\/+$/, ""),
    apiKey: apiKey || undefined,
    tenant: tenant || undefined,
    cookie: file.cookie || undefined,
    defaultProject: defaultProject || undefined,
    theme: theme || undefined,
    // Absent = on: the bell is the point of the feature, opt-out only.
    bell: file.bell !== false,
    preferredProfile: preferredProfile || undefined,
    contextWindow,
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

/** `~/.config/comuki/archive/` — `/archive` transcripts. */
export function archiveDir(
  xdgConfigHome: string | undefined = process.env.XDG_CONFIG_HOME
): string {
  return join(configDir(xdgConfigHome), "archive")
}

/** Reads the config file; missing or malformed → empty contents (first run). */
export async function readConfigFile(
  path: string = configFilePath()
): Promise<ConfigFileContents> {
  const result = decodeConfigFile(await readJsonFile(path))
  return result.ok ? result.value : {}
}

/** Writes the config file with owner-only permissions (0o600). */
export async function writeConfigFile(
  contents: ConfigFileContents,
  path: string = configFilePath()
): Promise<void> {
  await writeJsonFile(path, contents)
}

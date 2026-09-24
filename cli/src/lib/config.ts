/**
 * Config resolution for the CLI: one precedence chain, one helper.
 *
 *   CLI flags (--url / --api-key / --project)
 *     > environment (COMUKI_URL / COMUKI_API_KEY / COMUKI_PROJECT)
 *       > ~/.config/comuki/config.json (url / cookie / tenant / api-key / project)
 *         > http://localhost:8080
 *
 * Every call site goes through `resolveConfig` (interactive TUI, one-shot,
 * JSON commands, doctor, config show) so the precedence is identical
 * everywhere; `comuki setup` and `comuki login` write `url` into the
 * config file, which is then honored by `comuki` on a bare invocation.
 *
 * A non-blank URL from any source that does not parse as an `http(s)`
 * URL throws `ConfigError` naming the source — invalid persisted URLs
 * are a misconfiguration, not a silent fallback. The REPL, one-shot
 * mode and `comuki status` / `runs` / `whoami` all rely on `resolveConfig`
 * returning a usable URL, so a bad persisted value cannot reach them.
 *
 * `resolveConfig` is pure (env + file contents in, config out) so tests
 * cover the whole matrix without touching the filesystem.
 */
import { join } from "node:path"
import { DEFAULT_CONTEXT_WINDOW } from "./context"
import {
  configDir,
  configFilePath,
  configStore,
  decodeConfigFile,
  JsonConfigStore,
  type ConfigFileContents,
  type ConfigStore,
} from "../persistence/config-store"

export {
  configDir,
  configFilePath,
  configStore,
  decodeConfigFile,
  JsonConfigStore,
  type ConfigFileContents,
  type ConfigStore,
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
 * Thrown when `resolveConfig` rejects a value (an invalid URL from any
 * source). The message doubles as a usage hint — `comuki` surfaces it
 * directly without a stack trace.
 */
export class ConfigError extends Error {
  constructor(message: string) {
    super(message)
    this.name = "ConfigError"
  }
}

/** Local dev fallback — the bottom of the precedence chain. */
export const DEFAULT_URL = "http://localhost:8080"

/**
 * `http://host[:port]` or `https://…` — parseable with a non-empty host.
 * Anything else (missing scheme, blank after trim, garbage) is invalid
 * and resolves to `false`.
 */
export function isValidHttpUrl(value: string): boolean {
  const trimmed = value.trim()
  if (!/^https?:\/\//.test(trimmed)) {
    return false
  }
  try {
    return new URL(trimmed).hostname.length > 0
  } catch {
    return false
  }
}

/** Which precedence slot supplied the url (`default` = localhost fallback). */
export type UrlSource = "arg" | "env" | "file" | "default"

/**
 * Internal url picker for `resolveConfig`: walks arg > env > file >
 * `DEFAULT_URL`, validates every non-blank candidate as an http(s) URL,
 * and throws `ConfigError` naming the offending source. Returns the
 * normalized url (trailing slashes trimmed) plus the source tag for
 * diagnostics — callers that want to render the source use
 * `commands/config.ts`'s `resolveUrlDisplay` (which already exists and
 * returns `none` instead of `default` for display parity).
 */
export function resolveUrl(
  env: Record<string, string | undefined>,
  file: ConfigFileContents,
  overrides: ConfigOverrides
): { url: string; source: UrlSource } {
  const arg = overrides.url?.trim()
  if (arg) {
    if (!isValidHttpUrl(arg)) {
      throw new ConfigError(
        `invalid --url: ${JSON.stringify(arg)} (expected http://host or https://host)`
      )
    }
    return { url: arg, source: "arg" }
  }
  const fromEnv = env.COMUKI_URL?.trim()
  if (fromEnv) {
    if (!isValidHttpUrl(fromEnv)) {
      throw new ConfigError(
        `invalid COMUKI_URL: ${JSON.stringify(fromEnv)} (expected http://host or https://host)`
      )
    }
    return { url: fromEnv, source: "env" }
  }
  const fromFile = file.url?.trim()
  if (fromFile) {
    if (!isValidHttpUrl(fromFile)) {
      throw new ConfigError(
        `invalid url in config.json: ${JSON.stringify(fromFile)} — fix ~/.config/comuki/config.json or pass --url`
      )
    }
    return { url: fromFile, source: "file" }
  }
  return { url: DEFAULT_URL, source: "default" }
}

/** Pure precedence chain — the seam every config test drives. */
export function resolveConfig(
  env: Record<string, string | undefined> = {},
  file: ConfigFileContents = {},
  overrides: ConfigOverrides = {}
): ResolvedConfig {
  const { url } = resolveUrl(env, file, overrides)
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
/** `~/.config/comuki/sessions.json` — open tabs restored on next start. */
export function sessionsFilePath(): string {
  return join(configDir(), "sessions.json")
}

/** `~/.config/comuki/archive/` — archived session transcripts. */
export function archiveDir(
  xdgConfigHome: string | undefined = process.env.XDG_CONFIG_HOME
): string {
  return join(configDir(xdgConfigHome), "archive")
}

/** Reads the config file; missing or malformed → empty contents (first run). */
export async function readConfigFile(
  path: string = configFilePath()
): Promise<ConfigFileContents> {
  return path === configFilePath()
    ? configStore.read()
    : new JsonConfigStore(path).read()
}

/** Writes the config file with owner-only permissions (0o600). */
export async function writeConfigFile(
  contents: ConfigFileContents,
  path: string = configFilePath()
): Promise<void> {
  const store = path === configFilePath() ? configStore : new JsonConfigStore(path)
  await store.update((current) => ({ ...current, ...contents }))
}

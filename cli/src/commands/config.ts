/**
 * `comuki config [show]` — the resolved-configuration dump.
 *
 * Plain `console.log` key-value lines (no Ink, no host calls): the url
 * with the precedence source that won, masked credentials, the default
 * project, and the on-disk file paths with existence. Secrets never
 * print in full — the api key shows its first 8 chars, the cookie only
 * its name. Unlike `resolveConfig`, this view is display-only and never
 * throws: a missing url reports `(source: none)`, and the config file
 * counts as a url source here even though only arg/env can boot the
 * client.
 */
import { existsSync } from "node:fs"
import { homedir } from "node:os"
import {
  configFilePath,
  configStore,
  sessionsFilePath,
  type ConfigFileContents,
  type ConfigOverrides,
} from "../lib/config"

/** Which precedence slot supplied the url (`none` = unset everywhere). */
export type UrlSource = "arg" | "env" | "file" | "none"

export interface ConfigShowInput {
  readonly overrides: ConfigOverrides
  readonly env: Record<string, string | undefined>
  readonly file: ConfigFileContents
  readonly configPath: string
  readonly configExists: boolean
  readonly sessionsPath: string
  readonly sessionsExists: boolean
}

/** Label column width (`sessions` + 2) — keeps the value column aligned. */
const LABEL_WIDTH = 10

function row(label: string, value: string): string {
  return `${label.padEnd(LABEL_WIDTH)} ${value}`
}

/**
 * Display-only url precedence: arg > env > file, `none` when unset.
 * Mirrors `resolveConfig` but adds the file fallback and never throws.
 */
export function resolveUrlDisplay(
  overrides: ConfigOverrides,
  env: Record<string, string | undefined>,
  file: ConfigFileContents
): { url: string | undefined; source: UrlSource } {
  const fromArg = overrides.url?.trim()
  if (fromArg) {
    return { url: fromArg, source: "arg" }
  }
  const fromEnv = env.COMUKI_URL?.trim()
  if (fromEnv) {
    return { url: fromEnv, source: "env" }
  }
  const fromFile = file.url?.trim()
  if (fromFile) {
    return { url: fromFile, source: "file" }
  }
  return { url: undefined, source: "none" }
}

/** `ck_z6bc4…` — first 8 chars only, never the full key. */
export function maskApiKey(apiKey: string): string {
  return `${apiKey.slice(0, 8)}…`
}

/** `comuki.auth=***` — the cookie name only, never the value. */
export function maskCookie(cookie: string): string {
  const name = cookie.split("=")[0].trim()
  return name ? `${name}=***` : "***"
}

/** Folds a leading home directory prefix to `~` for display. */
export function displayPath(path: string, home: string): string {
  return home !== "" && path.startsWith(home)
    ? `~${path.slice(home.length)}`
    : path
}

function authRow(
  overrides: ConfigOverrides,
  env: Record<string, string | undefined>,
  file: ConfigFileContents
): string {
  const apiKey =
    overrides.apiKey?.trim() ||
    env.COMUKI_API_KEY?.trim() ||
    file.apiKey?.trim()
  if (apiKey) {
    return row("auth", `api-key ${maskApiKey(apiKey)}`)
  }
  const cookie = file.cookie?.trim()
  if (cookie) {
    return row("auth", `cookie ${maskCookie(cookie)}`)
  }
  return row("auth", "anonymous")
}

/**
 * The whole `config show` output as one string (trailing newline is
 * the caller's `console.log`). `home` is injectable so tests get a
 * stable `~` prefix on every platform.
 */
export function formatConfigShow(
  input: ConfigShowInput,
  home: string = homedir()
): string {
  const { overrides, env, file } = input
  const url = resolveUrlDisplay(overrides, env, file)
  const project =
    overrides.project?.trim() ||
    env.COMUKI_PROJECT?.trim() ||
    file.defaultProject?.trim()
  const rows = [
    row("url", `${url.url ?? "—"} (source: ${url.source})`),
    authRow(overrides, env, file),
    row("project", project ?? "—"),
    row(
      "config",
      `${displayPath(input.configPath, home)} (${input.configExists ? "exists" : "missing"})`
    ),
    row(
      "sessions",
      `${displayPath(input.sessionsPath, home)} (${input.sessionsExists ? "exists" : "missing"})`
    ),
  ]
  return rows.join("\n")
}

/** `comuki config [show]` entry: gathers on-disk state and prints it. */
export async function printConfigShow(
  overrides: ConfigOverrides
): Promise<void> {
  const file = await configStore.read()
  const configPath = configFilePath()
  const sessionsPath = sessionsFilePath()
  console.log(
    formatConfigShow({
      overrides,
      env: process.env,
      file,
      configPath,
      configExists: existsSync(configPath),
      sessionsPath,
      sessionsExists: existsSync(sessionsPath),
    })
  )
}

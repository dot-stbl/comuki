/**
 * Prompt aliases (`/alias`): named expansions persisted in
 * `~/.config/comuki/aliases.json`. On submit, if the entire prompt is
 * an alias name (or `/name` that does not collide with a slash
 * command), it expands to the stored text before send. Slash commands
 * always win.
 *
 * Shape is a flat `{ "fix": "please fix the failing tests" }` map —
 * names `[a-z][a-z0-9-]*`. Invalid name / blank text leave the store
 * unchanged (mirrors `saveSnippet`).
 */
import { join } from "node:path"
import { readJsonFile, writeJsonFile } from "./json"
import { configDir } from "./config"
import { isRegisteredSlashName } from "./slash"
import { colors } from "../theme"

/** Alias names: a letter, then lowercase letters / digits / dashes. */
export const ALIAS_NAME_PATTERN = /^[a-z][a-z0-9-]*$/

/** The persisted file shape — a flat name → text map. */
export type AliasStore = Readonly<Record<string, string>>

const EMPTY: AliasStore = {}

/** `~/.config/comuki/aliases.json`. */
export function aliasesFilePath(): string {
  return join(configDir(), "aliases.json")
}

export function isValidAliasName(name: string): boolean {
  return ALIAS_NAME_PATTERN.test(name)
}

/** Re-export so callers don't have to reach into slash.ts for precedence. */
export { isRegisteredSlashName }

/**
 * Stores `text` under `name` (overwriting a previous alias); an
 * invalid name or blank text leaves the store unchanged.
 */
export function setAlias(
  store: AliasStore,
  name: string,
  text: string
): AliasStore {
  const trimmed = text.trim()
  if (!isValidAliasName(name) || trimmed.length === 0) {
    return store
  }
  return { ...store, [name]: trimmed }
}

/** The alias stored under `name`, or undefined when absent. */
export function getAlias(store: AliasStore, name: string): string | undefined {
  return store[name]
}

/** Removes the alias under `name`; an unknown name → unchanged. */
export function removeAlias(store: AliasStore, name: string): AliasStore {
  if (store[name] === undefined) {
    return store
  }
  const next = { ...store }
  delete next[name]
  return next
}

/** All alias names, alphabetically — the `/alias` listing order. */
export function aliasNames(store: AliasStore): readonly string[] {
  return Object.keys(store).sort()
}

/** One-line preview of an alias: first line, collapsed, ≤48 chars. */
export function aliasPreview(text: string): string {
  const collapsed = text.replace(/\s+/g, " ").trim()
  return collapsed.length <= 48 ? collapsed : `${collapsed.slice(0, 48)}…`
}

/** The `/alias` transcript block — header row + one row per alias. */
export function aliasListingLines(store: AliasStore): readonly string[] {
  const names = aliasNames(store)
  if (names.length === 0) {
    return [
      `${colors.faint}  no aliases — /alias set <name> <text>${colors.reset}`,
    ]
  }
  const width = Math.max(...names.map((name) => name.length))
  return [
    `${colors.accent}aliases${colors.reset}`,
    ...names.map(
      (name) =>
        `  ${name.padEnd(width + 3)}${colors.faint}${aliasPreview(store[name] ?? "")}${colors.reset}`
    ),
  ]
}

/**
 * If the entire prompt is an alias name (bare `fix` or `/fix` that is
 * not a slash command), return the stored text. Otherwise null — the
 * caller sends the original input.
 *
 * Slash commands always win: a registered `/retry` never expands even
 * if the store has a `retry` key.
 */
export function expandAlias(
  store: AliasStore,
  raw: string
): string | null {
  const trimmed = raw.trim()
  if (trimmed.length === 0) {
    return null
  }
  const candidate = trimmed.startsWith("/") ? trimmed.slice(1) : trimmed
  if (!isValidAliasName(candidate) || isRegisteredSlashName(candidate)) {
    return null
  }
  const text = store[candidate]
  return text === undefined ? null : text
}

/** Reads aliases.json; missing, malformed or non-object → empty store. */
export async function readAliasesFile(
  path: string = aliasesFilePath()
): Promise<AliasStore> {
  const contents = await readJsonFile<unknown>(path)
  if (!contents || typeof contents !== "object" || Array.isArray(contents)) {
    return EMPTY
  }
  const store: Record<string, string> = {}
  for (const [name, text] of Object.entries(contents as Record<string, unknown>)) {
    if (typeof text === "string") {
      store[name] = text
    }
  }
  return store
}

/** Writes aliases.json with owner-only permissions (0o600). */
export async function writeAliasesFile(
  store: AliasStore,
  path: string = aliasesFilePath()
): Promise<void> {
  await writeJsonFile(path, store)
}

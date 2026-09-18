/**
 * Saved prompts (`/snip`): named reusable messages persisted in
 * `~/.config/comuki/snippets.json`. CRUD is pure over a plain record
 * (invalid name / blank text → store unchanged, mirroring
 * `renameSession`); `chat.tsx` pre-checks for user-facing messaging.
 *
 * `/snip <name>` SENDS the snippet as the next message — the prompt
 * editor's draft is internal state with no prefill seam, so
 * send-as-message is the honest v1 (documented in `/help`).
 */
import { join } from "node:path"
import { isJsonObject, readJsonFile, writeJsonFile } from "./json"
import { configDir } from "./config"
import { colors } from "../theme"

/** Snippet names are lowercase kebab-ish: `[a-z0-9-]+`. */
export const SNIPPET_NAME_PATTERN = /^[a-z0-9-]+$/

/** The persisted file shape — a wrapper keeps room for metadata later. */
export interface PersistedSnippets {
  readonly snippets: Readonly<Record<string, string>>
}

/** `~/.config/comuki/snippets.json`. */
export function snippetsFilePath(): string {
  return join(configDir(), "snippets.json")
}

export function isValidSnippetName(name: string): boolean {
  return SNIPPET_NAME_PATTERN.test(name)
}

/**
 * Stores `text` under `name` (overwriting a previous snippet); an
 * invalid name or blank text leaves the store unchanged.
 */
export function saveSnippet(
  store: PersistedSnippets,
  name: string,
  text: string
): PersistedSnippets {
  const trimmed = text.trim()
  if (!isValidSnippetName(name) || trimmed.length === 0) {
    return store
  }
  return { snippets: { ...store.snippets, [name]: trimmed } }
}

/** The snippet stored under `name`, or undefined when absent. */
export function getSnippet(
  store: PersistedSnippets,
  name: string
): string | undefined {
  return store.snippets[name]
}

/** Removes the snippet under `name`; an unknown name → unchanged. */
export function removeSnippet(
  store: PersistedSnippets,
  name: string
): PersistedSnippets {
  if (store.snippets[name] === undefined) {
    return store
  }
  const snippets = { ...store.snippets }
  delete snippets[name]
  return { snippets }
}

/** All snippet names, alphabetically — the `/snip` listing order. */
export function snippetNames(store: PersistedSnippets): readonly string[] {
  return Object.keys(store.snippets).sort()
}

/** One-line preview of a snippet: first line, collapsed, ≤48 chars. */
export function snippetPreview(text: string): string {
  const collapsed = text.replace(/\s+/g, " ").trim()
  return collapsed.length <= 48 ? collapsed : `${collapsed.slice(0, 48)}…`
}

/** The `/snip` transcript block — header row + one row per snippet. */
export function snippetListingLines(
  store: PersistedSnippets
): readonly string[] {
  const names = snippetNames(store)
  if (names.length === 0) {
    return [
      `${colors.faint}  no saved snippets — send a message, then /snip save <name>${colors.reset}`,
    ]
  }
  const width = Math.max(...names.map((name) => name.length))
  return [
    `${colors.accent}snippets${colors.reset}`,
    ...names.map(
      (name) =>
        `  ${name.padEnd(width + 3)}${colors.faint}${snippetPreview(store.snippets[name] ?? "")}${colors.reset}`
    ),
  ]
}

/** Reads snippets.json; missing, malformed or foreign shape → empty store. */
export async function readSnippetsFile(
  path: string = snippetsFilePath()
): Promise<PersistedSnippets> {
  const contents = await readJsonFile(path)
  if (!isJsonObject(contents) || !isJsonObject(contents.snippets)) {
    return { snippets: {} }
  }
  const snippets: Record<string, string> = {}
  for (const [name, text] of Object.entries(contents.snippets)) {
    if (typeof text === "string") {
      snippets[name] = text
    }
  }
  return { snippets }
}

/** Writes snippets.json with owner-only permissions (0o600). */
export async function writeSnippetsFile(
  store: PersistedSnippets,
  path: string = snippetsFilePath()
): Promise<void> {
  await writeJsonFile(path, store)
}

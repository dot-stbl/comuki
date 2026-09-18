/**
 * Optional overlay over the REPL's default chords.
 *
 * `~/.config/comuki/keybindings.json` is a flat `{ action: chord }`
 * map. Known actions merge over the defaults; unknown keys and
 * malformed values are ignored. Chords are `ctrl+<letter>` or `esc`.
 *
 * Chat wires every default through `matchesBinding` on the shell's
 * single `useInput` (copy rides `useCopyLastAnswer` with the same
 * resolved chord).
 */
import { join } from "node:path"
import { readJsonFile } from "./json"
import { configDir } from "./config"
import { colors } from "../theme"

export type KeybindingAction =
  | "new"
  | "close"
  | "verbose"
  | "copy"
  | "search"
  | "overview"

export type Keybindings = Readonly<Record<KeybindingAction, string>>

export const DEFAULT_KEYBINDINGS: Keybindings = {
  new: "ctrl+n",
  close: "ctrl+w",
  verbose: "ctrl+o",
  copy: "ctrl+y",
  search: "ctrl+f",
  overview: "esc",
}

const KEYBINDING_LABELS: Readonly<Record<KeybindingAction, string>> = {
  new: "new session",
  close: "close session",
  verbose: "toggle verbose",
  copy: "copy last answer",
  search: "search transcript",
  overview: "session overview",
}

const KEYBINDING_ACTIONS: readonly KeybindingAction[] = [
  "new",
  "close",
  "verbose",
  "copy",
  "search",
  "overview",
]

/** `ctrl+a` … `ctrl+z`, or `esc`. Anything else stays on the default. */
const CHORD_PATTERN = /^(ctrl\+[a-z]|esc)$/

export interface BindingKey {
  readonly ctrl: boolean
  readonly escape: boolean
}

/** `~/.config/comuki/keybindings.json`. */
export function keybindingsFilePath(): string {
  return join(configDir(), "keybindings.json")
}

/**
 * Merges `file` over `defaults`. Unknown keys, non-strings, and chords
 * that are not `ctrl+<letter>` / `esc` are dropped.
 */
export function resolveKeybindings(
  file: unknown,
  defaults: Keybindings = DEFAULT_KEYBINDINGS
): Keybindings {
  const result: Record<KeybindingAction, string> = { ...defaults }
  if (file === null || file === undefined || typeof file !== "object") {
    return result
  }
  for (const [key, value] of Object.entries(file as Record<string, unknown>)) {
    if (!isKeybindingAction(key) || typeof value !== "string") {
      continue
    }
    const chord = normalizeChord(value)
    if (chord !== undefined) {
      result[key] = chord
    }
  }
  return result
}

/** True when this keystroke is the chord `binding` names. */
export function matchesBinding(
  binding: string,
  input: string,
  key: BindingKey
): boolean {
  const chord = normalizeChord(binding)
  if (chord === undefined) {
    return false
  }
  if (chord === "esc") {
    return key.escape
  }
  const letter = chord.slice("ctrl+".length)
  return key.ctrl && input.toLowerCase() === letter
}

/** `/keys` transcript block — current chords, registry order. */
export function keybindingsListingLines(
  bindings: Keybindings
): readonly string[] {
  const width = Math.max(
    ...KEYBINDING_ACTIONS.map((action) => bindings[action].length)
  )
  return [
    `${colors.accent}keys${colors.reset}`,
    ...KEYBINDING_ACTIONS.map(
      (action) =>
        `  ${bindings[action].padEnd(width + 3)}${KEYBINDING_LABELS[action]}`
    ),
  ]
}

/** Missing or malformed file → defaults. */
export async function readKeybindingsFile(
  path: string = keybindingsFilePath()
): Promise<Keybindings> {
  return resolveKeybindings(await readJsonFile<unknown>(path))
}

function isKeybindingAction(value: string): value is KeybindingAction {
  return (KEYBINDING_ACTIONS as readonly string[]).includes(value)
}

function normalizeChord(value: string): string | undefined {
  const trimmed = value.trim().toLowerCase()
  const chord = trimmed === "escape" ? "esc" : trimmed
  return CHORD_PATTERN.test(chord) ? chord : undefined
}

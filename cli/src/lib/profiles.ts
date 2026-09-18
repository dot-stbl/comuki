/**
 * Worker-profile listing for `/profile`. The host exposes
 * `GET /profiles` (`plan:read`); `createSession` has no profile field,
 * so a selected name is stored in `config.json` as a preference only —
 * it does not ride the next turn.
 *
 * When the host is unreachable the listing falls back to the four
 * well-known control-plane stems so `/profile` still has something
 * honest to show.
 */
import { colors, paint, symbols } from "../theme"
import type { ProfileView } from "./client"

/** Control-plane stems under `control-plane/profiles/*.md`. */
export const WELL_KNOWN_PROFILES = [
  "implement",
  "explore-readonly",
  "docs-writer",
  "pr-review",
] as const

export type WellKnownProfile = (typeof WELL_KNOWN_PROFILES)[number]

export function isWellKnownProfile(name: string): boolean {
  return (WELL_KNOWN_PROFILES as readonly string[]).includes(name)
}

/**
 * Resolves a typed name against a fetched catalog (key, then name).
 * Catalog miss → well-known stem match. Empty query → undefined.
 */
export function resolveProfile(
  query: string,
  profiles: readonly ProfileView[]
): string | undefined {
  const wanted = query.trim().toLowerCase()
  if (wanted.length === 0) {
    return undefined
  }
  const fromCatalog =
    profiles.find((profile) => profile.key.toLowerCase() === wanted) ??
    profiles.find((profile) => profile.name.toLowerCase() === wanted)
  if (fromCatalog) {
    return fromCatalog.key
  }
  return isWellKnownProfile(wanted) ? wanted : undefined
}

/** `/profile` without args: current preference + catalog (or well-known). */
export function profileListingLines(
  current: string | null,
  profiles: readonly ProfileView[],
  options: { readonly fromHost: boolean } = { fromHost: true }
): string[] {
  const header = `  ${paint(symbols.event, colors.dim)} ${paint("profile", colors.muted)} ${paint(
    `· preferred: ${current ?? "none"}`,
    colors.dim
  )}`
  const names =
    profiles.length > 0
      ? profiles.map((profile) =>
          profile.description.length > 0
            ? `${profile.key} — ${profile.description}`
            : profile.key
        )
      : [...WELL_KNOWN_PROFILES]
  const sourceNote = options.fromHost
    ? []
    : [
        `  ${paint("host catalog unavailable — well-known names", colors.faint)}`,
      ]
  const honest = [
    `  ${paint("createSession has no profile field — stored locally only", colors.faint)}`,
  ]
  return [
    header,
    ...sourceNote,
    ...names.map(
      (name) => `  ${paint(`${symbols.bullet} ${name}`, colors.faint)}`
    ),
    ...honest,
  ]
}

/** The preference-switch notice: `* profile → {key}`. */
export function profileStoredLine(key: string): string {
  return `  ${paint(`${symbols.event} profile ${symbols.arrow} ${key} (local preference — sessions ignore it)`, colors.faint)}`
}

/**
 * The mark a project answers to, resolved — the display half of the identity
 * fields' contract.
 *
 * The backend stores only explicit overrides (an opaque `icon` string); the
 * *default* is derived here, on render, from the profiles repository's host.
 * Deriving at write time would freeze the mark when a repo moves and couple
 * the Projects module to provider knowledge it does not need, so the rule
 * lives in this one pure helper instead (design D3).
 */
import { i18n } from "@/shared/i18n"

/** What the identity cell draws, in resolution order. */
export type ProjectMark =
  | { kind: "stored"; value: string }
  | { kind: "brand"; brand: "github" | "gitlab" | "git" }
  | { kind: "neutral" }

/** The two fields the resolution reads — both `ProjectRow` and a seed carry them. */
interface MarkSource {
  icon: string | null
  /** The domain spelling of the wire's `profilesGitUrl`. */
  gitProfileRepo: string | null
}

/**
 * The host out of a git URL, in either shape the platform accepts.
 *
 * `git@github.com:org/repo.git` (scp-like) and `https://github.com/org/repo`
 * both answer `github.com`; anything that is not one of those two shapes —
 * an empty string, a bare path, a URL with a port but no host — answers
 * `null`, which the mark resolution reads as "no repository to derive from"
 * rather than as a parsing failure worth a screen for.
 */
export function gitHost(url: string | null): string | null {
  if (!url) {
    return null
  }

  // scp-like: everything between the leading `user@` and the first `:`.
  const scp = /^git@([^:/]+)[:/]/.exec(url)
  if (scp) {
    return scp[1]?.toLowerCase() ?? null
  }

  // https-like: the authority of an absolute URL.
  try {
    const parsed = new URL(url)
    return parsed.host ? parsed.host.toLowerCase() : null
  } catch {
    return null
  }
}

function brandForHost(host: string): "github" | "gitlab" | "git" {
  if (host === "github.com" || host.endsWith(".github.com")) {
    return "github"
  }
  // `gitlab*` covers gitlab.com and self-hosted gitlab.example.com — the host
  // names the tanuki either way, and the mark is monochrome in this product
  // regardless (design D3).
  if (host === "gitlab.com" || host.startsWith("gitlab.")) {
    return "gitlab"
  }
  return "git"
}

/**
 * The mark, in the order the spec states: a stored icon wins over derivation,
 * a derivable host wins over the neutral glyph, and a project with neither is
 * still a project — it gets the neutral mark, not a blank cell.
 */
export function resolveProjectMark(project: MarkSource): ProjectMark {
  // An icon that is only whitespace renders as nothing; the tolerant read
  // treats it the same as absent and falls through to derivation.
  const stored = project.icon?.trim()
  if (stored) {
    return { kind: "stored", value: stored }
  }

  const host = gitHost(project.gitProfileRepo)
  if (host) {
    return { kind: "brand", brand: brandForHost(host) }
  }

  return { kind: "neutral" }
}

/**
 * Whether a stored icon renders as an image or as text.
 *
 * The server validates length only and treats the string as opaque (design
 * D2), so the one rule that decides is client-side: a URL scheme at the start
 * means `<img>`, anything else — an emoji, a letter, a word — means text.
 * The heuristic is total: every string renders as one or the other.
 */
export function isImageIcon(value: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(value)
}

/**
 * The mark said in words, for the surfaces that name what they show (the
 * detail page's facts). The drawn glyph is decoration beside these words —
 * the same two-channel rule every mark in this product follows.
 *
 * The words live in the projects locale catalogues (`mark.<kind>`); the brand
 * segment (`github`, `gitlab`, `git`) is a value out of a closed set and
 * interpolates into the sentence rather than being translated itself.
 */
export function markLabel(mark: ProjectMark): string {
  if (mark.kind === "stored") {
    return i18n.t("projects:mark.stored")
  }
  if (mark.kind === "neutral") {
    return i18n.t("projects:mark.neutral")
  }
  return i18n.t("projects:mark.brand", { brand: mark.brand })
}

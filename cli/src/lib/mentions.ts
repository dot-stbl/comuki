/**
 * @-mentions — referencing knowledge documents in the chat prompt.
 *
 * Syntax: `@query ` (at-sign at word start, non-space chars, terminated
 * by a space or submit). On submit the caller expands each mention via
 * the knowledge search API and appends the hits as a context preamble
 * the brain receives on the wire:
 *
 *     [@knowledge: identity module — trimmed chunk text]
 *
 * The user's local echo never shows the preamble — `renderUserEcho`
 * strips `[@knowledge: …]` lines and re-accents the `@tokens` instead.
 * When a mention resolves to nothing it rides the wire as plain text
 * with a dim notice; when the knowledge API refuses the session (401/
 * 403/404), the feature flags itself off after the first failure.
 *
 * Pragmatic v1: resolution is client-side (no server changes), memory
 * facts are MCP-only (`memory.recall` is gated to workers with an
 * active work item) — mentions resolve against knowledge alone.
 *
 * Everything here is pure (the only I/O is the injected `search`
 * seam), so the full grammar is testable without Ink or a host.
 */
import { colors, paint, symbols } from "../theme"

/**
 * One knowledge search hit as this layer consumes it — the shape
 * `ComukiClient.knowledgeSearch` returns (`KnowledgeSearchHitView`).
 */
export interface MentionHit {
  readonly documentId: string
  readonly snippet: string
  readonly score: number
}

/** Search seam — `ComukiClient.knowledgeSearch` satisfies it. */
export type MentionSearch = (query: string) => Promise<readonly MentionHit[]>

/** Word-start `@token` — a leading `^`-anchor or whitespace, then the query. */
export const MENTION_TOKEN = /(?:^|\s)@([^\s@]+)/g

/** Preamble block line as it rides the wire (and as echo strips it). */
const PREAMBLE_LINE = /^\[@knowledge: .*\]$/

/** Trimmed content budget per resolved hit. */
export const SNIPPET_MAX_CHARS = 800

/** Hits that ride the preamble per mention (top-3 by score). */
export const MAX_HITS_PER_MENTION = 3

// ---------------------------------------------------------------------------
// Extraction
// ---------------------------------------------------------------------------

/**
 * Unique mention queries in order of appearance. `a@b` (mid-word `@`,
 * an email) is not a mention — the `@` must sit at a word start.
 */
export function extractMentions(text: string): string[] {
  const queries: string[] = []
  for (const match of text.matchAll(MENTION_TOKEN)) {
    const query = match[1] ?? ""
    if (query.length > 0 && !queries.includes(query)) {
      queries.push(query)
    }
  }
  return queries
}

/**
 * The mention token currently being typed, when the draft ends in one:
 * `see @ident` → `"ident"`. Drives the autocomplete popup — opens only
 * at a word start and only from two query characters on.
 */
export function activeMentionQuery(draft: string): string | null {
  const match = /(?:^|\s)@([^\s@]{2,})$/.exec(draft)
  return match === null ? null : (match[1] ?? null)
}

// ---------------------------------------------------------------------------
// Labels + slugs
// ---------------------------------------------------------------------------

/** Optional document-id → title lookup (the documents listing page). */
export type TitleLookup = (documentId: string) => string | undefined

/**
 * What a hit is called in notices, the preamble and the menu: the
 * document title when known, else the snippet's first line trimmed to
 * a label-sized prefix.
 */
export function mentionLabel(
  hit: MentionHit,
  titleFor?: TitleLookup
): string {
  const title = titleFor?.(hit.documentId)?.trim()
  if (title !== undefined && title.length > 0) {
    return title
  }
  const line = (hit.snippet.split("\n", 1)[0] ?? "").replace(/\s+/g, " ").trim()
  return line.length <= 48 ? line : `${line.slice(0, 47).trimEnd()}…`
}

/**
 * `Identity Module — v2` → `identity-module-v2`: what accept inserts
 * after the `@`. Lowercase, runs of non-letters/digits collapse to one
 * dash, edge dashes drop; cyrillic titles keep their letters.
 */
export function slugForMention(label: string): string {
  const slug = label
    .toLowerCase()
    .replace(/[^a-z0-9а-яё]+/gi, "-")
    .replace(/^-+|-+$/g, "")
  return slug.length > 0 ? slug : "mention"
}

// ---------------------------------------------------------------------------
// Expansion — typed text → outgoing wire message
// ---------------------------------------------------------------------------

/** One mention's resolved hits (top by score, already capped). */
export interface MentionResolution {
  readonly query: string
  readonly hits: readonly MentionHit[]
}

/** What `expandMentions` decided about one submitted line. */
export interface MentionExpansion {
  /** The text as typed (preamble-stripped when the input carried one). */
  readonly typed: string
  /** What goes on the wire — typed text plus the preamble blocks. */
  readonly outgoing: string
  readonly resolutions: readonly MentionResolution[]
  /** true when the search refused — the feature is off for the session. */
  readonly knowledgeUnavailable: boolean
}

/**
 * Expands a submitted line: every unique mention is searched and its
 * top hits are appended as `[@knowledge: label — trimmed text]` blocks.
 * The first search failure returns `knowledgeUnavailable` with the
 * outgoing text unchanged — the caller flags the feature off, not
 * blocks the send.
 */
export async function expandMentions(
  text: string,
  search: MentionSearch
): Promise<MentionExpansion> {
  const queries = extractMentions(text)
  if (queries.length === 0) {
    return { typed: text, outgoing: text, resolutions: [], knowledgeUnavailable: false }
  }
  const resolutions: MentionResolution[] = []
  for (const query of queries) {
    try {
      const hits = await search(query)
      resolutions.push({ query, hits: hits.slice(0, MAX_HITS_PER_MENTION) })
    } catch {
      return {
        typed: text,
        outgoing: text,
        resolutions: [],
        knowledgeUnavailable: true,
      }
    }
  }
  const blocks = preambleBlocks(resolutions)
  const outgoing =
    blocks.length > 0 ? `${text}\n\n${blocks.join("\n")}` : text
  return { typed: text, outgoing, resolutions, knowledgeUnavailable: false }
}

/** The `[@knowledge: …]` lines for every resolved hit, mentions in order. */
export function preambleBlocks(
  resolutions: readonly MentionResolution[],
  titleFor?: TitleLookup
): string[] {
  const blocks: string[] = []
  for (const resolution of resolutions) {
    for (const hit of resolution.hits) {
      blocks.push(
        `[@knowledge: ${mentionLabel(hit, titleFor)} — ${trimSnippet(hit.snippet)}]`
      )
    }
  }
  return blocks
}

/**
 * Removes preamble blocks from a stored message — the echo (and the
 * `/retry` re-expansion) work from the user's own words only.
 */
export function stripMentionPreamble(text: string): string {
  const lines = text.split("\n").filter((line) => !PREAMBLE_LINE.test(line))
  while (lines.length > 0 && lines[lines.length - 1]?.trim() === "") {
    lines.pop()
  }
  return lines.join("\n")
}

/** Collapses whitespace and caps at `maxChars` (ellipsis included). */
export function trimSnippet(text: string, maxChars = SNIPPET_MAX_CHARS): string {
  const flat = text.replace(/\s+/g, " ").trim()
  return flat.length <= maxChars
    ? flat
    : `${flat.slice(0, maxChars - 1).trimEnd()}…`
}

// ---------------------------------------------------------------------------
// Notices
// ---------------------------------------------------------------------------

/**
 * The dim `*` lines after a send: nothing found (sent as plain text),
 * ambiguous (top hit's title), or the knowledge surface refusing. A
 * mention with exactly one hit stays silent.
 */
export function mentionNoticeLines(
  expansion: MentionExpansion,
  titleFor?: TitleLookup
): string[] {
  if (expansion.knowledgeUnavailable) {
    return [mentionNotice("knowledge api unavailable — mentions sent as plain text")]
  }
  const lines: string[] = []
  for (const resolution of expansion.resolutions) {
    if (resolution.hits.length === 0) {
      lines.push(
        mentionNotice(`@${resolution.query} — nothing found, sent as plain text`)
      )
    } else if (resolution.hits.length > 1) {
      const top = resolution.hits[0]
      if (top) {
        lines.push(
          mentionNotice(`@${resolution.query} ← ${mentionLabel(top, titleFor)}`)
        )
      }
    }
  }
  return lines
}

function mentionNotice(text: string): string {
  return `  ${paint(symbols.event, colors.faint)} ${paint(text, colors.faint)}`
}

// ---------------------------------------------------------------------------
// Menu rows
// ---------------------------------------------------------------------------

/** One autocomplete row: display label, dim snippet, insert slug. */
export interface SuggestionRow {
  readonly label: string
  readonly snippet: string
  /** What accept inserts after the `@` (`@{mention} `). */
  readonly mention: string
}

/**
 * Keeps the best-scoring hit per document — the search returns chunks,
 * the menu lists documents.
 */
export function dedupeByDocument(hits: readonly MentionHit[]): MentionHit[] {
  const best = new Map<string, MentionHit>()
  for (const hit of hits) {
    const current = best.get(hit.documentId)
    if (current === undefined || hit.score > current.score) {
      best.set(hit.documentId, hit)
    }
  }
  return [...best.values()]
}

/** Search hits → menu rows, deduped by document and fitted to `width`. */
export function suggestionRows(
  hits: readonly MentionHit[],
  titleFor?: TitleLookup,
  width = 72
): SuggestionRow[] {
  return dedupeByDocument(hits).map((hit) => {
    const label = mentionLabel(hit, titleFor)
    return {
      label,
      snippet: fitSnippet(label, hit.snippet, width),
      mention: slugForMention(label),
    }
  })
}

/** `label — snippet` fitted to one popup row (snippet yields first). */
function fitSnippet(
  label: string,
  snippet: string,
  width: number
): string {
  const budget = Math.max(8, width - label.length - 3)
  return trimSnippet(snippet, budget)
}

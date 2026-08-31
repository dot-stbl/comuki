import {
  KEYED_SHAPES,
  resolveQuery,
  type SearchCatalogue,
  type SearchTarget,
} from "@/app/search"
import { can, type Session } from "@/shared/session"

/**
 * Identifiers inside a message, and where they point.
 *
 * Somebody pastes `5b1d7e40` out of a ticket into the composer, or the
 * assistant names a run in its prose. That string is the run, and a console
 * that renders it as inert grey text is making the operator copy it back out
 * and paste it into a search box — which is the gesture the product already
 * has a resolver for.
 *
 * ## Reused, not re-catalogued
 *
 * The product's identifier shapes live in `app/search/shapes.ts` and this
 * reads them. Nothing about a run id changes because it appeared in a sentence
 * rather than in a search box, and a second catalogue would be a second thing
 * to update the day a new entity is added — the exact failure that file's own
 * header argues against.
 *
 * ## Only the keyed tier
 *
 * `resolveShapes` runs the catalogue tier too, and that tier is right for a
 * search box and wrong for prose: it matches project handles and application
 * names as substrings, so a sentence containing the word "atlas" or "web" would
 * grow links on ordinary words. In a search box that is a disambiguation the
 * operator asked for; in a paragraph it is the interface underlining nouns.
 * The keyed tier is decided by the string alone — `5b1d7e40` can only be a run
 * — and that is the only tier a message is allowed to use.
 *
 * ## Access is asked here too
 *
 * A reference this session cannot open renders as plain text. The alternative
 * is a link into a forbidden state, which teaches the shape of somebody else's
 * access — the same rule the command palette keeps when it drops a resolved
 * row it cannot reach.
 */

/** One run of text: either words, or an identifier that resolved. */
export interface TextToken {
  /** The text exactly as it was written, punctuation included. */
  text: string
  /** Where it goes, when it is a reference this session may follow. */
  target: SearchTarget | null
}

/**
 * Punctuation an identifier can be wearing when it appears in a sentence.
 *
 * Stripped before the shape is tested and put back before the token is
 * rendered, so `«5b1d7e40»` links the id and keeps the quotes outside it. The
 * set is deliberately closed: `_`, `-` and `:` are *inside* real identifiers
 * (`wi_0101`, `ap-14`, `sha256:…`) and must never be trimmed.
 */
const TRIM = /^[«"'([{]+|[»"')\]}.,;:!?—…]+$/g

function bare(chunk: string): string {
  return chunk.replace(TRIM, "")
}

/** The one target a keyed shape gives for this string, or nothing. */
function keyedTarget(
  word: string,
  catalogue: SearchCatalogue
): SearchTarget | null {
  if (!word) {
    return null
  }
  for (const shape of KEYED_SHAPES) {
    const [hit] = shape.match(word, catalogue)
    if (hit) {
      return hit
    }
  }
  return null
}

/**
 * A message, split into the runs a renderer draws.
 *
 * Whitespace is preserved as its own token rather than collapsed, so the
 * message reads back exactly as it was typed — a pasted stack trace keeps its
 * shape, which is half the reason anybody pastes one.
 */
export function tokenizeReferences(
  text: string,
  catalogue: SearchCatalogue,
  session: Session
): TextToken[] {
  return text.split(/(\s+)/).flatMap<TextToken>((chunk) => {
    if (!chunk) {
      return []
    }
    const word = bare(chunk)
    const target = keyedTarget(word, catalogue)
    if (!target || !can(session, target.permission)) {
      return [{ text: chunk, target: null }]
    }

    // The identifier links; whatever it was wearing stays outside the link.
    const start = chunk.indexOf(word)
    const before = chunk.slice(0, start)
    const after = chunk.slice(start + word.length)
    return [
      ...(before ? [{ text: before, target: null }] : []),
      { text: word, target },
      ...(after ? [{ text: after, target: null }] : []),
    ]
  })
}

/**
 * Where a question gets handed off to, when the answer is a list.
 *
 * The console does not draw its own runs table. Asked to find something, it
 * offers *a filter on the real screen* — and it offers exactly the ones the
 * command palette offers, from the same call, because a second list of
 * destinations is a second list to forget to update the day a screen learns a
 * new filter parameter.
 *
 * `resolveQuery` returns four bands; only the hand-off band is a hand-off. The
 * `resolved` band is the identifier resolver, which messages already use one
 * token at a time, and the `section`/`act` bands are navigation, which the rail
 * is for.
 */
export interface ChatHandoff {
  id: string
  /** The screen, in its own words — `live runs`, `the queue`. */
  where: string
  /** What it will be narrowed to. */
  query: string
  href: string
}

/**
 * `resolve.ts` builds a hand-off's hint as `in <where>` and exports no `where`
 * of its own. Undoing the preposition is the one thing this needs that the
 * palette does not — the palette says "in live runs" under a row, chat says
 * "search live runs for …" in a sentence. A `where` field on `SearchItem`, or
 * an exported `HANDOFFS`, would remove this line; it is one line, in one place,
 * and it is not worth editing another agent's module for.
 */
const IN = /^in /

export function chatHandoffs(
  query: string,
  session: Session,
  catalogue: SearchCatalogue
): ChatHandoff[] {
  const trimmed = query.trim()
  if (!trimmed) {
    return []
  }
  return resolveQuery(trimmed, { session, catalogue })
    .filter((item) => item.group === "handoff")
    .map((item) => ({
      id: item.id,
      where: (item.hint ?? "").replace(IN, ""),
      query: item.label,
      href: item.href,
    }))
}

/**
 * What the operator was looking at, read out of the address bar.
 *
 * The dock opens over a screen the operator cannot see through the scrim, so
 * the screen comes with them instead — as a seeded reference in the composer.
 * "What they were looking at" is not a guess and not a global: it is whatever
 * the location already says, which is exactly the thing the product decided
 * locations are for.
 *
 * Two places carry a subject, read in this order:
 *
 * 1. **The path.** A detail screen is `/runs/5b1d7e40`, `/queue/workers/wk_e34d`
 *    — the entity the whole screen is about, spelled as a segment.
 * 2. **The search.** A list narrowed to one thing — `?w=wk_e34d`,
 *    `?q=ap-14` — is a list that is *about* that thing for as long as the
 *    filter holds.
 *
 * A screen that names nothing seeds nothing. "The queue" as a reference would
 * be a word, not an entity, and the composer's seed is an entity or absent —
 * a suggestion the operator can see the shape of, not a mode the conversation
 * is "in".
 *
 * Access is asked here for the same reason `tokenizeReferences` asks: a seed
 * the session cannot follow is a link into a forbidden state, and offering it
 * teaches the shape of somebody else's access. Refused quietly, by omission.
 */
export function referenceFromLocation(
  pathname: string,
  search: string,
  catalogue: SearchCatalogue,
  session: Session
): SearchTarget | null {
  const candidates = [
    ...pathname.split("/").filter(Boolean),
    ...Array.from(new URLSearchParams(search).values()),
  ]
  for (const part of candidates) {
    const target = keyedTarget(part, catalogue)
    if (target && can(session, target.permission)) {
      return target
    }
  }
  return null
}

import type { SidebarNavGroup } from "@/app/layout/app-shell-sidebar"
import { i18n } from "@/shared/i18n"
import { can, type Permission, type Session } from "@/shared/session"

import {
  navSections,
  visibleActs,
  type SearchAct,
  type SearchSection,
} from "./sections"
import { resolveShapes, type SearchCatalogue } from "./shapes"

/**
 * The resolver: three layers, one order, no data scanned.
 *
 * (a) **Identifier resolution** — the shape catalogue in `shapes.ts` reads the
 *     *shape* of the query and routes without searching anything.
 * (b) **Sections and acts** — the product's own places, matched as substrings
 *     of words the rail already teaches. Hidden exactly where the rail hides.
 * (c) **Hand-off** — for text that is neither, the palette does not invent a
 *     result list it cannot back. It offers to *narrow a screen* by that text,
 *     and enter lands on that screen with the filter already applied.
 *
 * Every layer answers in constant time against a closed catalogue. Nothing here
 * touches a row, which is what makes this a resolver rather than a search
 * engine wearing one's clothes.
 */

/** Which band of the palette a row belongs to. */
export type SearchGroup = "resolved" | "section" | "act" | "handoff"

/** One row in the palette. */
export interface SearchItem {
  /** Stable across renders and unique in the list — the collection's key. */
  id: string
  group: SearchGroup
  /** The kind, in the product's own word. Names the row without an icon. */
  kind: string
  /** What the row is about. */
  label: string
  /**
   * True when `label` is an identifier rather than words, so the row sets it
   * in the data voice. Every identifier in this product is a value, and a run
   * id set in the interface face would be the wrong one of the two voices.
   */
  value: boolean
  /** Supporting words after the label. Always the interface voice. */
  hint?: string
  /** Where enter goes. Path plus search string, ready for the router. */
  href: string
}

export interface ResolveContext {
  session: Session
  catalogue: SearchCatalogue
  /** The rail to read places out of. Defaults to the product's own. */
  nav?: SidebarNavGroup[]
  /** The acts to offer. Defaults to the product's own. */
  acts?: SearchAct[]
}

/**
 * Where free text can be handed off to.
 *
 * The list is short for a reason that is the whole design: a hand-off is only
 * offered to a screen whose filter is *addressable*, so pressing enter lands on
 * a list already narrowed rather than on a list the operator has to narrow
 * again by hand. Adding a screen here means putting its filter in the URL
 * first — see the note on `q` in `routes/runs/index.tsx`.
 */
interface Handoff {
  /** The words after the query, as a `shell` key (D7 — resolved at build). */
  hintKey: string
  /** Path, without the search string — the query is appended. */
  path: string
  /** The parameter that screen narrows on. */
  param: string
  permission: Permission
}

const HANDOFFS: Handoff[] = [
  {
    hintKey: "search.handoffHint.runs",
    path: "/runs",
    param: "q",
    permission: "runs.view",
  },
  {
    hintKey: "search.handoffHint.queue",
    path: "/queue",
    param: "q",
    permission: "queue.view",
  },
  {
    hintKey: "search.handoffHint.inbox",
    path: "/tasks",
    param: "q",
    permission: "inbox.view",
  },
]

/** An entry's own word when it carries a key, its literal when it does not. */
function word(text: string, key: string | undefined): string {
  return key ? i18n.t(`shell:${key}`) : text
}

function matches(needle: string, ...fields: (string | undefined)[]): boolean {
  return fields.some((field) => field?.toLowerCase().includes(needle))
}

function sectionItem(section: SearchSection): SearchItem {
  return {
    id: `section:${section.href}`,
    group: "section",
    kind: "section",
    label: section.label,
    value: false,
    hint: section.group.toLowerCase(),
    href: section.href,
  }
}

function actItem(act: SearchAct): SearchItem {
  return {
    id: `act:${act.href}`,
    group: "act",
    kind: "act",
    label: word(act.label, act.labelKey),
    value: false,
    hint: word(act.hint, act.hintKey),
    href: act.href,
  }
}

/**
 * The palette's rows for one query.
 *
 * An empty query is the resting list — every place and act this session can
 * reach, which is the honest answer to "what is here" and costs nothing to
 * produce. A query runs the three layers in order.
 */
export function resolveQuery(
  query: string,
  { session, catalogue, nav, acts }: ResolveContext
): SearchItem[] {
  const trimmed = query.trim()
  const sections = navSections(session, nav)
  const allowedActs = visibleActs(session, acts)

  if (!trimmed) {
    return [...sections.map(sectionItem), ...allowedActs.map(actItem)]
  }

  const needle = trimmed.toLowerCase()

  // (a) The shape catalogue, then the access rule. A destination this session
  // cannot open is dropped rather than shown and refused: the rail hides what
  // it cannot reach, and a palette that did otherwise would be teaching the
  // shape of somebody else's access.
  const resolved = resolveShapes(trimmed, catalogue)
    .filter((target) => can(session, target.permission))
    .map<SearchItem>((target) => ({
      id: `resolved:${target.kind}:${target.id}`,
      group: "resolved",
      kind: target.kind,
      label: target.id,
      // Every identifier is a value, and so is a project handle and an
      // application name — they are what a column holds, not what a sentence
      // says.
      value: true,
      hint: target.hint,
      href: target.href,
    }))

  // (b) Places and acts, by substring. The rail group is matched too, so
  // "observe" offers the four screens filed under it.
  const places = [
    ...sections
      .filter((section) => matches(needle, section.label, section.group))
      .map(sectionItem),
    ...allowedActs
      .filter((act) => matches(needle, act.label, act.hint))
      .map(actItem),
  ]

  // (c) The hand-off. Offered on every non-empty query rather than only when
  // the layers above came back empty: `web` resolves to two applications *and*
  // is a perfectly good thing to look for in run titles, and the operator is
  // the one who knows which they meant. It sits last, and it is worded as an
  // act — "search live runs for …" — because that is what it is. What it is
  // not, and must never become, is a list of rows this client guessed at.
  const handoffs = HANDOFFS.filter((handoff) =>
    can(session, handoff.permission)
  ).map<SearchItem>((handoff) => ({
    id: `handoff:${handoff.path}`,
    group: "handoff",
    kind: "search",
    label: trimmed,
    value: true,
    hint: i18n.t(`shell:${handoff.hintKey}`),
    href: `${handoff.path}?${handoff.param}=${encodeURIComponent(trimmed)}`,
  }))

  return [...resolved, ...places, ...handoffs]
}

/**
 * The palette's word for a row's kind, keyed for the `shell` namespace. The
 * machine word on the item stays as the closed vocabulary; the word an
 * operator reads is translated at the display edge.
 */
export const KIND_KEYS: Record<string, string> = {
  run: "search.kind.run",
  "work item": "search.kind.workItem",
  worker: "search.kind.worker",
  approval: "search.kind.approval",
  project: "search.kind.project",
  app: "search.kind.app",
  person: "search.kind.person",
  "api key": "search.kind.apiKey",
  image: "search.kind.image",
  section: "search.kind.section",
  act: "search.kind.act",
  search: "search.kind.search",
}

/** The bands, in the order the palette lays them out. */
export const GROUP_ORDER: SearchGroup[] = [
  "resolved",
  "section",
  "act",
  "handoff",
]

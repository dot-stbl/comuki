import type { Permission, ProjectRef } from "@/shared/session"

/**
 * The shape catalogue — the whole of it, in one module.
 *
 * ## Why shapes rather than a search
 *
 * An honest full-text search needs a server-side index, and this product does
 * not have one. What usually stands in for it until then is a client-side scan
 * of whatever arrays the mock happens to hold — a crutch that is thrown away
 * the moment the backend arrives, and that gets slower with every row seeded.
 *
 * This product has a better axis. Almost everything a person pastes into a
 * search box here comes out of a ticket, a log line or a chat message, and is
 * an **exact identifier** — and identifiers have shapes. `5b1d7e40` can only be
 * a run. `wk_e34d` can only be a worker. Reading the shape and routing on it
 * needs no index, no scan and no data at all: the work is proportional to the
 * number of *kinds* the product has, not to the volume of rows it holds. Adding
 * an entity is one rule in this file rather than an edit to a search index.
 *
 * ## The two tiers, and where ambiguity lives
 *
 * **Keyed shapes** are decided by the string alone — a regular expression over
 * a prefix and an alphabet. Their prefixes are disjoint by construction, so at
 * most one of them ever matches and the answer is a single destination.
 *
 * **Catalogue shapes** are matched against a *closed, small* list the session
 * already holds: the projects it can see, and the applications the platform
 * knows. This is where ambiguity lives, and it is bounded by construction —
 * four projects and eleven applications, not by how many runs exist. `comuki`
 * is a project handle and could equally be the head of an application name, so
 * the resolver answers with both candidates, each labelled by its kind, and
 * lets the operator say which they meant. That is a disambiguation, not a
 * results list.
 *
 * ## The destination has to be able to receive the query
 *
 * A shape earns its place only when the screen it points at can actually be
 * narrowed by the string being handed over. That is a contract in two halves,
 * and both have to hold: the shape resolves to a destination, and the
 * destination's own filter accepts what arrives. `u_…` and `k_…` are internal
 * identity keys that nothing in the product displays — but they get pasted out
 * of a log or an api response, so the people and api-key lists carry the id in
 * their match strings alongside the address and the visible prefix. Add a shape
 * without widening the destination's match and the operator lands on an empty
 * screen, which is worse than not resolving at all.
 *
 * ## The shape of the future
 *
 * On a real backend this becomes one call — `GET /resolve?q=` returning
 * `{ kind, id, href }` — so `href` is spelled here as the string that endpoint
 * will return, and swapping the mock for the server is a transport change with
 * no consumer to rewrite.
 */

/** What a resolved query turned out to be. The word is also the row's label. */
export type SearchKind =
  | "run"
  | "work item"
  | "worker"
  | "approval"
  | "project"
  | "app"
  | "person"
  | "api key"
  | "image"

/** One thing a query resolved to, and where it lives. */
export interface SearchTarget {
  kind: SearchKind
  /** The identifier, spelled the way the product spells it. */
  id: string
  /** Path and search string — what `GET /resolve?q=` will one day return. */
  href: string
  /** The act that opens the destination; a session without it never sees it. */
  permission: Permission
  /** Words after the identifier, when the identifier alone is not enough. */
  hint?: string
}

/**
 * The closed lists the catalogue tier matches against.
 *
 * Both are small and both are already in the client for other reasons — the
 * projects because every list in the product filters by them, the applications
 * because every run names one. Nothing here is a row of data: a project is not
 * scanned for a substring in its runs, only in its own handle.
 */
export interface SearchCatalogue {
  /** Projects this session can see, by handle and name. */
  projects: ProjectRef[]
  /** Every application the platform knows about. */
  apps: string[]
}

/**
 * One rule. `match` returns every target the query resolves to under this
 * shape — none, one, or (for the catalogue tier) a handful.
 */
export interface SearchShape {
  kind: SearchKind
  match: (query: string, catalogue: SearchCatalogue) => SearchTarget[]
}

/* --------------------------------------------------------------------------
 * The keyed shapes. One line each, and the prefixes do not overlap.
 * ----------------------------------------------------------------------- */

/** A run id: eight hex characters. The same string the trace is keyed on. */
const RUN = /^[0-9a-f]{8}$/i
/** A work item, as the queue spells it. */
const WORK_ITEM = /^wi_[0-9a-z]+$/i
/** A worker container. */
const WORKER = /^wk_[0-9a-z]+$/i
/** A pending approval. */
const APPROVAL = /^ap-\d{1,4}$/i
/** A project's internal id; the handle beside it is what screens display. */
const PROJECT_ID = /^p_[0-9a-z_]+$/i
/** A person's internal id. */
const USER_ID = /^u_[0-9a-z_]+$/i
/** An api key's internal id. */
const API_KEY_ID = /^k_[0-9a-z_]+$/i
/** The visible head of an api key — the only part of one that ever survives. */
const API_KEY_PREFIX = /^cmk_[0-9a-z]+$/i
/**
 * An email address, loosely. Loose on purpose: this is a routing decision, not
 * a validation — the cost of being wrong is one candidate the operator ignores.
 */
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
/** A container image, by its short digest. */
const DIGEST = /^sha256:[0-9a-f]{6,64}$/i

/** A keyed shape: a test on the string, and the destination it implies. */
function keyed(
  kind: SearchKind,
  pattern: RegExp,
  to: (id: string) => Omit<SearchTarget, "kind" | "id">
): SearchShape {
  return {
    kind,
    match: (query) =>
      pattern.test(query) ? [{ kind, id: query, ...to(query) }] : [],
  }
}

/** Percent-encodes a value on its way into a search string. */
const q = (value: string) => encodeURIComponent(value)

export const KEYED_SHAPES: SearchShape[] = [
  keyed("run", RUN, (id) => ({
    href: `/runs/${id}`,
    permission: "runs.view",
    hint: "in live runs",
  })),
  keyed("work item", WORK_ITEM, (id) => ({
    href: `/queue?q=${q(id)}`,
    permission: "queue.view",
    hint: "in the claim queue",
  })),
  keyed("worker", WORKER, (id) => ({
    href: `/queue?w=${q(id)}`,
    permission: "queue.view",
    hint: "in the worker pool",
  })),
  keyed("approval", APPROVAL, (id) => ({
    href: `/approvals?q=${q(id)}`,
    permission: "plans.approve",
    hint: "in approvals",
  })),
  keyed("image", DIGEST, (id) => ({
    // The pool is where a digest is *actionable* — it is the column that
    // explains why one container is draining while its neighbours are not.
    href: `/queue?w=${q(id)}`,
    permission: "queue.view",
    hint: "workers on this image",
  })),
  keyed("api key", API_KEY_PREFIX, (id) => ({
    href: `/identity?tab=keys&q=${q(id)}`,
    permission: "identity.manage",
    hint: "in api keys",
  })),
  keyed("person", EMAIL, (id) => ({
    href: `/identity?tab=users&q=${q(id)}`,
    permission: "identity.manage",
    hint: "in people",
  })),
  // The two internal ids. Neither is shown anywhere in the product, but both
  // get pasted out of a log or an api response, and both lists now carry the
  // id in their match string so the row is actually found.
  keyed("person", USER_ID, (id) => ({
    href: `/identity?tab=users&q=${q(id)}`,
    permission: "identity.manage",
    hint: "in people",
  })),
  keyed("api key", API_KEY_ID, (id) => ({
    href: `/identity?tab=keys&q=${q(id)}`,
    permission: "identity.manage",
    hint: "in api keys",
  })),
  {
    // A project id resolves through the session's own catalogue, because the
    // registry narrows by handle and the id is not one. An id this session
    // cannot see resolves to nothing and falls through to the hand-off, which
    // is the truth: it is not a project as far as this shift is concerned.
    kind: "project",
    match: (query, catalogue) => {
      if (!PROJECT_ID.test(query)) {
        return []
      }
      const project = catalogue.projects.find(
        (entry) => entry.id.toLowerCase() === query.toLowerCase()
      )
      return project ? [projectTarget(project)] : []
    },
  },
]

/* --------------------------------------------------------------------------
 * The catalogue shapes. Bounded by the catalogue, never by the data.
 * ----------------------------------------------------------------------- */

/** Below this a query is a letter, and a letter names everything. */
const CATALOGUE_MIN = 2

/** How many candidates one kind may offer before it stops being a choice. */
const CATALOGUE_MAX = 4

function projectTarget(project: ProjectRef): SearchTarget {
  return {
    kind: "project",
    id: project.key,
    href: `/projects?q=${q(project.key)}`,
    permission: "projects.view",
    hint: project.name,
  }
}

export const CATALOGUE_SHAPES: SearchShape[] = [
  {
    kind: "project",
    match: (query, catalogue) => {
      if (query.length < CATALOGUE_MIN) {
        return []
      }
      const needle = query.toLowerCase()
      return catalogue.projects
        .filter(
          (project) =>
            project.key.toLowerCase().includes(needle) ||
            project.name.toLowerCase().includes(needle)
        )
        .slice(0, CATALOGUE_MAX)
        .map(projectTarget)
    },
  },
  {
    kind: "app",
    match: (query, catalogue) => {
      if (query.length < CATALOGUE_MIN) {
        return []
      }
      const needle = query.toLowerCase()
      return catalogue.apps
        .filter((app) => app.toLowerCase().includes(needle))
        .slice(0, CATALOGUE_MAX)
        .map((app) => ({
          kind: "app" as const,
          id: app,
          href: `/runs?q=${q(app)}`,
          permission: "runs.view" as const,
          hint: "runs on this app",
        }))
    },
  },
]

/**
 * Every shape, in precedence order: keyed first, catalogue second.
 *
 * The order is the whole of the precedence rule. A string that *is* an
 * identifier is that identifier and nothing else — `wi_0101` is never offered
 * as a fuzzy match against an application name — so the keyed tier answers
 * alone whenever it answers at all.
 */
export const SEARCH_SHAPES: SearchShape[] = [
  ...KEYED_SHAPES,
  ...CATALOGUE_SHAPES,
]

/**
 * What a query resolves to, before anything is asked about access.
 *
 * Returns the keyed answer on its own when there is one, and otherwise every
 * catalogue candidate — which is the ambiguous case, and a normal one.
 */
export function resolveShapes(
  query: string,
  catalogue: SearchCatalogue
): SearchTarget[] {
  const trimmed = query.trim()
  if (!trimmed) {
    return []
  }

  const keyedHits = KEYED_SHAPES.flatMap((shape) =>
    shape.match(trimmed, catalogue)
  )
  if (keyedHits.length > 0) {
    return keyedHits
  }

  return CATALOGUE_SHAPES.flatMap((shape) => shape.match(trimmed, catalogue))
}

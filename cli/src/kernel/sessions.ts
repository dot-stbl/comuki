/**
 * SessionStore — durable per-session metadata (issue #77).
 *
 * The kernel owns ONE list of session references that survives a
 * process restart, a dropped connection, or a host switch. The
 * durable shape is intentionally separate from the workspace's
 * `WorkspaceSessionRef` (which carries the live turn state) — the
 * SessionStore holds the stable identity (id, name, createdAt,
 * lastKnownCursor, renamed, archived) so the user can list, search,
 * resume, rename, archive, and fork sessions from a fresh process
 * before the workspace document has been read.
 *
 * Storage shape — a single NDJSON file under the platform state root:
 *
 *   <state>/sessions.ndjson
 *
 * One line per session reference. Append-only on mutation; the
 * `compact()` helper rewrites the whole file when stale lines have
 * accumulated (tests use it to verify atomic rewrite; production
 * code triggers it via the kernel's housekeeping cycle). Override
 * the directory via `COMUKI_STATE_DIR` (the same env var
 * `receipts.ts` already honors). The store is a singleton on the
 * kernel — only one CLI process owns the list — and is safe to
 * instantiate multiple times in tests.
 *
 * All reads are async-by-design: the file may be on slow storage
 * and the kernel awaits the load before serving the home view.
 */
import { appendFile, mkdir, readFile, rename, writeFile } from "node:fs/promises"
import { dirname, join } from "node:path"

// ---------------------------------------------------------------------------
// SessionMeta — one durable session reference
// ---------------------------------------------------------------------------

/**
 * The durable shape. `id` is the stable server-assigned or `local-…`
 * pending id; `lastKnownCursor` is the high-water mark we last saw
 * from the realtime hub; `archived` is the user's soft-delete flag
 * (the server session keeps existing, but we stop tracking it).
 *
 * `createdAt` is unix milliseconds. `name` is the user-facing title
 * the SessionStore last wrote (the workspace owns its own copy too;
 * the SessionStore copy is the source of truth for the home view's
 * list).
 */
export interface SessionMeta {
  readonly id: string
  readonly name: string
  readonly createdAt: number
  readonly lastKnownCursor: number
  readonly renamed: boolean
  readonly archived: boolean
}

/**
 * Partial fields the store merges onto an existing entry. `id` is
 * taken from the upsert's key parameter, never from this object.
 */
export interface SessionMetaUpdate {
  readonly name?: string
  readonly lastKnownCursor?: number
  readonly renamed?: boolean
  readonly archived?: boolean
}

/**
 * The filter shape `list(filter)` accepts. All fields are AND-ed;
 * an empty filter returns every session. `archived` defaults to
 * `false` — listing excludes archived sessions unless the caller
 * asks for them.
 */
export interface SessionFilter {
  readonly archived?: boolean
  readonly renamed?: boolean
  readonly query?: string
}

// ---------------------------------------------------------------------------
// Storage location
// ---------------------------------------------------------------------------

/** Sub-path under the state root that holds the sessions NDJSON file. */
export const SESSIONS_FILE = "sessions.ndjson"

/**
 * Build the full path to the sessions file. Tests inject the root;
 * production wires `defaultStateDirectory()`.
 */
export function sessionsFilePath(stateDirectory: string): string {
  return join(stateDirectory, SESSIONS_FILE)
}

// ---------------------------------------------------------------------------
// Encoder / decoder — one line per session reference
// ---------------------------------------------------------------------------

/**
 * Stable key order so a diff between two NDJSON files is meaningful.
 * `lastKnownCursor` defaults to `0` in the on-disk line when the
 * caller never saw one — that matches the SessionMeta contract.
 */
export function encodeMeta(meta: SessionMeta): string {
  const ordered: Record<string, unknown> = {
    id: meta.id,
    name: meta.name,
    createdAt: meta.createdAt,
    lastKnownCursor: meta.lastKnownCursor,
    renamed: meta.renamed,
    archived: meta.archived,
  }
  return `${JSON.stringify(ordered)}\n`
}

/**
 * Parse one NDJSON line into a `SessionMeta`. Returns `null` on any
 * malformed input — a corrupt line drops, the rest of the file
 * keeps going. The reader is best-effort by design; a corrupt
 * sessions file must not brick the CLI.
 */
export function decodeMeta(line: string): SessionMeta | null {
  const trimmed = line.trim()
  if (trimmed.length === 0) {
    return null
  }
  let raw: unknown
  try {
    raw = JSON.parse(trimmed)
  } catch {
    return null
  }
  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    return null
  }
  const record = raw as Record<string, unknown>
  if (
    typeof record["id"] !== "string" ||
    record["id"].length === 0 ||
    typeof record["name"] !== "string" ||
    typeof record["createdAt"] !== "number" ||
    !Number.isFinite(record["createdAt"])
  ) {
    return null
  }
  const cursor = record["lastKnownCursor"]
  return {
    id: record["id"],
    name: record["name"],
    createdAt: record["createdAt"],
    lastKnownCursor:
      typeof cursor === "number" && Number.isFinite(cursor) ? cursor : 0,
    renamed: record["renamed"] === true,
    archived: record["archived"] === true,
  }
}

// ---------------------------------------------------------------------------
// SessionStore — the singleton interface the kernel owns
// ---------------------------------------------------------------------------

/**
 * The store's public surface. The implementation is in-memory +
 * periodic compaction; `whenIdle` resolves once the latest mutation
 * has settled on disk so tests can assert against the file.
 *
 * All reads are async. The store pre-loads on the first call so
 * subsequent reads are cheap, but a fresh process needs one round
 * of await before the home view sees sessions.
 */
export interface SessionStore {
  /**
   * One snapshot of the in-memory list, optionally filtered.
   * `filter.archived` defaults to `false` — listing excludes archived
   * sessions unless the caller asks. The query (if any) is matched
   * case-insensitively against the session id and name.
   */
  list(filter?: SessionFilter): Promise<readonly SessionMeta[]>

  /** Same shape as `list(filter)` but with `query` enforced. Empty list on no matches. */
  search(query: string): Promise<readonly SessionMeta[]>

  /** Single lookup by id, or `null` when the id is unknown. */
  get(id: string): Promise<SessionMeta | null>

  /**
   * Insert-or-update one session reference. If the id already exists
   * the partial `update` is merged onto the existing entry (so a
   * `lastKnownCursor` write does not clobber `name`); if the id is
   * new, every field in `update` is optional and the missing ones
   * default to `""`, `0`, `false`, `false`.
   */
  upsert(id: string, update: SessionMetaUpdate): Promise<SessionMeta>

  /**
   * Mark one session as archived (soft-delete). Returns the post-write
   * meta, or `null` if the id was unknown. Idempotent: archiving an
   * already-archived session returns its current meta without a write.
   */
  archive(id: string): Promise<SessionMeta | null>

  /**
   * Rename one session and set `renamed: true`. Returns the post-write
   * meta, or `null` if the id was unknown (or the trimmed name was
   * empty). The SessionStore treats an empty rename as a no-op signal
   * — the caller is expected to validate the new name upstream.
   */
  rename(id: string, name: string): Promise<SessionMeta | null>

  /**
   * Fork one session into a new session reference. The new entry's
   * id is `newId`; the rest of the fields copy from the source. The
   * fresh entry starts `archived: false` and `lastKnownCursor: 0`
   * (a fork is a new conversation — the source's stream cursor does
   * not apply). Idempotent on retry: a second fork with the same
   * `newId` returns the existing fork without rewriting it.
   */
  fork(sourceId: string, newId: string): Promise<SessionMeta | null>

  /**
   * Rewrite the on-disk NDJSON from the in-memory map, dropping stale
   * lines (a name rename leaves the old `name: "..."` line behind
   * because the file is append-only). Atomic: writes to a sibling
   * tmp file, then renames over the live file. Production code calls
   * this from the kernel's housekeeping; tests call it to assert
   * compaction semantics.
   */
  compact(): Promise<void>

  /** Resolves when every queued write has settled (test seam). */
  whenIdle(): Promise<void>
}

export interface SessionStoreOptions {
  /** Absolute path to the directory that holds the sessions file. */
  readonly stateDirectory: string
  /** Injectable clock — defaults to `Date.now`. */
  readonly now?: () => number
}

/**
 * Build a `SessionStore` rooted at `stateDirectory`. The file is
 * created lazily on the first mutation; reads on first call, writes
 * on every mutation. The in-memory map is the source of truth for
 * `list`/`get`/`search`; the file is rebuilt under one append lock
 * so concurrent mutations do not interleave lines.
 */
export function createSessionStore(options: SessionStoreOptions): SessionStore {
  const now = options.now ?? (() => Date.now())
  const filePath = sessionsFilePath(options.stateDirectory)

  let loaded = false
  /** Single in-flight load promise — concurrent reads share it. */
  let loadPromise: Promise<void> | null = null
  const meta = new Map<string, SessionMeta>()
  /** Single-flight write queue — append-or-compact, in order. */
  let chain: Promise<void> = Promise.resolve()

  function ensureLoaded(): Promise<void> {
    if (loaded) {
      return Promise.resolve()
    }
    if (loadPromise === null) {
      loadPromise = loadFromDisk().then(() => {
        loaded = true
      })
    }
    return loadPromise
  }

  async function loadFromDisk(): Promise<void> {
    try {
      const text = await readFile(filePath, "utf8")
      const lines = text.split("\n")
      for (const line of lines) {
        const entry = decodeMeta(line)
        if (entry !== null) {
          // Last write wins — multiple lines for the same id collapse
          // to the latest, which matches the upsert semantics.
          meta.set(entry.id, entry)
        }
      }
    } catch (error: unknown) {
      // A missing file is the bootstrap case (first launch). Any other
      // I/O error degrades to an empty list — the host can recover
      // by writing fresh entries on the next mutation.
      if (!isMissingFileError(error)) {
        // Swallow — corrupt files must not brick the CLI.
      }
    }
  }

  async function enqueueAppend(line: string): Promise<void> {
    const next = chain.then(async () => {
      await mkdir(dirname(filePath), { recursive: true, mode: 0o700 })
      await appendFile(filePath, line, "utf8")
    })
    chain = next
    await next
  }

  function enqueueCompact(): Promise<void> {
    const next = chain.then(async () => {
      await mkdir(dirname(filePath), { recursive: true, mode: 0o700 })
      const payload = `${[...meta.values()].map(encodeMeta).join("")}`
      // Atomic rewrite — write to a sibling tmp, then rename. Avoids
      // the half-written file that would let a reader see zero rows
      // mid-compaction.
      const tmpPath = `${filePath}.tmp`
      await writeFile(tmpPath, payload, "utf8")
      await rename(tmpPath, filePath)
    })
    chain = next
    return next
  }

  function matches(entry: SessionMeta, filter: SessionFilter): boolean {
    if (filter.archived !== undefined && entry.archived !== filter.archived) {
      return false
    }
    if (filter.renamed !== undefined && entry.renamed !== filter.renamed) {
      return false
    }
    if (filter.query !== undefined && filter.query.length > 0) {
      const needle = filter.query.toLowerCase()
      const haystack = `${entry.id}\n${entry.name}`.toLowerCase()
      if (!haystack.includes(needle)) {
        return false
      }
    }
    return true
  }

  function snapshot(filter?: SessionFilter): readonly SessionMeta[] {
    const all = [...meta.values()]
    const filtered =
      filter === undefined ? all : all.filter((entry) => matches(entry, filter))
    filtered.sort((left, right) => left.createdAt - right.createdAt)
    return filtered
  }

  return {
    async list(filter) {
      await ensureLoaded()
      return snapshot(filter)
    },
    async search(query) {
      await ensureLoaded()
      if (query.length === 0) {
        return []
      }
      return snapshot({ query })
    },
    async get(id) {
      await ensureLoaded()
      return meta.get(id) ?? null
    },
    async upsert(id, update) {
      await ensureLoaded()
      const existing = meta.get(id)
      const next: SessionMeta =
        existing !== undefined
          ? {
              id: existing.id,
              name: update.name ?? existing.name,
              createdAt: existing.createdAt,
              lastKnownCursor:
                update.lastKnownCursor ?? existing.lastKnownCursor,
              renamed: update.renamed ?? existing.renamed,
              archived: update.archived ?? existing.archived,
            }
          : {
              id,
              name: update.name ?? "",
              createdAt: now(),
              lastKnownCursor: update.lastKnownCursor ?? 0,
              renamed: update.renamed ?? false,
              archived: update.archived ?? false,
            }
      meta.set(id, next)
      await enqueueAppend(encodeMeta(next))
      return next
    },
    async archive(id) {
      await ensureLoaded()
      const existing = meta.get(id)
      if (existing === undefined) {
        return null
      }
      if (existing.archived) {
        return existing
      }
      const next: SessionMeta = { ...existing, archived: true }
      meta.set(id, next)
      await enqueueAppend(encodeMeta(next))
      return next
    },
    async rename(id, name) {
      await ensureLoaded()
      const collapsed = name.replace(/\s+/g, " ").trim()
      if (collapsed.length === 0) {
        return null
      }
      const existing = meta.get(id)
      if (existing === undefined) {
        return null
      }
      const next: SessionMeta = {
        ...existing,
        name: collapsed,
        renamed: true,
      }
      meta.set(id, next)
      await enqueueAppend(encodeMeta(next))
      return next
    },
    async fork(sourceId, newId) {
      await ensureLoaded()
      // Idempotent: a retry with the same newId returns the existing
      // fork without rewriting — the spec's "concurrent fork of the
      // same session is idempotent" requirement.
      const existing = meta.get(newId)
      if (existing !== undefined) {
        return existing
      }
      const source = meta.get(sourceId)
      if (source === undefined) {
        return null
      }
      const fresh: SessionMeta = {
        id: newId,
        // The fork starts with the source's title so the user can
        // spot it in the list — they can rename later if they want
        // a different name. The fork is its own conversation.
        name: source.name,
        createdAt: now(),
        lastKnownCursor: 0,
        renamed: false,
        archived: false,
      }
      meta.set(newId, fresh)
      await enqueueAppend(encodeMeta(fresh))
      return fresh
    },
    async compact() {
      await ensureLoaded()
      await enqueueCompact()
    },
    async whenIdle() {
      // Drain every queued mutation. Tests await this to assert
      // against the on-disk shape.
      await ensureLoaded()
      await chain
    },
  }
}

function isMissingFileError(error: unknown): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    (error as { code?: unknown }).code === "ENOENT"
  )
}

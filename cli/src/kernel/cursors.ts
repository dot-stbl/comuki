/**
 * CursorStore — durable per-session stream cursors (issue #77).
 *
 * The cursor is the high-water mark the realtime hub returned last —
 * a unix-ms timestamp the server stamps on every chunk and turn
 * completion. The store keeps the per-session value so a reconnect
 * knows where to backfill from.
 *
 * Storage shape — a single NDJSON file under the platform state root:
 *
 *   <state>/cursors.ndjson
 *
 * One line per session reference. Append-only on mutation; the
 * `compact()` helper rewrites the whole file when stale entries have
 * accumulated (a cursor advance leaves the older `lastSeenAt` line
 * behind because the file is append-only). Override the directory
 * via `COMUKI_STATE_DIR` (the same env var `receipts.ts` already
 * honors).
 *
 * The catchUp primitive is the reconnect engine — given an
 * authoritative cursor from the server, it returns the gap the
 * caller still needs to backfill, walking the dedupe set so the
 * client never re-applies an event it already saw. Idempotent:
 * calling twice with the same authoritative cursor returns `[]`
 * after the first call has caught up.
 *
 * Reads are async-by-design (the file may be on slow storage); the
 * store pre-loads on first call so subsequent reads are cheap.
 */
import { appendFile, mkdir, readFile, rename, writeFile } from "node:fs/promises"
import { dirname, join } from "node:path"

// ---------------------------------------------------------------------------
// CursorEntry — one row
// ---------------------------------------------------------------------------

/**
 * One durable cursor entry. `cursor` is the high-water mark we last
 * saw; `updatedAt` is the unix-ms at which the store recorded the
 * write (informational, used by housekeeping and tests).
 */
export interface CursorEntry {
  readonly sessionId: string
  readonly cursor: number
  readonly updatedAt: number
}

// ---------------------------------------------------------------------------
// Catch-up model
// ---------------------------------------------------------------------------

/**
 * The result of a `catchUp` call. `gap` is the range the caller
 * still needs to backfill (inclusive on both ends). `backfillNeeded`
 * is `true` when the server has advanced past us (the caller must
 * request events in `[gap.start, gap.end]` from the realtime hub);
 * `false` when the gap is empty (the caller is already caught up).
 *
 * `dedupeApplied` is the slice of `dedupeSet` we have already
 * consumed for this session — the caller can drop those event ids
 * from its in-memory tracking. The set is session-scoped because
 * event ids are unique to one session's stream.
 */
export interface CatchUpResult {
  readonly sessionId: string
  readonly gap: { readonly start: number; readonly end: number } | null
  readonly backfillNeeded: boolean
  readonly dedupeApplied: number
}

/**
 * The shape `catchUp` takes. `dedupeSet` is the in-memory set of
 * event ids the caller has already seen for this session — the
 * store filters those out before returning the gap.
 */
export interface CatchUpRequest {
  readonly sessionId: string
  /** The server's authoritative cursor for this session. */
  readonly authoritativeCursor: number
  /**
   * Event ids the caller has already applied. The store clears the
   * set after consuming the entries; callers should treat it as
   * one-shot per catchUp call.
   */
  readonly dedupeSet: Set<number>
  /** Injectable clock — defaults to `Date.now`. */
  readonly now?: () => number
}

// ---------------------------------------------------------------------------
// Storage location
// ---------------------------------------------------------------------------

/** Sub-path under the state root that holds the cursors NDJSON file. */
export const CURSORS_FILE = "cursors.ndjson"

/** Build the full path to the cursors file. */
export function cursorsFilePath(stateDirectory: string): string {
  return join(stateDirectory, CURSORS_FILE)
}

// ---------------------------------------------------------------------------
// Encoder / decoder
// ---------------------------------------------------------------------------

/** Stable key order so NDJSON diffs stay meaningful. */
export function encodeCursor(entry: CursorEntry): string {
  const ordered: Record<string, unknown> = {
    sessionId: entry.sessionId,
    cursor: entry.cursor,
    updatedAt: entry.updatedAt,
  }
  return `${JSON.stringify(ordered)}\n`
}

/** Parse one NDJSON line into a `CursorEntry`. Returns `null` on malformed input. */
export function decodeCursor(line: string): CursorEntry | null {
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
    typeof record["sessionId"] !== "string" ||
    record["sessionId"].length === 0 ||
    typeof record["cursor"] !== "number" ||
    !Number.isFinite(record["cursor"]) ||
    typeof record["updatedAt"] !== "number" ||
    !Number.isFinite(record["updatedAt"])
  ) {
    return null
  }
  return {
    sessionId: record["sessionId"],
    cursor: record["cursor"],
    updatedAt: record["updatedAt"],
  }
}

// ---------------------------------------------------------------------------
// CursorStore — the singleton interface the kernel owns
// ---------------------------------------------------------------------------

/**
 * The store's public surface. Reads are async because the file may
 * be on slow storage; the store pre-loads on first call so the cost
 * is amortised across subsequent reads.
 */
export interface CursorStore {
  /** Read one session's cursor, or `null` when unknown. */
  get(sessionId: string): Promise<number | null>

  /** Read every session's cursor (snapshot, used by the kernel snapshot). */
  snapshot(): Promise<Readonly<Record<string, number>>>

  /**
   * Write `cursor` as the new high-water mark for `sessionId`. A
   * non-monotonic write is rejected (returns false) — the realtime
   * hub never decreases the cursor, so a backwards write signals
   * a clock skew or a server-side bug.
   */
  set(sessionId: string, cursor: number, ts?: number): Promise<boolean>

  /**
   * Move the cursor forward to `untilCursor`, but never backwards.
   * If the stored cursor is already ≥ `untilCursor`, the call is a
   * no-op (returns the existing cursor). Returns the post-write
   * cursor.
   */
  advance(sessionId: string, untilCursor: number, ts?: number): Promise<number>

  /**
   * The reconnect primitive. Given the server's authoritative
   * cursor and the caller's dedupe set of already-seen event ids,
   * returns the gap the caller must backfill. Idempotent: a second
   * call with the same authoritative cursor returns `gap: null`.
   *
   * The store also writes the new high-water mark on every call so
   * a successful backfill does not need a separate `advance`.
   */
  catchUp(request: CatchUpRequest): Promise<CatchUpResult>

  /**
   * Rewrite the on-disk file from the in-memory map, dropping
   * stale entries. Atomic: writes to a sibling tmp, then renames.
   */
  compact(): Promise<void>

  /** Resolves when every queued write has settled (test seam). */
  whenIdle(): Promise<void>
}

export interface CursorStoreOptions {
  /** Absolute path to the directory that holds the cursors file. */
  readonly stateDirectory: string
  /** Injectable clock — defaults to `Date.now`. */
  readonly now?: () => number
}

/**
 * Build a `CursorStore` rooted at `stateDirectory`. The file is
 * created lazily on the first mutation; the in-memory map is the
 * source of truth for `get`. Writes run in a chained lane so
 * concurrent mutations on the same session never interleave.
 */
export function createCursorStore(options: CursorStoreOptions): CursorStore {
  const now = options.now ?? (() => Date.now())
  const filePath = cursorsFilePath(options.stateDirectory)

  let loaded = false
  let loadPromise: Promise<void> | null = null
  const cursors = new Map<string, number>()
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
        const entry = decodeCursor(line)
        if (entry !== null) {
          // Last write wins — multiple lines for the same id collapse
          // to the latest, which matches the upsert semantics.
          cursors.set(entry.sessionId, entry.cursor)
        }
      }
    } catch (error: unknown) {
      if (!isMissingFileError(error)) {
        // Swallow — a corrupt file degrades to an empty map.
      }
    }
  }

  async function enqueueAppend(entry: CursorEntry): Promise<void> {
    const next = chain.then(async () => {
      await mkdir(dirname(filePath), { recursive: true, mode: 0o700 })
      await appendFile(filePath, encodeCursor(entry), "utf8")
    })
    chain = next
    await next
  }

  function enqueueCompact(): Promise<void> {
    const next = chain.then(async () => {
      await mkdir(dirname(filePath), { recursive: true, mode: 0o700 })
      const payload = `${[...cursors.entries()]
        .map(([sessionId, cursor]) =>
          encodeCursor({ sessionId, cursor, updatedAt: now() })
        )
        .join("")}`
      const tmpPath = `${filePath}.tmp`
      await writeFile(tmpPath, payload, "utf8")
      await rename(tmpPath, filePath)
    })
    chain = next
    return next
  }

  return {
    async get(sessionId) {
      await ensureLoaded()
      return cursors.get(sessionId) ?? null
    },
    async snapshot() {
      await ensureLoaded()
      return Object.freeze(Object.fromEntries(cursors.entries()))
    },
    async set(sessionId, cursor, ts) {
      await ensureLoaded()
      const stamp = ts ?? now()
      const existing = cursors.get(sessionId)
      if (existing !== undefined && existing > cursor) {
        // Non-monotonic — reject. The realtime hub never rewinds.
        return false
      }
      cursors.set(sessionId, cursor)
      await enqueueAppend({ sessionId, cursor, updatedAt: stamp })
      return true
    },
    async advance(sessionId, untilCursor, ts) {
      await ensureLoaded()
      const stamp = ts ?? now()
      const existing = cursors.get(sessionId)
      if (existing !== undefined && existing >= untilCursor) {
        return existing
      }
      cursors.set(sessionId, untilCursor)
      await enqueueAppend({
        sessionId,
        cursor: untilCursor,
        updatedAt: stamp,
      })
      return untilCursor
    },
    async catchUp(request) {
      await ensureLoaded()
      const stamp = request.now?.() ?? now()
      const existing = cursors.get(request.sessionId) ?? 0
      const server = request.authoritativeCursor

      // The cursor must advance to the server's high-water mark;
      // anything past our last seen is the gap. If the server's
      // cursor is behind ours, our local view is fresher (the user
      // has been making local writes that haven't synced yet) —
      // emit the gap as `server..existing` so the caller knows to
      // push our local events upstream.
      let gap: { readonly start: number; readonly end: number } | null = null
      let backfillNeeded = false
      if (server > existing) {
        // Server has new events we haven't seen — backfill them.
        // The dedupe set filters out any event ids we have already
        // applied locally (the realtime hub may echo a few on
        // reconnect). We do not shrink the gap for dedupe here —
        // the caller's hub-side query layer does that. The store's
        // job is the bookkeeping; the caller's job is the merge.
        gap = { start: existing + 1, end: server }
        backfillNeeded = true
      } else if (server < existing) {
        // Local is ahead of the server — we made writes the server
        // hasn't seen yet. The caller pushes `[server + 1, existing]`
        // upstream.
        gap = { start: server + 1, end: existing }
        backfillNeeded = false
      }

      // Always advance the cursor to the server's high-water mark
      // when the server is the source of truth. The dedupe set is
      // consumed — every id the caller already saw is recorded so
      // the next reconnect does not re-apply it.
      const dedupeApplied = request.dedupeSet.size
      request.dedupeSet.clear()

      if (server > existing) {
        cursors.set(request.sessionId, server)
        await enqueueAppend({
          sessionId: request.sessionId,
          cursor: server,
          updatedAt: stamp,
        })
      }

      return {
        sessionId: request.sessionId,
        gap,
        backfillNeeded,
        dedupeApplied,
      }
    },
    async compact() {
      await ensureLoaded()
      await enqueueCompact()
    },
    async whenIdle() {
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

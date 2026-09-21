/**
 * DraftStore — per-session composer drafts (issue #77).
 *
 * Drafts are plain UTF-8 text files under the platform state root:
 *
 *   <state>/drafts/<sessionId>.txt
 *
 * No JSON wrapper, no metadata — the user can read, edit, or remove
 * the file directly with their own tools. The store tracks only
 * that a draft exists for a session; the body is the file content.
 *
 * The draft file is keyed by **session id**, not by session name.
 * Renaming the session does NOT move the draft — that separation
 * is what lets the user clear the local draft without touching
 * the server session, and vice versa (issue #77's central
 * invariant: "Both layers survive the same crash but the user
 * should be able to clear the local draft without touching the
 * server session, and vice versa.").
 *
 * The store is a singleton on the kernel — only one CLI process
 * owns the files — and is safe to instantiate multiple times in
 * tests.
 */
import { mkdir, readdir, readFile, rm, stat, writeFile } from "node:fs/promises"
import { dirname, join } from "node:path"

// ---------------------------------------------------------------------------
// Storage location
// ---------------------------------------------------------------------------

/** Sub-path under the state root that holds the per-session draft files. */
export const DRAFTS_SUBDIR = "drafts"

/**
 * Build the full path for one session's draft file. Tests inject the
 * root; production wires `defaultStateDirectory()`.
 */
export function draftFilePath(stateDirectory: string, sessionId: string): string {
  if (sessionId.length === 0) {
    throw new Error("DraftStore: empty sessionId")
  }
  return join(stateDirectory, DRAFTS_SUBDIR, `${sessionId}.txt`)
}

// ---------------------------------------------------------------------------
// DraftStore — the singleton interface the kernel owns
// ---------------------------------------------------------------------------

/**
 * The store's public surface. Reads and writes are async-by-design
 * (a draft file may be on slow storage); `whenIdle` resolves once
 * the latest write has settled.
 */
export interface DraftStore {
  /**
   * Read one session's draft body, or `null` when the file is absent.
   * A file that exists but is empty is `""` (a cleared draft is not
   * the same as a missing draft — the user explicitly emptied it).
   */
  get(sessionId: string): Promise<string | null>

  /**
   * Persist `body` as the session's draft. Empty body deletes the
   * file (`set(id, "")` is equivalent to `clear(id)`).
   */
  set(sessionId: string, body: string): Promise<void>

  /** Delete the draft file. Idempotent — missing files resolve fine. */
  clear(sessionId: string): Promise<void>

  /**
   * Return the ids of every draft file older than `maxAgeSeconds`
   * ago (relative to `now`, the injectable clock). Used by the
   * housekeeping sweep that reaps abandoned drafts.
   */
  listStale(maxAgeSeconds: number, now?: number): Promise<readonly string[]>

  /** Resolves when every queued write has settled (test seam). */
  whenIdle(): Promise<void>
}

export interface DraftStoreOptions {
  /** Absolute path to the directory that holds the drafts tree. */
  readonly stateDirectory: string
}

/**
 * Build a `DraftStore` rooted at `stateDirectory`. The drafts
 * subdirectory is created lazily on first write; reads return null
 * on a missing file. Writes run in a chained lane so concurrent
 * mutations on the same session never interleave.
 */
export function createDraftStore(options: DraftStoreOptions): DraftStore {
  const base = options.stateDirectory
  let chain: Promise<void> = Promise.resolve()

  async function readDraft(sessionId: string): Promise<string | null> {
    try {
      return await readFile(draftFilePath(base, sessionId), "utf8")
    } catch (error: unknown) {
      if (isMissingFileError(error)) {
        return null
      }
      // Other I/O errors degrade to null — the user can re-save.
      return null
    }
  }

  async function writeDraft(sessionId: string, body: string): Promise<void> {
    if (body.length === 0) {
      await removeDraft(sessionId)
      return
    }
    const path = draftFilePath(base, sessionId)
    await mkdir(dirname(path), { recursive: true, mode: 0o700 })
    await writeFile(path, body, "utf8")
  }

  async function removeDraft(sessionId: string): Promise<void> {
    const path = draftFilePath(base, sessionId)
    try {
      await rm(path, { force: true })
    } catch (error: unknown) {
      if (!isMissingFileError(error)) {
        // Swallow — a corrupt filesystem must not break the chain.
      }
    }
  }

  async function listDrafts(maxAgeSeconds: number, now: number): Promise<readonly string[]> {
    const dir = join(base, DRAFTS_SUBDIR)
    let names: readonly string[]
    try {
      names = await readdir(dir)
    } catch (error: unknown) {
      if (isMissingFileError(error)) {
        return []
      }
      return []
    }
    const stale: string[] = []
    for (const name of names) {
      if (!name.endsWith(".txt")) {
        continue
      }
      const sessionId = name.slice(0, -".txt".length)
      if (sessionId.length === 0) {
        continue
      }
      const path = draftFilePath(base, sessionId)
      let info
      try {
        info = await stat(path)
      } catch {
        continue
      }
      const ageSeconds = Math.floor((now - info.mtimeMs) / 1000)
      if (ageSeconds >= maxAgeSeconds) {
        stale.push(sessionId)
      }
    }
    return stale
  }

  function enqueue(operation: () => Promise<void>): Promise<void> {
    const next = chain.then(operation)
    chain = next.catch(() => undefined)
    return next
  }

  return {
    async get(sessionId) {
      return readDraft(sessionId)
    },
    set(sessionId, body) {
      return enqueue(() => writeDraft(sessionId, body))
    },
    clear(sessionId) {
      return enqueue(() => removeDraft(sessionId))
    },
    async listStale(maxAgeSeconds, now) {
      return listDrafts(maxAgeSeconds, now ?? Date.now())
    },
    async whenIdle() {
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

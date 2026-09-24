/**
 * `comuki archive` — list files in `~/.config/comuki/archive/`
 * (no Ink, no host). `comuki archive save <sessionId>` /
 * `comuki archive save --current` writes a session transcript to the
 * same directory and prints its path. The list path needs no config;
 * the save path needs `loadConfig` (a `ComukiClient`) and the live
 * `~/.config/comuki/sessions.json`.
 */
import { EmptyArchiveError, saveArchiveFile } from "../lib/archive"
import { archiveDir, sessionsFilePath } from "../lib/config"
import { formatArchiveList, listArchiveFiles } from "../lib/archive"
import { ComukiClient } from "../lib/client"
import type { ResolvedConfig } from "../lib/config"
import { describeError } from "./chat"
import { fromPersisted, PENDING_PREFIX, readSessionsFile } from "../lib/sessions"

export async function printArchiveList(
  dir: string = archiveDir()
): Promise<void> {
  const listings = await listArchiveFiles(dir)
  process.stdout.write(formatArchiveList(listings))
}

export interface ArchiveSaveArgs {
  readonly sessionId?: string
  readonly current: boolean
}

interface ResolvedTarget {
  readonly sessionId: string
  readonly sessionName: string
}

interface UnresolvedTarget {
  readonly error: string
}

/**
 * Pick the session the `save` action will archive. `--current` wins
 * over an explicit id (documented in the `--current` describe string).
 * The explicit-id path treats the persisted `sessions.json` as a
 * best-effort friendly name lookup only — a session id the CLI has
 * never seen locally still archives via the host, with an empty
 * `sessionName` (which `archiveFileName` turns into the `session`
 * fallback, not an error).
 */
export async function resolveArchiveTarget(
  args: ArchiveSaveArgs,
  persistPath: string
): Promise<ResolvedTarget | UnresolvedTarget> {
  if (args.current) {
    const persisted = await readSessionsFile(persistPath)
    const restored = fromPersisted(persisted)
    const active = restored.sessions[restored.activeIndex]
    if (!active) {
      return { error: "no active session — pass a <sessionId> instead" }
    }
    if (active.id.startsWith(PENDING_PREFIX)) {
      return {
        error: `active session '${active.id}' has no server id yet — send a message first, or pass a <sessionId>`,
      }
    }
    return { sessionId: active.id, sessionName: active.name }
  }
  if (args.sessionId === undefined) {
    return {
      error: "usage: comuki archive save <sessionId> | --current",
    }
  }
  const persisted = await readSessionsFile(persistPath)
  const restored = fromPersisted(persisted)
  return {
    sessionId: args.sessionId,
    sessionName:
      restored.sessions.find((session) => session.id === args.sessionId)?.name ??
      "",
  }
}

/**
 * Glue for the argv writer: resolve the target, build a fresh
 * `ComukiClient`, write the file, print the path. Returns the process
 * exit code — `0` on success, `2` on usage / no-active-session errors,
 * `1` on everything else.
 */
export async function runArchiveSave(
  config: ResolvedConfig,
  args: ArchiveSaveArgs
): Promise<number> {
  const target = await resolveArchiveTarget(args, sessionsFilePath())
  if ("error" in target) {
    process.stderr.write(`comuki: ${target.error}\n`)
    return 2
  }
  try {
    const path = await saveArchiveFile({
      client: new ComukiClient(config),
      sessionId: target.sessionId,
      sessionName: target.sessionName,
    })
    process.stdout.write(`${path}\n`)
    return 0
  } catch (error) {
    if (error instanceof EmptyArchiveError) {
      process.stderr.write(`comuki: ${error.message}\n`)
      return 1
    }
    process.stderr.write(
      `comuki: archive save failed: ${describeError(error)}\n`
    )
    return 1
  }
}

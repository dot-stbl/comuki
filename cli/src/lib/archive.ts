/**
 * Session archive: `comuki archive` lists markdown transcripts under
 * `~/.config/comuki/archive/` (read-only, no host). `comuki archive
 * save <sessionId>` (or `--current`) writes the same kind of file from
 * the host's transcript — it pages through `listMessages`, renders with
 * `exportMarkdown`, and lands in the directory `listArchiveFiles` already
 * knows how to read. Path building is pure so tests never touch disk
 * beyond the injected directory.
 */
import { readdir, stat } from "node:fs/promises"
import { join } from "node:path"
import { archiveDir } from "./config"
import type { ChatMessageView } from "./client"
import { exportMarkdown, sessionSlug } from "./export"
import { formatBytes } from "./kb"
import type { ChatBlock } from "./sessions"

/** `{id}-{slug}-{yyyymmdd}.md` — id is sanitised, slug from the tab name. */
export function archiveFileName(
  id: string,
  sessionName: string,
  now: Date = new Date()
): string {
  const safeId =
    id.replace(/[^\p{L}\p{N}_.-]+/gu, "-").replace(/^-+|-+$/g, "") || "session"
  const pad = (value: number) => String(value).padStart(2, "0")
  const date = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`
  return `${safeId}-${sessionSlug(sessionName)}-${date}.md`
}

export function archiveFilePath(
  id: string,
  sessionName: string,
  now: Date = new Date(),
  dir: string = archiveDir()
): string {
  return join(dir, archiveFileName(id, sessionName, now))
}

export interface ArchiveListing {
  readonly name: string
  readonly size: number
  readonly mtimeMs: number
}

/** Newest first; directories skipped. Missing dir → empty list. */
export async function listArchiveFiles(
  dir: string = archiveDir()
): Promise<readonly ArchiveListing[]> {
  let names: string[]
  try {
    names = await readdir(dir)
  } catch {
    return []
  }
  const listings: ArchiveListing[] = []
  for (const name of names) {
    try {
      const info = await stat(join(dir, name))
      if (!info.isFile()) {
        continue
      }
      listings.push({ name, size: info.size, mtimeMs: info.mtimeMs })
    } catch {
      // A file that vanished between readdir and stat is skipped.
    }
  }
  return listings.sort((left, right) => right.mtimeMs - left.mtimeMs)
}

function formatMtime(mtimeMs: number): string {
  const date = new Date(mtimeMs)
  const pad = (value: number) => String(value).padStart(2, "0")
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** Plain-console table: name, size, mtime. Empty → a one-line hint. */
export function formatArchiveList(
  listings: readonly ArchiveListing[]
): string {
  if (listings.length === 0) {
    return "no archives yet\n"
  }
  const nameWidth = Math.max(4, ...listings.map((item) => item.name.length))
  const sizeWidth = Math.max(
    4,
    ...listings.map((item) => formatBytes(item.size).length)
  )
  const header = `${"name".padEnd(nameWidth)}  ${"size".padStart(sizeWidth)}  mtime`
  return [
    header,
    ...listings.map((item) => {
      const size = formatBytes(item.size).padStart(sizeWidth)
      return `${item.name.padEnd(nameWidth)}  ${size}  ${formatMtime(item.mtimeMs)}`
    }),
    "",
  ].join("\n")
}

/**
 * Minimal slice of `ComukiClient` the writer needs. Real `ComukiClient`
 * satisfies it structurally — tests pass a fake.
 */
export interface ArchiveMessagesClient {
  listMessages(
    sessionId: string,
    page?: number,
    pageSize?: number
  ): Promise<{
    readonly items: readonly ChatMessageView[]
    readonly total: number | string
  }>
}

/**
 * Thrown when the host's transcript for a session is empty — no file is
 * written in that case (mirrors the removed `/archive` slash command).
 */
export class EmptyArchiveError extends Error {
  constructor(readonly sessionId: string) {
    super(`nothing to archive — session ${sessionId} has no transcript`)
    this.name = "EmptyArchiveError"
  }
}

const ARCHIVE_PAGE_SIZE = 50

/**
 * Walk `listMessages` page-by-page and concatenate items in chronological
 * order (oldest first, matching the wire shape's own ordering). Exposed
 * for testing — callers usually want `saveArchiveFile`.
 */
export async function fetchArchiveMessages(
  client: ArchiveMessagesClient,
  sessionId: string
): Promise<readonly ChatMessageView[]> {
  const first = await client.listMessages(sessionId, 1, ARCHIVE_PAGE_SIZE)
  const total = Number(first.total)
  const pageCount = Math.max(1, Math.ceil(total / ARCHIVE_PAGE_SIZE))
  const collected: ChatMessageView[] = [...first.items]
  for (let page = 2; page <= pageCount; page += 1) {
    const next = await client.listMessages(sessionId, page, ARCHIVE_PAGE_SIZE)
    collected.push(...next.items)
  }
  return collected
}

export interface SaveArchiveFileParams {
  readonly client: ArchiveMessagesClient
  readonly sessionId: string
  readonly sessionName: string
  readonly now?: Date
  readonly dir?: string
}

/**
 * Fetch the whole transcript, render it to markdown, write it under
 * `dir` (default `archiveDir()`) using `archiveFilePath` for the name,
 * and return the written path. Empty transcripts reject with
 * `EmptyArchiveError` and never touch disk.
 */
export async function saveArchiveFile(
  params: SaveArchiveFileParams
): Promise<string> {
  const messages = await fetchArchiveMessages(params.client, params.sessionId)
  if (messages.length === 0) {
    throw new EmptyArchiveError(params.sessionId)
  }
  const blocks: ChatBlock[] = messages.map((message, index) => ({
    kind: "message",
    key: `${params.sessionId}-${index}`,
    message,
  }))
  const markdown = exportMarkdown(blocks)
  if (markdown.length === 0) {
    throw new EmptyArchiveError(params.sessionId)
  }
  const path = archiveFilePath(
    params.sessionId,
    params.sessionName,
    params.now ?? new Date(),
    params.dir ?? archiveDir()
  )
  await Bun.write(path, markdown, { createPath: true })
  return path
}

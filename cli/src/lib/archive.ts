/**
 * Session archive: `/archive` writes a markdown transcript under
 * `~/.config/comuki/archive/{id}-{slug}-{date}.md`; `comuki archive`
 * lists those files. Path building is pure so tests never touch disk.
 */
import { readdir, stat } from "node:fs/promises"
import { join } from "node:path"
import { archiveDir } from "./config"
import { sessionSlug } from "./export"
import { formatBytes } from "./kb"

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
  const rows = listings.map((item) => {
    const size = formatBytes(item.size).padStart(sizeWidth)
    return `${item.name.padEnd(nameWidth)}  ${size}  ${formatMtime(item.mtimeMs)}`
  })
  return [header, ...rows, ""].join("\n")
}

/**
 * `comuki archive` — list files in `~/.config/comuki/archive/`.
 * No Ink, no host: a directory listing to stdout.
 */
import { archiveDir } from "../lib/config"
import { formatArchiveList, listArchiveFiles } from "../lib/archive"

export async function printArchiveList(
  dir: string = archiveDir()
): Promise<void> {
  const listings = await listArchiveFiles(dir)
  process.stdout.write(formatArchiveList(listings))
}

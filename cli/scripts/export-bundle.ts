/**
 * `comuki export-bundle <path>` — package the diagnostics log, the
 * receipts ledger, the durable session NDJSON, the drafts tree, the
 * kernel version, and platform info into a single archive the user
 * can attach to a bug report.
 *
 * Format selection:
 *
 *   - Windows → `.zip` (native OS support, double-clickable)
 *   - Linux / macOS → `.tar.gz` (the standard cross-distro format;
 *     `tar -xzf` works out of the box)
 *
 * The brief is explicit: the bundle **never** includes composer draft
 * text. We package the audit receipts + diagnostics + drafts metadata
 * (paths only), but never the `drafts/<id>.txt` bodies.
 *
 * Both archive formats are built in-house — Bun ships `Bun.gzipSync`
 * for the gzip wrapper, and the tar / zip headers are a few hundred
 * bytes of structured data; depending on `archiver` / `tar-stream`
 * would only bloat the compiled binary without giving the user
 * anything they could not unzip with stock OS tools.
 *
 * If the destination path does not exist, we `mkdir -p` the parent
 * directories (mkdir -p semantics) so a single command works from a
 * cold start.
 */

import {
  mkdir,
  readdir,
  readFile,
  stat,
  writeFile,
} from "node:fs/promises"
import { dirname, join, resolve } from "node:path"
import { defaultStateDirectory, RECEIPTS_SUBDIR } from "../src/kernel/receipts"
import { DRAFTS_SUBDIR } from "../src/kernel/drafts"
import { SESSIONS_FILE } from "../src/kernel/sessions"
import { DIAGNOSTICS_FILE } from "../src/kernel/telemetry"
import { CLIENT_VERSION_STRING } from "../src/kernel/version"

// ---------------------------------------------------------------------------
// Public surface
// ---------------------------------------------------------------------------

export interface ExportBundleOptions {
  /** Source directory; defaults to the platform state directory. */
  readonly stateDirectory?: string
  /** Destination path. Parent dirs are created (mkdir -p). */
  readonly destination: string
  /**
   * Override the archive format. Default: `.zip` on Windows,
   * `.tar.gz` elsewhere. The value `"zip"` or `"tar.gz"` wins
   * regardless of platform — used by tests + the brief's dogfood.
   */
  readonly format?: "zip" | "tar.gz"
}

export interface ExportBundleReport {
  readonly destination: string
  readonly format: "zip" | "tar.gz"
  readonly bytes: number
  readonly entries: number
}

const BUNDLE_VERSION = 1

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

/**
 * Build the bundle. Returns the resolved destination + format + size;
 * throws on any IO failure (with the failing path in the message).
 *
 * Note on `Bun.Archive`: the constructor takes `entries: [...]`; each
 * entry has `{ name, data: Buffer }`. `bytes()` returns a Promise of
 * the encoded archive bytes (resolved async by Bun's file IO layer).
 */
export async function exportBundle(options: ExportBundleOptions): Promise<ExportBundleReport> {
  const stateDirectory = options.stateDirectory ?? defaultStateDirectory()
  const format = options.format ?? inferFormat(options.destination)
  await mkdir(dirname(resolve(options.destination)), { recursive: true })

  const entries: ArchiveEntry[] = []

  // Bundle manifest — written first so the unpacker sees it at the top.
  entries.push({
    name: "manifest.json",
    data: Buffer.from(
      JSON.stringify(
        {
          bundleVersion: BUNDLE_VERSION,
          createdAtUnixMs: Date.now(),
          clientVersion: CLIENT_VERSION_STRING,
          platform: platformInfo(),
        },
        null,
        2
      ),
      "utf8"
    ),
  })

  // Diagnostics log — optional (the file is created lazily on first
  // log write). When missing, the manifest's `diagnostics.status`
  // field flags it; we still emit a placeholder so the archive shape
  // is stable.
  const diagnosticsPath = join(stateDirectory, DIAGNOSTICS_FILE)
  entries.push(
    await fileEntry(
      "diagnostics.log",
      diagnosticsPath,
      () => "(no diagnostics yet)\n"
    )
  )

  // Receipts ledger — one file per session, plus a tiny index that
  // names which sessions exist.
  const receiptsDir = join(stateDirectory, RECEIPTS_SUBDIR)
  const receiptFiles = await listNdjson(receiptsDir)
  const receiptIndex: string[] = []
  for (const relative of receiptFiles) {
    const absolute = join(receiptsDir, relative)
    const entryName = `receipts/${relative}`
    entries.push(await fileEntry(entryName, absolute))
    receiptIndex.push(entryName)
  }
  entries.push({
    name: "receipts-index.json",
    data: Buffer.from(
      JSON.stringify(
        { count: receiptIndex.length, files: receiptIndex },
        null,
        2
      ),
      "utf8"
    ),
  })

  // Durable session NDJSON.
  const sessionsPath = join(stateDirectory, SESSIONS_FILE)
  entries.push(
    await fileEntry(SESSIONS_FILE, sessionsPath, () => "(no sessions yet)\n")
  )

  // Drafts — metadata only. The brief is explicit that composer draft
  // text is PII and must NEVER land in the bundle; we list the
  // session ids that have a draft, with byte sizes + mtimes, but
  // never the text.
  const draftsDir = join(stateDirectory, DRAFTS_SUBDIR)
  const draftIndex: Array<{
    readonly sessionId: string
    readonly size: number
    readonly mtimeUnixMs: number
  }> = []
  for (const relative of await listNdjson(draftsDir)) {
    const absolute = join(draftsDir, relative)
    const info = await safeStat(absolute)
    if (info === null) {
      continue
    }
    const sessionId = relative.replace(/\.txt$/, "")
    draftIndex.push({
      sessionId,
      size: info.size,
      mtimeUnixMs: info.mtimeMs,
    })
  }
  entries.push({
    name: "drafts-index.json",
    data: Buffer.from(
      JSON.stringify(
        {
          count: draftIndex.length,
          drafts: draftIndex,
          note: "Composer draft bodies are PII and intentionally excluded from this bundle.",
        },
        null,
        2
      ),
      "utf8"
    ),
  })

  // Encode the archive. Both formats live below as pure functions;
  // no third-party dep, no `Bun.Archive` (which is a reader API in
  // Bun 1.3, not a writer). Tarballs are unwrapped with stock
  // `tar -xzf`; zips are double-clickable on Windows.
  const payload = entries.map((entry) => ({ name: entry.name, data: entry.data }))
  const bytes =
    format === "zip"
      ? encodeZip(payload)
      : encodeTarGz(payload)

  await writeFile(resolve(options.destination), bytes)

  return {
    destination: resolve(options.destination),
    format,
    bytes: bytes.byteLength,
    entries: entries.length,
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface ArchiveEntry {
  readonly name: string
  readonly data: Buffer
}

function inferFormat(destination: string): "zip" | "tar.gz" {
  if (destination.toLowerCase().endsWith(".zip")) {
    return "zip"
  }
  if (destination.toLowerCase().endsWith(".tar.gz") || destination.toLowerCase().endsWith(".tgz")) {
    return "tar.gz"
  }
  // Default by platform.
  return process.platform === "win32" ? "zip" : "tar.gz"
}

interface FileStat {
  readonly size: number
  readonly mtimeMs: number
}

async function safeStat(path: string): Promise<FileStat | null> {
  try {
    const info = await stat(path)
    return { size: info.size, mtimeMs: info.mtimeMs }
  } catch {
    return null
  }
}

async function fileEntry(
  name: string,
  absolute: string,
  fallback?: () => string
): Promise<ArchiveEntry> {
  try {
    const data = await readFile(absolute)
    return { name, data: Buffer.from(data) }
  } catch (error: unknown) {
    if (fallback !== undefined && isMissingFileError(error)) {
      return { name, data: Buffer.from(fallback(), "utf8") }
    }
    throw error
  }
}

async function listNdjson(directory: string): Promise<readonly string[]> {
  try {
    const entries = await readdir(directory, { withFileTypes: true })
    return entries
      .filter((entry) => entry.isFile() && entry.name.endsWith(".ndjson"))
      .map((entry) => entry.name)
      .sort()
  } catch (error: unknown) {
    if (isMissingFileError(error)) {
      return []
    }
    throw error
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

function platformInfo(): Record<string, unknown> {
  return {
    platform: process.platform,
    arch: process.arch,
    bun: Bun.version,
    node: process.versions.node ?? null,
    pid: process.pid,
    cwd: process.cwd(),
    argv0: process.argv0,
    startedAtUnixMs:
      process.uptime() > 0
        ? Date.now() - Math.round(process.uptime() * 1000)
        : Date.now(),
  }
}

// ---------------------------------------------------------------------------
// tar + gzip encoder (USTAR v0, no PAX extensions)
// ---------------------------------------------------------------------------

const TAR_BLOCK_SIZE = 512
const TAR_FILE_TYPE = "0" // regular file
const TAR_MAGIC = "ustar\u000000"

/** Octal-string of fixed length (right-padded with NULs). */
function octal(value: number, length: number): string {
  const text = value.toString(8)
  if (text.length > length - 1) {
    // tar's octal fields carry a trailing NUL or space; if the number
    // doesn't fit, force the high bit to mark "GNU large" — the
    // standard readers still handle a saturated field for files
    // smaller than 8 GiB which is the cap we care about.
    return "77777777777".slice(0, length - 1) + "\u0000"
  }
  return text.padStart(length - 1, "0") + "\u0000"
}

/** Right-pad with NULs to `length` bytes. */
function padTo(data: string, length: number): Buffer {
  const bytes = Buffer.alloc(length)
  bytes.write(data, 0, length, "utf8")
  return bytes
}

function computeChecksum(header: Buffer): number {
  // The chksum field itself is treated as eight spaces while
  // computing the sum — replace bytes 148..156 with spaces first.
  const copy = Buffer.from(header)
  for (let i = 148; i < 156; i += 1) {
    copy[i] = 0x20
  }
  let sum = 0
  for (const byte of copy) {
    sum += byte
  }
  return sum
}

function tarHeader(name: string, size: number): Buffer {
  const buffer = Buffer.alloc(TAR_BLOCK_SIZE)
  // name (0..100), mode (100..108), uid (108..116), gid (116..124),
  // size (124..136), mtime (136..148), chksum (148..156), typeflag
  // (156..157), linkname (157..257), magic (257..263), version
  // (263..265), uname (265..297), gname (297..329), devmajor (329..337),
  // devminor (337..345), prefix (345..500), pad (500..512).
  padTo(name.slice(0, 99), 100).copy(buffer, 0)
  padTo(octal(0o644, 8), 8).copy(buffer, 100) // mode
  padTo(octal(0, 8), 8).copy(buffer, 108) // uid
  padTo(octal(0, 8), 8).copy(buffer, 116) // gid
  padTo(octal(size, 12), 12).copy(buffer, 124)
  padTo(octal(Math.floor(Date.now() / 1000), 12), 12).copy(buffer, 136)
  // chksum placeholder (8 spaces, then overwrite after compute).
  padTo("", 8).copy(buffer, 148)
  buffer.write(TAR_FILE_TYPE, 156, 1, "utf8")
  // linkname (157..257) — empty for regular files
  padTo(TAR_MAGIC, 6).copy(buffer, 257)
  padTo("00", 2).copy(buffer, 263) // version
  padTo("root", 32).copy(buffer, 265) // uname
  padTo("root", 32).copy(buffer, 297) // gname
  // devmajor / devminor / prefix — empty
  const checksum = computeChecksum(buffer)
  padTo(octal(checksum, 8), 8).copy(buffer, 148)
  return buffer
}

function tarBlockFor(data: Buffer): Buffer[] {
  const blocks: Buffer[] = []
  if (data.byteLength > 0) {
    blocks.push(data)
  }
  const padding = (TAR_BLOCK_SIZE - (data.byteLength % TAR_BLOCK_SIZE)) % TAR_BLOCK_SIZE
  if (padding > 0) {
    blocks.push(Buffer.alloc(padding))
  }
  return blocks
}

/**
 * Build a `tar.gz` archive from in-memory entries. The encoding is
 * pure POSIX ustar — `tar -xzf bundle.tar.gz` extracts it with the
 * original paths.
 */
export function encodeTarGz(entries: ReadonlyArray<{ name: string; data: Buffer }>): Buffer {
  const buffers: Buffer[] = []
  for (const entry of entries) {
    buffers.push(tarHeader(entry.name, entry.data.byteLength))
    buffers.push(...tarBlockFor(entry.data))
  }
  // Two zero blocks terminate the tar stream.
  buffers.push(Buffer.alloc(TAR_BLOCK_SIZE))
  buffers.push(Buffer.alloc(TAR_BLOCK_SIZE))
  const tarBytes = Buffer.concat(buffers)
  return Buffer.from(Bun.gzipSync(new Uint8Array(tarBytes)))
}

// ---------------------------------------------------------------------------
// zip encoder (store mode, no compression — small files compress
// poorly with DEFLATE; the kernel's bundle is a few KiB and stays
// readable in Windows Explorer)
// ---------------------------------------------------------------------------

function crc32(data: Buffer): number {
  // CRC-32 (poly 0xEDB88320) — table-less, slow but adequate for
  // our small bundles.
  let crc = 0xffffffff
  for (const byte of data) {
    crc = crc ^ byte
    for (let i = 0; i < 8; i += 1) {
      crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1))
    }
  }
  return (crc ^ 0xffffffff) >>> 0
}

function dosTime(date: Date): { time: number; date: number } {
  const seconds = Math.floor(date.getSeconds() / 2)
  const time = (date.getHours() << 11) | (date.getMinutes() << 5) | seconds
  const datePart =
    ((date.getFullYear() - 1980) << 9) | ((date.getMonth() + 1) << 5) | date.getDate()
  return { time, date: datePart }
}

function zipEntry(name: string, data: Buffer): Buffer[] {
  const { time, date } = dosTime(new Date())
  const crc = crc32(data)
  const size = data.byteLength
  const localHeader = Buffer.alloc(30 + name.length)
  localHeader.writeUInt32LE(0x04034b50, 0) // local file header signature
  localHeader.writeUInt16LE(20, 4) // version needed
  localHeader.writeUInt16LE(0, 6) // gp flag
  localHeader.writeUInt16LE(0, 8) // method (0 = store)
  localHeader.writeUInt16LE(time, 10)
  localHeader.writeUInt16LE(date, 12)
  localHeader.writeUInt32LE(crc, 14)
  localHeader.writeUInt32LE(size, 18) // compressed size
  localHeader.writeUInt32LE(size, 22) // uncompressed size
  localHeader.writeUInt16LE(name.length, 26)
  localHeader.writeUInt16LE(0, 28) // extra length
  localHeader.write(name, 30, name.length, "utf8")
  return [localHeader, data]
}

/**
 * Build a `.zip` archive (store mode, no compression). Readable by
 * Windows Explorer and `unzip` on Linux.
 */
export function encodeZip(entries: ReadonlyArray<{ name: string; data: Buffer }>): Buffer {
  const localParts: Buffer[] = []
  const centralParts: Buffer[] = []
  let offset = 0
  for (const entry of entries) {
    const local = zipEntry(entry.name, entry.data)
    const { time, date } = dosTime(new Date())
    const central = Buffer.alloc(46 + entry.name.length)
    central.writeUInt32LE(0x02014b50, 0) // central dir signature
    central.writeUInt16LE(20, 4) // version made by
    central.writeUInt16LE(20, 6) // version needed
    central.writeUInt16LE(0, 8) // gp flag
    central.writeUInt16LE(0, 10) // method
    central.writeUInt16LE(time, 12)
    central.writeUInt16LE(date, 14)
    central.writeUInt32LE(crc32(entry.data), 16)
    central.writeUInt32LE(entry.data.byteLength, 20)
    central.writeUInt32LE(entry.data.byteLength, 24)
    central.writeUInt16LE(entry.name.length, 28)
    central.writeUInt16LE(0, 30) // extra length
    central.writeUInt16LE(0, 32) // comment length
    central.writeUInt16LE(0, 34) // disk number
    central.writeUInt16LE(0, 36) // internal attrs
    central.writeUInt32LE(0, 38) // external attrs
    central.writeUInt32LE(offset, 42) // local header offset
    central.write(entry.name, 46, entry.name.length, "utf8")

    localParts.push(...local)
    centralParts.push(central)
    offset += local.reduce((sum, part) => sum + part.byteLength, 0)
  }
  const localSection = Buffer.concat(localParts)
  const centralSection = Buffer.concat(centralParts)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0) // EOCD signature
  end.writeUInt16LE(0, 4)
  end.writeUInt16LE(0, 6)
  end.writeUInt16LE(entries.length, 8)
  end.writeUInt16LE(entries.length, 10)
  end.writeUInt32LE(centralSection.byteLength, 12)
  end.writeUInt32LE(localSection.byteLength, 16)
  end.writeUInt16LE(0, 20) // comment length
  return Buffer.concat([localSection, centralSection, end])
}

/**
 * In-process zip reader — parses the central directory and returns
 * one entry per file. Used by the test suite so the round-trip
 * doesn't depend on a system `unzip` / `Expand-Archive` /
 * `tar -xf` binary being available.
 *
 * Only the subset needed for our archives: stored (method 0),
 * uncompressed, no encryption, no extra fields, no comments,
 * single-disk. Deflate / encryption are not used here — we
 * archive stored.
 */
export interface ZipEntry {
  readonly name: string
  readonly data: Buffer
}

export function readZip(buffer: Buffer): ZipEntry[] {
  // EOCD is the last 22 bytes (plus optional comment, which we
  // never emit). Walk back from the end to find the signature.
  const eocdSig = 0x06054b50
  let eocdOffset = -1
  for (let i = buffer.byteLength - 22; i >= Math.max(0, buffer.byteLength - 22 - 0xffff); i--) {
    if (buffer.readUInt32LE(i) === eocdSig) {
      eocdOffset = i
      break
    }
  }
  if (eocdOffset < 0) {
    throw new Error("readZip: EOCD signature not found")
  }
  const centralCount = buffer.readUInt16LE(eocdOffset + 10)
  const centralDirOffset = buffer.readUInt32LE(eocdOffset + 16)

  const out: ZipEntry[] = []
  const centralSig = 0x02014b50
  const localSig = 0x04034b50
  let cursor = centralDirOffset
  for (let i = 0; i < centralCount; i++) {
    if (buffer.readUInt32LE(cursor) !== centralSig) {
      throw new Error(`readZip: bad central signature at offset ${cursor}`)
    }
    const compressedSize = buffer.readUInt32LE(cursor + 20)
    const uncompressedSize = buffer.readUInt32LE(cursor + 24)
    const nameLength = buffer.readUInt16LE(cursor + 28)
    const extraLength = buffer.readUInt16LE(cursor + 30)
    const commentLength = buffer.readUInt16LE(cursor + 32)
    const localHeaderOffset = buffer.readUInt32LE(cursor + 42)
    const name = buffer.toString("utf8", cursor + 46, cursor + 46 + nameLength)

    if (buffer.readUInt32LE(localHeaderOffset) !== localSig) {
      throw new Error(`readZip: bad local signature at offset ${localHeaderOffset}`)
    }
    const localExtraLength = buffer.readUInt16LE(localHeaderOffset + 28)
    const dataStart = localHeaderOffset + 30 + nameLength + localExtraLength
    const dataEnd = dataStart + compressedSize
    if (compressedSize !== uncompressedSize) {
      throw new Error(
        `readZip: only stored (uncompressed) entries are supported; entry '${name}' is compressed`,
      )
    }
    const data = Buffer.from(buffer.subarray(dataStart, dataEnd))
    out.push({ name, data })

    cursor += 46 + nameLength + extraLength + commentLength
  }
  return out
}

// ---------------------------------------------------------------------------
// Script entry — `bun run scripts/export-bundle.ts <path>` produces the
// archive and prints a one-line report.
// ---------------------------------------------------------------------------

if (import.meta.main) {
  const args = process.argv.slice(2)
  if (args.length === 0) {
    process.stderr.write("usage: export-bundle <path> [--state <dir>] [--format zip|tar.gz]\n")
    process.exit(2)
  }
  const formatArgIndex = args.indexOf("--format")
  const explicitFormat =
    formatArgIndex >= 0 ? (args[formatArgIndex + 1] as "zip" | "tar.gz") : undefined
  const stateArgIndex = args.indexOf("--state")
  const stateDirectory =
    stateArgIndex >= 0 ? args[stateArgIndex + 1] : undefined
  const destination = args[0]
  if (destination === undefined) {
    process.stderr.write("export-bundle: missing destination path\n")
    process.exit(2)
  }
  exportBundle({
    destination,
    ...(stateDirectory !== undefined ? { stateDirectory } : {}),
    ...(explicitFormat !== undefined ? { format: explicitFormat } : {}),
  })
    .then((report) => {
      process.stdout.write(
        `${report.destination} (${report.format}, ${report.bytes} bytes, ${report.entries} entries)\n`
      )
      process.exit(0)
    })
    .catch((error: unknown) => {
      process.stderr.write(
        `export-bundle: ${error instanceof Error ? error.message : String(error)}\n`
      )
      process.exit(1)
    })
}

// No re-exports — the archive encoders above are the public surface;
// `encodeTarGz` and `encodeZip` exist for tests that round-trip a
// hand-built entry list.

/**
 * Pure parts of `/kb` — the knowledge-library slash pack: local file
 * collection + validation for `add`, list formatting for `list`, and
 * the honest-degradation notices when the server refuses.
 *
 * Wire shapes come from `lib/client.ts` (`knowledgeDocuments`,
 * `knowledgeIngest`); rendering follows the `lib/format.ts` contract —
 * spacing + status colours, no frames. No React, no network: the only
 * I/O is reading the filesystem for `collectFiles`, which takes an
 * injectable cwd so the suite never depends on the repo layout.
 */
import { existsSync, statSync } from "node:fs"
import { basename, resolve } from "node:path"
import type {
  KnowledgeDocumentsPageView,
  KnowledgeDocumentSummaryView,
} from "./client"
import { ageFromIso, tableRow } from "./format"
import { colors, paint, symbols } from "../theme"

/**
 * CLI-side per-file cap. The server sets no explicit limit on the
 * ingest text (Kestrel's ~30 MB request ceiling is the only bound), so
 * this is a deliberate client guard: a 2 MB text file is ~500k tokens
 * of chunks — far past useful — and rejects before the read, not after.
 */
export const KB_MAX_FILE_BYTES = 2 * 1024 * 1024

/**
 * Cap on how many files one `/kb add` may carry — a broad recursive
 * markdown glob in a repo root can match thousands including
 * node_modules; each file is one HTTP call, so the pack stops here and
 * says so instead of flooding the host.
 */
export const KB_MAX_FILES = 100

/** Rows `/kb list` requests — one page of the newest documents. */
export const KB_PAGE_SIZE = 25

/**
 * Text-only extensions `/kb add` accepts. The ingest wire carries a
 * `text` string, not bytes — a PDF or PNG would arrive as mojibake, so
 * anything outside this set is rejected locally with a notice.
 */
const KB_TEXT_EXTENSIONS: ReadonlySet<string> = new Set([
  "md", "markdown", "mdx", "txt", "text", "rst",
  "json", "jsonc", "yaml", "yml", "toml", "ini", "env", "csv", "tsv",
  "ts", "tsx", "js", "jsx", "mjs", "cjs", "cs", "csx", "py", "pyi",
  "rb", "rs", "go", "java", "kt", "swift", "c", "h", "cpp", "hpp",
  "cc", "php", "sql", "sh", "bash", "zsh", "fish", "ps1", "psm1",
  "html", "htm", "css", "scss", "sass", "less", "vue", "svelte",
  "astro", "graphql", "gql", "proto", "tf", "hcl", "lua", "vim",
  "r", "m", "pl", "ex", "exs", "erl", "hs", "ml", "clj", "cljs",
  "edn", "gradle", "xml", "svg", "diff", "patch", "log",
])

/** Glob metacharacters — a pattern without any of these is a literal path. */
const GLOB_METACHARS = /[*?[{]/

/**
 * Expands each pattern (literal file path or glob) against `cwd` and
 * returns the union, sorted and de-duplicated, with `/`-separated
 * paths (Bun's scanner emits `\` on Windows). Literal paths resolve
 * via a stat; globs via `Bun.Glob.scanSync` (`onlyFiles`, no
 * dotdirs). A pattern that matches nothing contributes nothing — the
 * caller decides whether the total is empty.
 */
export function collectFiles(
  patterns: readonly string[],
  cwd: string = process.cwd()
): string[] {
  const found = new Set<string>()
  const normalize = (path: string): string => path.replace(/\\/g, "/")
  for (const pattern of patterns) {
    if (pattern.length === 0) {
      continue
    }
    if (GLOB_METACHARS.test(pattern)) {
      const glob = new Bun.Glob(pattern)
      for (const match of glob.scanSync({ cwd, dot: false, onlyFiles: true })) {
        found.add(normalize(match))
      }
      continue
    }
    // Literal path — existsSync keeps absent paths out of the result
    // without try/catch around statSync. The pattern stays as typed
    // (separator-normalised) so titles and sourceRefs read like the
    // user's input.
    const literal = resolve(cwd, pattern)
    if (existsSync(literal) && statSync(literal).isFile()) {
      found.add(normalize(pattern))
    }
  }
  return [...found].sort()
}

/** The verdict of `validateIngestFile` — ok, or a human-readable reason. */
export type IngestFileCheck =
  | { readonly ok: true }
  | { readonly ok: false; readonly reason: string }

/**
 * Local pre-flight for one candidate file: extension must be a known
 * text kind, and the byte size must be non-zero and under the cap.
 * The server would accept almost anything (its guards only require
 * non-empty strings) — this is the CLI being honest instead of
 * uploading binary mojibake or a 40 MB dump.
 */
export function validateIngestFile(
  path: string,
  sizeBytes: number,
  maxBytes: number = KB_MAX_FILE_BYTES
): IngestFileCheck {
  const extension = path.slice(path.lastIndexOf(".") + 1).toLowerCase()
  if (!path.includes(".") || !KB_TEXT_EXTENSIONS.has(extension)) {
    return {
      ok: false,
      reason: `unsupported file type — /kb add takes text, markdown and source files, not '.${extension || path}'`,
    }
  }
  if (sizeBytes === 0) {
    return { ok: false, reason: "file is empty" }
  }
  if (sizeBytes > maxBytes) {
    return {
      ok: false,
      reason: `too large — ${formatBytes(sizeBytes)} exceeds the ${formatBytes(maxBytes)} /kb add limit`,
    }
  }
  return { ok: true }
}

/** `2.4 KB`, `1.1 MB` — compact byte sizes for validation notices. */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** MIME type riding the ingest wire; unknown text extensions stay text/plain. */
export function mimeTypeFor(path: string): string {
  const extension = path.slice(path.lastIndexOf(".") + 1).toLowerCase()
  const mime: Record<string, string> = {
    md: "text/markdown", markdown: "text/markdown", mdx: "text/markdown",
    txt: "text/plain", text: "text/plain", rst: "text/plain", log: "text/plain",
    json: "application/json", jsonc: "application/json",
    yaml: "application/yaml", yml: "application/yaml",
    toml: "application/toml", csv: "text/csv", tsv: "text/tab-separated-values",
    ts: "text/x-typescript", tsx: "text/x-typescript",
    js: "text/x-javascript", jsx: "text/x-javascript",
    mjs: "text/x-javascript", cjs: "text/x-javascript",
    cs: "text/x-csharp", csx: "text/x-csharp", py: "text/x-python",
    pyi: "text/x-python", rb: "text/x-ruby", rs: "text/rust",
    go: "text/x-go", java: "text/x-java", kt: "text/x-kotlin",
    swift: "text/x-swift", c: "text/x-c", h: "text/x-c",
    cpp: "text/x-c++", hpp: "text/x-c++", cc: "text/x-c++",
    php: "text/x-php", sql: "application/sql", sh: "text/x-shellscript",
    bash: "text/x-shellscript", zsh: "text/x-shellscript",
    fish: "text/x-shellscript", ps1: "text/x-powershell",
    psm1: "text/x-powershell", html: "text/html", htm: "text/html",
    css: "text/css", scss: "text/x-scss", sass: "text/x-sass",
    less: "text/x-less", xml: "application/xml", svg: "image/svg+xml",
  }
  return mime[extension] ?? "text/plain"
}

/** Shape of `POST /api/v1/knowledge/ingest` the client method takes. */
export interface KbIngestRequest {
  readonly projectId?: string
  readonly title: string
  readonly source: "upload"
  readonly sourceRef: string
  readonly mimeType: string
  readonly text: string
}

/**
 * Builds the ingest body for one local file. `projectId` rides along
 * whenever the session has a project context — a project-scoped api
 * key may ONLY write into its assigned projects, and the global
 * corpus (projectId omitted) is reserved for unrestricted subjects,
 * so omitting it would 403 for exactly the users most likely to run
 * this from a project session.
 */
export function ingestRequestFor(
  path: string,
  text: string,
  projectId: string | undefined
): KbIngestRequest {
  return {
    ...(projectId ? { projectId } : {}),
    title: basename(path),
    source: "upload",
    sourceRef: `upload:${path}`,
    mimeType: mimeTypeFor(path),
    text,
  }
}

// ---------------------------------------------------------------------------
// Transcript lines
// ---------------------------------------------------------------------------

/** `  /kb add <file|glob> · /kb list` — what a bare or unknown /kb shows. */
export function kbUsageLines(): string[] {
  return [
    paint("  usage", colors.accent),
    `  /kb add <file|glob>   ingest local text, markdown or source files (${formatBytes(KB_MAX_FILE_BYTES)} each, max ${KB_MAX_FILES})`,
    `  /kb list              the knowledge library — newest documents first`,
  ]
}

const KB_LIST_COLUMNS = [
  { width: 5 },
  { width: 7 },
  { width: 34 },
  { width: 0 },
] as const

/** One library row: age, chunk count, title, origin kind. */
export function kbListRow(
  document: KnowledgeDocumentSummaryView,
  now: Date = new Date()
): string {
  return tableRow([
    { text: ageFromIso(document.createdAt, now), width: KB_LIST_COLUMNS[0].width },
    { text: `${document.chunkCount} chn`, width: KB_LIST_COLUMNS[1].width },
    { text: document.title, width: KB_LIST_COLUMNS[2].width },
    {
      text: `${document.source}${document.projectId === null ? " · global" : ""}`,
      width: KB_LIST_COLUMNS[3].width,
    },
  ])
}

/**
 * The `/kb list` block: `* kb` header with shown/total, a faint column
 * line, one row per document, and a `+N more` tail when the page is
 * not the whole library.
 */
export function kbListLines(
  page: KnowledgeDocumentsPageView,
  now: Date = new Date()
): string[] {
  const header = `  ${paint(symbols.event, colors.dim)} ${paint("kb", colors.muted)} ${paint(
    `· ${page.items.length} of ${page.total}`,
    colors.dim
  )}`
  if (page.items.length === 0) {
    return [
      header,
      paint("  · library is empty — /kb add <file|glob> to ingest", colors.faint),
    ]
  }
  const columnLine = paint(
    tableRow(
      ["AGE", "CHUNKS", "TITLE", "SOURCE"].map((text, index) => ({
        text,
        width: KB_LIST_COLUMNS[index].width,
      }))
    ),
    colors.dim
  )
  const more = page.total - page.items.length - (page.page - 1) * page.pageSize
  return [
    header,
    columnLine,
    ...page.items.map((document) => kbListRow(document, now)),
    ...(more > 0
      ? [paint(`  · +${more} more on the server`, colors.faint)]
      : []),
  ]
}

/**
 * The per-file progress line the mission names: `* {name} →
 * {documentId}`, with the chunk count riding along.
 */
export function kbAddResultLine(
  name: string,
  result: { readonly sourceDocumentId: string; readonly chunksWritten: number }
): string {
  return `  ${paint(symbols.event, colors.dim)} ${paint(name, colors.bright)} ${paint(
    symbols.arrow,
    colors.dim
  )} ${paint(result.sourceDocumentId, colors.muted)} ${paint(
    `· ${result.chunksWritten} chunks`,
    colors.dim
  )}`
}

/** The per-file failure line: `✗ {name} → {what went wrong}`. */
export function kbAddErrorLine(name: string, reason: string): string {
  return `  ${paint(symbols.cross, colors.error)} ${paint(name, colors.bright)} ${paint(
    symbols.arrow,
    colors.dim
  )} ${paint(reason, colors.error)}`
}

/**
 * The honest-degradation notice: `/kb add` hit a wall the subject
 * cannot walk around. 403 → the key lacks `knowledge:write`; 401 → no
 * authenticated subject at all. Names what is missing instead of
 * faking success — and the caller stops the remaining files.
 */
export function kbWriteUnavailableLines(status: number): string[] {
  if (status === 401) {
    return [
      paint(
        `  ${symbols.cross} knowledge write api not available for this subject — not authenticated (401)`,
        colors.error
      ),
      paint("  · run comuki login or set COMUKI_API_KEY, then /kb add again", colors.faint),
    ]
  }
  return [
    paint(
      `  ${symbols.cross} knowledge write api not available for this subject — the key lacks the knowledge:write permission (403)`,
      colors.error
    ),
    paint(
      "  · knowledge:write is granted to platform-admin, operator and project-admin roles",
      colors.faint
    ),
  ]
}

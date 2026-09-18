/**
 * Structured diff rendering — a code part whose language is `diff` (or
 * whose body simply looks like a unified diff) renders as signed lines,
 * not a plain muted fence: additions in the deck's ok lavender,
 * deletions in error yellow, `@@` hunk headers in accent, context and
 * file-metadata lines recede. Pure — finished ANSI strings, no React.
 *
 * Detection is deliberately conservative: a bare `@@` in prose must not
 * flip a code block into diff mode, so content sniffing requires the
 * real unified-diff shapes (`diff --git`, `--- a/`, a well-formed
 * `@@ -n[,n] +m[,m] @@` hunk header) on line starts.
 *
 * Width: one diff line is ONE viewport row — a continuation wrap would
 * lose the `+`/`-` gutter alignment, so overlong lines ellipsize on the
 * tail instead (the marker and the code lead stay visible). Downstream
 * `wrapVisible` then has nothing left to wrap.
 */
import { colors, paint } from "../theme"
import { DEFAULT_MARKDOWN_WIDTH } from "./markdown"

/** A well-formed unified-diff hunk header: `@@ -3,7 +3,8 @@ optional …`. */
const HUNK_HEADER = /^@@ -\d+(?:,\d+)? \+\d+(?:,\d+)? @@/

/** File-level metadata lines: `diff --git`, `index`, `---`, `+++`, `new file mode`… . */
function isFileHeader(line: string): boolean {
  return (
    line.startsWith("diff --git ") ||
    line.startsWith("index ") ||
    line.startsWith("--- ") ||
    line.startsWith("+++ ") ||
    line.startsWith("new file mode") ||
    line.startsWith("deleted file mode") ||
    line.startsWith("old mode") ||
    line.startsWith("new mode") ||
    line.startsWith("rename from") ||
    line.startsWith("rename to") ||
    line.startsWith("similarity index") ||
    line.startsWith("copy from") ||
    line.startsWith("copy to") ||
    line.startsWith("Binary files")
  )
}

/**
 * True when a code block should render as a structured diff: the
 * language tag says `diff`, or the body carries the unmistakable
 * signatures of a unified diff. A `+`/`-`-leading line alone proves
 * nothing (any code can start lines with those).
 */
export function isDiffContent(
  lang: string | undefined | null,
  code: string
): boolean {
  if ((lang ?? "").trim().toLowerCase() === "diff") {
    return true
  }
  const lines = code.split("\n")
  const first = lines[0] ?? ""
  if (first.startsWith("diff --git ")) {
    return true
  }
  if (first.startsWith("--- a/")) {
    return true
  }
  return lines.some((line) => HUNK_HEADER.test(line))
}

/** Plain-text tail ellipsis: keeps the diff marker + code lead, drops the tail. */
function fitPlain(line: string, width: number): string {
  if (width <= 0) {
    return line
  }
  return line.length > width ? line.slice(0, Math.max(1, width - 1)) + "…" : line
}

/** One diff line → its painted form. Pure; the marker decides the tier. */
function renderDiffLine(line: string, width: number): string {
  if (line.length === 0) {
    return ""
  }
  if (line.startsWith("@@")) {
    return paint(fitPlain(line, width), colors.accent)
  }
  if (isFileHeader(line)) {
    return paint(fitPlain(line, width), colors.dim)
  }
  if (line.startsWith("+")) {
    return paint(fitPlain(line, width), colors.ok)
  }
  if (line.startsWith("-")) {
    return paint(fitPlain(line, width), colors.error)
  }
  // Context rows (and the `\ No newline` notice) are the background the
  // changed lines pop against — the faintest reading tier.
  return paint(fitPlain(line, width), colors.faint)
}

/**
 * A unified diff body → painted lines, one per source line. Blank lines
 * stay truly blank (no paint on whitespace). `width` bounds each row;
 * non-positive width disables the ellipsis.
 */
export function renderDiffLines(
  code: string,
  width: number = DEFAULT_MARKDOWN_WIDTH
): string[] {
  const body = code.replace(/\n$/, "")
  return body
    .split("\n")
    .map((line) => renderDiffLine(line, width))
}

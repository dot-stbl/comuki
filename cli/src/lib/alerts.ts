/**
 * Alert slabs. Pure: unknown error -> finished ANSI lines, so the
 * transcript flattener can drop them in as a `lines` block and the
 * Ink `AlertCard` can mount the same bytes.
 *
 * The kind word carries the status colour and body is faint.
 * Dichromat: colour never rides alone - the word `error` /
 * `warn` / `info` is on the top rule.
 */
import { ComukiApiError } from "./client"
import { DEFAULT_MARKDOWN_WIDTH } from "./markdown"
import { colors, paint, stripAnsi } from "../theme"

export type AlertKind = "error" | "warn" | "info"

export interface AlertCardModel {
  readonly kind: AlertKind
  readonly code?: string
  readonly title: string
  readonly detail: string
  readonly hints: readonly string[]
}

const UNREACHABLE =
  /unable to connect|fetch failed|econnrefused|connection refused|enotfound|eai_again|getaddrinfo|server unreachable/i

const DEFAULT_DETAIL: Record<AlertKind, string> = {
  error: "request failed",
  warn: "something needs attention",
  info: "",
}

/** Kind word → status colour. Body and frame never take this. */
export function kindColor(kind: AlertKind): string {
  if (kind === "error") {
    return colors.error
  }
  if (kind === "warn") {
    return colors.waiting
  }
  return colors.accent
}

/** DNS / connection-refused — the TUI stays up, but the process is a failure. */
export function isUnrecoverableError(error: unknown): boolean {
  if (error instanceof ComukiApiError) {
    return false
  }
  const message = error instanceof Error ? error.message : String(error)
  return UNREACHABLE.test(message)
}

/**
 * Maps any thrown value onto an alert card. HTTP 401/403/5xx get
 * stable titles so the transcript never collapses to `HTTP 401: …`.
 */
export function alertFromError(error: unknown): AlertCardModel {
  if (error instanceof ComukiApiError) {
    return fromApiError(error)
  }
  const message = error instanceof Error ? error.message : String(error)
  if (UNREACHABLE.test(message)) {
    return {
      kind: "error",
      title: "server unreachable",
      detail: "could not reach the comuki host",
      hints: [
        "check COMUKI_URL / COMUKI_API_KEY",
        "or run  comuki login",
      ],
    }
  }
  return {
    kind: "error",
    title: "request failed",
    detail: message.length > 0 ? message : DEFAULT_DETAIL.error,
    hints: ["/retry  last message"],
  }
}

/** Finished ANSI lines for a `kind: "lines"` transcript block. */
export function alertLines(
  error: unknown,
  width: number = DEFAULT_MARKDOWN_WIDTH
): string[] {
  return renderAlertCard(alertFromError(error), width)
}

/**
 * Filled-slab content. Width clamps so a narrow terminal never overflows.
 */
export function renderAlertCard(
  card: AlertCardModel,
  width: number = DEFAULT_MARKDOWN_WIDTH
): string[] {
  const label = card.code ?? card.title
  const headerPlain = `[${card.kind}] ${label}`
  const hintLines = card.hints.map((hint) =>
    hint.startsWith(" ") ? hint : ` ${hint}`
  )
  const detailSeed = card.detail.trim()
  const room = Math.max(8, width - 4)
  const body: string[] = [
    ...wrapWords(detailSeed, room),
    ...(hintLines.length > 0 ? ["", ...hintLines] : []),
  ]
  const kindPaint = kindColor(card.kind)
  return [
    paint(headerPlain, kindPaint),
    ...body.map((line) => `  ${paint(fit(line, room), colors.faint)}`),
  ]
}

function fromApiError(error: ComukiApiError): AlertCardModel {
  if (error.status === 401) {
    return {
      kind: "error",
      code: error.code,
      title: "signed out",
      detail:
        error.detail ?? "permission requires a signed-in subject",
      hints: ["/login  to sign in again", "/retry  last message"],
    }
  }
  if (error.status === 403) {
    return {
      kind: "error",
      code: error.code,
      title: "permission denied",
      detail:
        error.detail ?? "this subject lacks the required permission",
      hints: ["comuki login  if this is the wrong subject"],
    }
  }
  if (error.status >= 500) {
    return {
      kind: "error",
      code: error.code,
      title: "server error",
      detail: error.detail ?? "the host failed this request",
      hints: ["/retry  last message"],
    }
  }
  return {
    kind: "error",
    code: error.code,
    title: "request failed",
    detail: error.detail ?? DEFAULT_DETAIL.error,
    hints: ["/retry  last message"],
  }
}

function wrapWords(text: string, width: number): string[] {
  if (text.length === 0) {
    return []
  }
  const words = text.split(/\s+/).filter((word) => word.length > 0)
  const lines: string[] = []
  let current = ""
  for (const word of words) {
    const pieces =
      word.length > width ? splitHard(word, width) : [word]
    for (const piece of pieces) {
      const next = current.length === 0 ? piece : `${current} ${piece}`
      if (next.length > width && current.length > 0) {
        lines.push(current)
        current = piece
      } else {
        current = next
      }
    }
  }
  if (current.length > 0) {
    lines.push(current)
  }
  return lines
}

function splitHard(word: string, width: number): string[] {
  const chunks: string[] = []
  for (let offset = 0; offset < word.length; offset += width) {
    chunks.push(word.slice(offset, offset + width))
  }
  return chunks
}

function fit(text: string, width: number): string {
  const plain = stripAnsi(text)
  if (plain.length <= width) {
    return text
  }
  if (width <= 1) {
    return "…"
  }
  return `${plain.slice(0, width - 1)}…`
}

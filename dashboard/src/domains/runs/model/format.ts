export function formatDuration(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds))
  const minutes = Math.floor(safe / 60)
  const rem = safe % 60
  return `${String(minutes).padStart(2, "0")}:${String(rem).padStart(2, "0")}`
}

export function formatCost(value: number): string {
  return `$${value.toFixed(2)}`
}

export function formatTokens(tokens: number): string {
  if (tokens <= 0) {
    return "0"
  }
  return `${(tokens / 1000).toFixed(1)}k`
}

/** One run of a brief: prose, or a span the brief marked up as a value. */
export interface BriefSegment {
  text: string
  /** Written between backticks — a path, a header, an identifier. */
  code: boolean
}

/**
 * A ticket brief split into prose and the values it quotes.
 *
 * The brief arrives as text with backticks around the things that are literal
 * — endpoints, headers, table names. Splitting it here rather than turning it
 * into markup means the screen renders elements instead of injecting a string,
 * and there is no path from a payload to the DOM that goes through innerHTML.
 */
export function briefSegments(brief: string): BriefSegment[] {
  if (!brief) {
    return []
  }
  // An unmatched trailing backtick leaves an odd number of parts; the last one
  // is then prose that happened to follow a stray tick, which is what a reader
  // would assume anyway.
  const parts = brief.split("`")
  const closed = parts.length % 2 === 1
  return parts
    .map((text, index) => ({ text, code: closed && index % 2 === 1 }))
    .filter((segment) => segment.text.length > 0)
}

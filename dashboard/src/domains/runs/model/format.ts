/* What a run cost and what its brief said — readings that are the runs
   domain's own, and stay here.
 *
 * `formatDuration` used to live in this file and was imported by nine files
 * across four domains, which is what a utility parked in one domain's model
 * looks like from the outside: unfindable. It is `shared/lib/duration` now.
 * `formatCost` and `formatTokens` are read from other domains too and are the
 * obvious next move; `briefSegments` is a ticket brief and belongs nowhere
 * else. */

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

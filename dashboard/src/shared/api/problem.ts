/**
 * ProblemDetails readings off a failed kubb-client call.
 *
 * The transport (`kubb-client.ts`) rejects non-2xx responses with an `Error`
 * carrying `status` and `data` — the host's RFC 9457 problem body (`title`,
 * `detail`, `code`). A screen that prints only `error.message` says
 * "request failed 501" at an operator who asked for an act; the host's own
 * `detail` sentence is the reading they are owed. Endpoints that answer
 * "not implemented" (drain, stop, key mutation) are the main users — their
 * whole value on this side of the wire is the sentence.
 */

interface ProblemShape {
  readonly title?: unknown
  readonly detail?: unknown
  readonly code?: unknown
}

/** The problem body a rejected transport error carries, when it carries one. */
function problemOf(error: unknown): ProblemShape | null {
  if (typeof error !== "object" || error === null) {
    return null
  }
  const data = (error as { data?: unknown }).data
  if (typeof data !== "object" || data === null) {
    return null
  }
  return data as ProblemShape
}

function asText(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null
}

/** The problem's `detail` (or `title`), when the error carries one. */
export function problemDetail(error: unknown): string | null {
  const problem = problemOf(error)
  if (!problem) {
    return null
  }
  return asText(problem.detail) ?? asText(problem.title)
}

/**
 * The sentence a failed call should show: the host's problem detail when
 * there is one, the error's own message otherwise, a caller's fallback last.
 */
export function requestFailureMessage(
  error: unknown,
  fallback: string,
): string {
  if (problemDetail(error)) {
    return problemDetail(error) as string
  }
  if (error instanceof Error && error.message.length > 0) {
    return error.message
  }
  return fallback
}

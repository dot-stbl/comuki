import { describe, expect, it } from "vitest"

import {
  problemDetail,
  requestFailureMessage,
} from "@/shared/api/problem"

/** The shape `kubb-client` rejects with: message + status + parsed body. */
function transportError(body: unknown, message = "request failed 501") {
  return Object.assign(new Error(message), { status: 501, data: body })
}

describe("problem detail extraction", () => {
  it("Given a problem body with detail, when problemDetail reads it, then it answers the detail sentence", () => {
    const error = transportError({
      title: "Worker drain not implemented",
      detail: "the claim loop has no per-worker drain flag",
      code: "worker.drain_unsupported",
    })

    expect(problemDetail(error)).toBe(
      "the claim loop has no per-worker drain flag"
    )
  })

  it("Given a problem body without detail, when problemDetail reads it, then it falls back to the title", () => {
    const error = transportError({ title: "Virtual key not found" })

    expect(problemDetail(error)).toBe("Virtual key not found")
  })

  it("Given an error with no parsed body, when problemDetail reads it, then it answers null", () => {
    expect(problemDetail(transportError({}))).toBeNull()
    expect(problemDetail(new Error("plain"))).toBeNull()
    expect(problemDetail(null)).toBeNull()
  })

  it("Given any failure shape, when requestFailureMessage renders it, then the most human sentence wins", () => {
    expect(
      requestFailureMessage(
        transportError({ detail: "the claim loop has no drain flag" }),
        "fallback"
      )
    ).toBe("the claim loop has no drain flag")

    expect(requestFailureMessage(new Error("plain message"), "fallback")).toBe(
      "plain message"
    )

    expect(requestFailureMessage("not even an error", "fallback")).toBe(
      "fallback"
    )
  })
})

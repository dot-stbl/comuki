import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { StatusMappingPreview } from "./status-mapping-preview"

/**
 * What the tracker will say once the swarm has been through the ticket — and,
 * when it will say nothing, which of the two reasons that is.
 *
 * The heading takes the provider's word from the registry, so it reads for a
 * provider this build has never met. The body is the interesting half: an
 * empty mapping used to render one sentence, and that sentence claimed the
 * connection *was* the tracker. True of native intake. A lie about anything
 * else, and the lie an unknown provider would have been told.
 */
describe("what is written back, and what is not", () => {
  it("lists the provider's own words when there is a mapping", () => {
    render(
      <StatusMappingPreview
        kind="jira"
        mapping={[{ from: "success", to: "transition to done" }]}
      />
    )

    expect(screen.getByText("status written back to jira")).toBeTruthy()
    expect(screen.getByText("transition to done")).toBeTruthy()
  })

  it("says native intake is the tracker, which is final", () => {
    render(<StatusMappingPreview kind="native" mapping={[]} />)

    expect(screen.getByText(/native intake is the tracker/)).toBeTruthy()
  })

  it("says a provider it has no mapping for is a gap, not the same thing", () => {
    render(<StatusMappingPreview kind="linear" mapping={[]} />)

    // The host's own word in the heading rather than a blank, and a body
    // that says nothing is written back *and why* — an operator reading the
    // native sentence here would think their linear tickets were being
    // closed by the swarm.
    expect(screen.getByText("status written back to linear")).toBeTruthy()
    expect(screen.getByText(/this build has no mapping for that provider/))
      .toBeTruthy()
    expect(screen.queryByText(/native intake is the tracker/)).toBeNull()
  })
})

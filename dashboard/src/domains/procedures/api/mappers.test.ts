import { describe, expect, it } from "vitest"

import {
  mapProcedureLiveResponse,
  mapProcedureTraceResponse,
  mapProcedureVersionResponse,
  normalizeMode,
} from "@/domains/procedures/api/mappers"

/**
 * The procedures mappers' contract — the wire's kubb shape projects
 * into the domain's procedures vocabulary, and the inverse mappers
 * (the propose-patch mapper) are unit-tested separately by the host.
 * These tests lock the two domains apart: a name change in the
 * kubb-generated wire is caught here, not in a storybook render.
 */

describe("mapProcedureVersionResponse", () => {
  it("projects nodes + edges + identity into the domain", () => {
    const wire = {
      versionId: "v1",
      projectId: "p_comuki",
      procedureKey: "standard-feature",
      catalogVersion: "1",
      sourceRef: "client git source",
      nodes: [
        { id: "intake", kindKey: "agent", parameters: {} },
        { id: "verify", kindKey: "verify", parameters: {} },
      ],
      edges: [
        { fromNodeId: "intake", fromPort: "default", toNodeId: "verify" },
      ],
    }

    const domain = mapProcedureVersionResponse(wire)

    expect(domain.versionId).toBe("v1")
    expect(domain.nodes).toHaveLength(2)
    expect(domain.nodes[0].kindKey).toBe("agent")
    expect(domain.edges).toHaveLength(1)
    expect(domain.edges[0].fromPort).toBe("default")
  })
})

describe("mapProcedureTraceResponse", () => {
  it("projects events into the domain events with the run's pinned version", () => {
    const wire = {
      runId: "r-crown-mock",
      pinnedVersionId: "v1",
      events: [
        {
          nodeId: "v1",
          eventType: "pin_recorded",
          detail: "project p_comuki procedure standard-feature pinned to v1",
          at: "2026-10-04T08:46:00Z",
        },
      ],
    }

    const domain = mapProcedureTraceResponse(wire)

    expect(domain.runId).toBe("r-crown-mock")
    expect(domain.pinnedVersionId).toBe("v1")
    expect(domain.events).toHaveLength(1)
    expect(domain.events[0].eventType).toBe("pin_recorded")
  })
})

describe("mapProcedureLiveResponse", () => {
  it("projects the live panel's nullable fields from the trace", () => {
    const wire = {
      runId: "r-crown-mock",
      pinnedVersionId: "v1",
      procedureKey: "standard-feature",
      projectId: "p_comuki",
      pinnedAt: "2026-10-04T08:46:00Z",
      eventCount: 0,
      events: [],
      drift: null,
    }

    const domain = mapProcedureLiveResponse(wire)

    expect(domain.runId).toBe("r-crown-mock")
    expect(domain.pinnedVersionId).toBe("v1")
    expect(domain.drift).toBeNull()
    expect(domain.events).toEqual([])
  })
})

describe("normalizeMode", () => {
  it("accepts the three closed workbench modes", () => {
    expect(normalizeMode("studio")).toBe("studio")
    expect(normalizeMode("live")).toBe("live")
    expect(normalizeMode("replay")).toBe("replay")
  })

  it("falls back to studio on null, undefined, or unknown values", () => {
    expect(normalizeMode(null)).toBe("studio")
    expect(normalizeMode("garbage")).toBe("studio")
    expect(normalizeMode("")).toBe("studio")
  })
})
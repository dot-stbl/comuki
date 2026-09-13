import { describe, expect, it } from "vitest"

import {
  evidenceForRun,
  latestPngForTicket,
} from "@/domains/artifacts"
import {
  ARTIFACT_MIME,
  isImageMime,
  isSandboxedDocumentMime,
  type VisualArtifact,
} from "@/domains/artifacts/model/types"
import {
  mapVisualArtifactListItemToArtifact,
  mapVisualArtifactPageToPage,
} from "@/domains/artifacts/api/mappers"
import type { VisualArtifactListItem } from "@/shared/api/_generated/types/VisualArtifactListItem"
import type { VisualArtifactPage as VisualArtifactPageDto } from "@/shared/api/_generated/types/VisualArtifactPage"

/* Wire-shaped entry used by the mappers. The kubb types declare
   sizeBytes and version as `number | string` (the JSON-Schema int64 /
   int32 carrier); the seed / test below narrows to `string` for the
   predicate. */
function makeItem(
  overrides: Partial<VisualArtifactListItem> = {}
): VisualArtifactListItem {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    filename: "screenshot.png",
    contentType: "image/png",
    sizeBytes: 1024,
    title: null,
    createdAt: "2026-09-01T10:00:00.000Z",
    createdBy: "worker",
    runId: null,
    workItemId: null,
    sessionId: null,
    ticketId: null,
    version: 1,
    ...overrides,
  }
}

/* Coerce the wire's `number | string` for size / version into a string
   for the test — the type system enforces the union, not the carrier. */
function withStringNumbers(item: VisualArtifactListItem) {
  return {
    ...item,
    sizeBytes: String(item.sizeBytes),
    version: String(item.version),
  }
}

/**
 * The mapper normalises the wire's `number | string` carrier and the
 * `null` fields into a domain `VisualArtifact`. The fan-out is one
 * mapper, no branches on `useMock` — the kubb client and the mock seed
 * both reach this line.
 */
describe("mapVisualArtifactListItemToArtifact", () => {
  it("coerces a stringly-carried size into a number", () => {
    const result = mapVisualArtifactListItemToArtifact(
      withStringNumbers(makeItem({ sizeBytes: 4096 })),
      "00000000-0000-0000-0000-000000000abc",
    )
    expect(result.sizeBytes).toBe(4096)
  })

  it("falls back to 0 when the wire sent a non-finite value", () => {
    const result = mapVisualArtifactListItemToArtifact(
      withStringNumbers(makeItem({ sizeBytes: "not-a-number" })),
      "00000000-0000-0000-0000-000000000abc",
    )
    expect(result.sizeBytes).toBe(0)
  })

  it("threads the caller's projectId through to the domain row", () => {
    const result = mapVisualArtifactListItemToArtifact(
      makeItem(),
      "00000000-0000-0000-0000-000000000abc",
    )
    expect(result.projectId).toBe("00000000-0000-0000-0000-000000000abc")
  })

  it("keeps the optional links as `null` rather than undefined", () => {
    const result = mapVisualArtifactListItemToArtifact(makeItem(), "p")
    expect(result.runId).toBeNull()
    expect(result.workItemId).toBeNull()
    expect(result.sessionId).toBeNull()
    expect(result.ticketId).toBeNull()
  })
})

describe("mapVisualArtifactPageToPage", () => {
  it("maps each item and threads projectId through to every row", () => {
    const page = makePage(makeItem({ id: "a" }), makeItem({ id: "b" }))
    const result = mapVisualArtifactPageToPage(page, "proj-1")
    expect(result.projectId).toBe("proj-1")
    expect(result.items.map((entry: VisualArtifact) => entry.id)).toEqual([
      "a",
      "b",
    ])
    expect(
      result.items.every((entry: VisualArtifact) => entry.projectId === "proj-1"),
    ).toBe(true)
  })
})

/**
 * The list filters are the page-side reading of a fetched page —
 * the wire filter (the `?runId=` / `?ticketId=` query args) is also a
 * concern, but these helpers stay client-side because the mock path
 * reads through `ARTIFACTS_VISUAL_SEED` and a server that grew a wider
 * filter would still leave these tests deterministic.
 */
describe("evidenceForRun", () => {
  it("keeps only rows whose runId matches the requested run", () => {
    const items = [
      makeArtifact({ id: "a", runId: "run-1" }),
      makeArtifact({ id: "b", runId: "run-2" }),
      makeArtifact({ id: "c", runId: null }),
    ]
    expect(evidenceForRun(items, "run-1").map((entry) => entry.id)).toEqual([
      "a",
    ])
  })

  it("never returns a row whose runId is null (unlinked brain mockup)", () => {
    const items = [makeArtifact({ id: "a", runId: null })]
    expect(evidenceForRun(items, "run-1")).toEqual([])
  })
})

describe("latestPngForTicket", () => {
  it("returns the newest png linked to the ticket", () => {
    const items = [
      makeArtifact({
        id: "old",
        ticketId: "t-1",
        contentType: ARTIFACT_MIME.png,
        createdAt: "2026-09-01T10:00:00.000Z",
      }),
      makeArtifact({
        id: "new",
        ticketId: "t-1",
        contentType: ARTIFACT_MIME.png,
        createdAt: "2026-09-02T10:00:00.000Z",
      }),
    ]
    expect(latestPngForTicket(items, "t-1")?.id).toBe("new")
  })

  it("ignores non-png even when the ticket matches", () => {
    const items = [
      makeArtifact({
        id: "html",
        ticketId: "t-1",
        contentType: ARTIFACT_MIME.html,
      }),
    ]
    expect(latestPngForTicket(items, "t-1")).toBeNull()
  })

  it("ignores the ticket entirely when no rows link to it", () => {
    const items = [
      makeArtifact({ id: "a", ticketId: "t-2" }),
      makeArtifact({ id: "b", ticketId: "t-3" }),
    ]
    expect(latestPngForTicket(items, "t-1")).toBeNull()
  })
})

/**
 * The mime dispatchers — one predicate for `<img>`, one for the
 * sandboxed iframe. The list is closed because the host's size cap
 * (`VisualArtifactLimits`) is the same closed list, and a fifth
 * mime would land there before it landed here.
 */
describe("mime dispatchers", () => {
  it("treats only image/png as image mime", () => {
    expect(isImageMime("image/png")).toBe(true)
    expect(isImageMime("image/svg+xml")).toBe(false)
    expect(isImageMime("text/html")).toBe(false)
  })

  it("treats html + svg as sandboxed-document mime", () => {
    expect(isSandboxedDocumentMime("text/html")).toBe(true)
    expect(isSandboxedDocumentMime("image/svg+xml")).toBe(true)
    expect(isSandboxedDocumentMime("image/png")).toBe(false)
  })
})

/* --- tiny helpers, file-static per project rule ---------------------- */

function makeArtifact(overrides: Partial<VisualArtifact> = {}): VisualArtifact {
  return {
    id: "00000000-0000-0000-0000-000000000001",
    projectId: "p",
    filename: "screenshot.png",
    contentType: ARTIFACT_MIME.png,
    sizeBytes: 1024,
    title: null,
    createdAt: "2026-09-01T10:00:00.000Z",
    createdBy: "worker",
    runId: null,
    workItemId: null,
    sessionId: null,
    ticketId: null,
    version: 1,
    ...overrides,
  }
}

function makePage(
  ...items: VisualArtifactListItem[]
): VisualArtifactPageDto {
  return {
    projectId: "00000000-0000-0000-0000-000000000abc",
    items,
  }
}

import { describe, expect, it } from "vitest"

import {
  knowledgeDocumentToEntry,
  knowledgeDocumentsToSnapshot,
  knowledgeHitToEntry,
} from "@/domains/knowledge/api/mappers"

const DOC = {
  id: "d1f0c744-1111-2222-3333-444444444444",
  projectId: "b3d8a402-1111-2222-3333-444444444444",
  title: "Pricing migration runbook",
  source: "git",
  sourceRef: "docs/runbooks/pricing.md@v3",
  mimeType: "text/markdown",
  chunkCount: 12,
  tokenCount: 8400,
  createdAt: "2026-09-10T08:15:00Z",
} as const

describe("a document onto the library row", () => {
  it("states what the document carries rather than inventing rule marks", () => {
    const entry = knowledgeDocumentToEntry(DOC)

    expect(entry.kind).toBe("doc")
    expect(entry.title).toBe("Pricing migration runbook")
    expect(entry.revision).toBe("docs/runbooks/pricing.md@v3")
    expect(entry.ruleKind).toBeUndefined()
    expect(entry.pinned).toBe(false)
    expect(entry.summary).toBe(
      "git · text/markdown · 12 chunks · 8400 tokens"
    )
    expect(entry.updated).toBe("2026-09-10")
  })

  it("says where the full text lives instead of fabricating a body", () => {
    const entry = knowledgeDocumentToEntry(DOC)

    expect(entry.body).toContain("documents API")
    expect(entry.body).toContain(DOC.sourceRef)
  })
})

describe("a documents page onto the real-mode snapshot", () => {
  it("answers entries alone — no revision, no rule counts, no eval", () => {
    const snapshot = knowledgeDocumentsToSnapshot({
      items: [DOC],
      page: 1,
      pageSize: 25,
      total: 1,
    })

    expect(snapshot.revision).toBeNull()
    expect(snapshot.rulesActive).toBeNull()
    expect(snapshot.eval).toEqual([])
    expect(snapshot.entries).toHaveLength(1)
  })
})

describe("a search hit onto the library row", () => {
  it("keeps every wire value: snippet, score, both ids", () => {
    const entry = knowledgeHitToEntry({
      documentId: DOC.id,
      chunkId: "c9a1-0001",
      snippet: "migrate the payout computation onto the new price list",
      score: 0.83,
    })

    expect(entry.title).toBe("match 83%")
    expect(entry.summary).toBe("migrate the payout computation onto the new price list")
    expect(entry.scope).toBe(DOC.id)
    expect(entry.revision).toBe("c9a1-0001")
  })
})

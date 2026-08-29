import { describe, expect, it } from "vitest"

import { filterKnowledgeEntries } from "@/domains/knowledge/model/filter-knowledge"
import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

const sample: KnowledgeEntry[] = [
  {
    id: "api-errors",
    kind: "rule",
    title: "api-errors",
    scope: "global",
    ruleKind: "hard",
    revision: "a1b9e0",
    pinned: true,
    summary: "Typed API errors",
    body: "ProblemDetails",
    updated: "2h ago",
  },
  {
    id: "ui-tokens",
    kind: "rule",
    title: "ui-tokens",
    scope: "app:web-app",
    ruleKind: "soft",
    revision: "a1b9e0",
    pinned: false,
    summary: "Design tokens only",
    body: "No hex",
    updated: "2h ago",
  },
  {
    id: "architecture-overview",
    kind: "doc",
    title: "Architecture overview",
    scope: "docs",
    revision: "2.4.1",
    pinned: false,
    summary: "Leading model + swarm",
    body: "MCP knowledge",
    updated: "1w ago",
  },
]

describe("filterKnowledgeEntries", () => {
  it("returns all entries when query is empty", () => {
    expect(filterKnowledgeEntries(sample, "  ")).toHaveLength(3)
  })

  it("matches id, scope and summary", () => {
    expect(filterKnowledgeEntries(sample, "web-app")).toEqual([sample[1]])
    expect(filterKnowledgeEntries(sample, "typed")).toEqual([sample[0]])
    expect(filterKnowledgeEntries(sample, "doc")).toEqual([sample[2]])
  })
})

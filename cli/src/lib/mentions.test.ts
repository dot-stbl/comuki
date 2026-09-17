import { describe, expect, it } from "bun:test"
import {
  activeMentionQuery,
  dedupeByDocument,
  expandMentions,
  extractMentions,
  MAX_HITS_PER_MENTION,
  mentionLabel,
  mentionNoticeLines,
  preambleBlocks,
  slugForMention,
  stripMentionPreamble,
  suggestionRows,
  trimSnippet,
  type MentionHit,
} from "./mentions"
import { colors, stripAnsi } from "../theme"

function hit(
  documentId: string,
  snippet: string,
  score = 0.5
): MentionHit {
  return { documentId, snippet, score }
}

describe("extractMentions", () => {
  it("collects word-start @tokens in order", () => {
    expect(extractMentions("see @identity and @translator please")).toEqual([
      "identity",
      "translator",
    ])
  })

  it("dedupes repeated queries", () => {
    expect(extractMentions("@a and again @a")).toEqual(["a"])
  })

  it("ignores mid-word @ (emails) and a bare @", () => {
    expect(extractMentions("mail me at bob@example.com")).toEqual([])
    expect(extractMentions("just @ home")).toEqual([])
  })

  it("finds the leading mention of the line", () => {
    expect(extractMentions("@identity module")).toEqual(["identity"])
  })
})

describe("activeMentionQuery", () => {
  it("returns the token the draft ends in (from two chars on)", () => {
    expect(activeMentionQuery("see @id")).toBe("id")
    expect(activeMentionQuery("@identity")).toBe("identity")
  })

  it("stays closed for one-char tokens and terminated tokens", () => {
    expect(activeMentionQuery("see @i")).toBeNull()
    expect(activeMentionQuery("see @identity ")).toBeNull()
    expect(activeMentionQuery("@identity and more")).toBeNull()
  })

  it("requires the @ at a word start", () => {
    expect(activeMentionQuery("bob@example")).toBeNull()
  })
})

describe("labels and slugs", () => {
  it("prefers the document title, falls back to the snippet first line", () => {
    expect(
      mentionLabel(hit("d1", "first line of the chunk\nsecond"), (id) =>
        id === "d1" ? "Identity Module" : undefined
      )
    ).toBe("Identity Module")
    expect(mentionLabel(hit("d1", "first line of the chunk\nsecond"))).toBe(
      "first line of the chunk"
    )
  })

  it("truncates fallback labels to a label-sized prefix", () => {
    const label = mentionLabel(
      hit("d1", "a".repeat(120))
    )
    expect(label.length).toBe(48)
    expect(label.endsWith("…")).toBe(true)
  })

  it("slugifies labels — lowercase, dashes, no punctuation", () => {
    expect(slugForMention("Identity Module — v2")).toBe("identity-module-v2")
    expect(slugForMention("  Модуль памяти! ")).toBe("модуль-памяти")
    expect(slugForMention("???")).toBe("mention")
  })
})

describe("trimSnippet", () => {
  it("collapses whitespace and passes short text through", () => {
    expect(trimSnippet("  a\n\n b   c ")).toBe("a b c")
  })

  it("caps at the budget including the ellipsis", () => {
    const trimmed = trimSnippet("x".repeat(1000), 800)
    expect(trimmed.length).toBe(800)
    expect(trimmed.endsWith("…")).toBe(true)
  })
})

describe("expandMentions", () => {
  it("passes plain text through untouched", async () => {
    const expansion = await expandMentions("no mentions here", async () => [])
    expect(expansion).toEqual({
      typed: "no mentions here",
      outgoing: "no mentions here",
      resolutions: [],
      knowledgeUnavailable: false,
    })
  })

  it("appends one preamble block per resolved hit, top-3 max", async () => {
    const search = async (query: string) =>
      query === "identity"
        ? [
            hit("d1", "chunk one", 0.9),
            hit("d1", "chunk two", 0.8),
            hit("d2", "chunk three", 0.7),
            hit("d3", "chunk four", 0.6),
          ]
        : []
    const expansion = await expandMentions("use @identity please", search)

    expect(expansion.outgoing).toContain("use @identity please")
    const blocks = expansion.outgoing.split("\n\n")[1]?.split("\n") ?? []
    expect(blocks).toHaveLength(MAX_HITS_PER_MENTION)
    expect(blocks[0]).toMatch(/^\[@knowledge: .+ — chunk one\]$/)
    expect(expansion.resolutions[0]?.hits).toHaveLength(MAX_HITS_PER_MENTION)
  })

  it("flags the session off when the search refuses", async () => {
    const search = async () => {
      throw new Error("HTTP 403")
    }
    const expansion = await expandMentions("@identity", search)
    expect(expansion.knowledgeUnavailable).toBe(true)
    expect(expansion.outgoing).toBe("@identity")
    expect(expansion.resolutions).toEqual([])
  })
})

describe("preambleBlocks / stripMentionPreamble", () => {
  it("labels blocks with the document title when the lookup knows it", () => {
    const blocks = preambleBlocks(
      [{ query: "q", hits: [hit("d1", "the chunk text")] }],
      (id) => (id === "d1" ? "Identity Module" : undefined)
    )
    expect(blocks).toEqual(["[@knowledge: Identity Module — the chunk text]"])
  })

  it("strip removes block lines and trailing blanks, keeps the prose", () => {
    const stored =
      "use @identity please\n\n[@knowledge: Identity — chunk]\n[@knowledge: Other — chunk]"
    expect(stripMentionPreamble(stored)).toBe("use @identity please")
    expect(stripMentionPreamble("plain text")).toBe("plain text")
  })
})

describe("mentionNoticeLines", () => {
  it("nothing found → dim plain-text notice", () => {
    const lines = mentionNoticeLines({
      typed: "@ghost",
      outgoing: "@ghost",
      resolutions: [{ query: "ghost", hits: [] }],
      knowledgeUnavailable: false,
    })
    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toContain(
      "@ghost — nothing found, sent as plain text"
    )
    expect(lines[0]).toContain(colors.faint)
  })

  it("multiple matches → the top hit's title with an arrow", () => {
    const lines = mentionNoticeLines({
      typed: "@ident",
      outgoing: "@ident",
      resolutions: [
        {
          query: "ident",
          hits: [hit("d1", "chunk a", 0.9), hit("d2", "chunk b", 0.5)],
        },
      ],
      knowledgeUnavailable: false,
    })
    expect(lines).toHaveLength(1)
    expect(stripAnsi(lines[0] ?? "")).toContain("@ident ← ")
  })

  it("single match and clean runs stay silent", () => {
    const expansion = {
      typed: "@ident",
      outgoing: "@ident",
      resolutions: [{ query: "ident", hits: [hit("d1", "chunk")] }],
      knowledgeUnavailable: false,
    }
    expect(mentionNoticeLines(expansion)).toEqual([])
  })

  it("knowledge refusal → the unavailable notice", () => {
    const lines = mentionNoticeLines({
      typed: "@ident",
      outgoing: "@ident",
      resolutions: [],
      knowledgeUnavailable: true,
    })
    expect(stripAnsi(lines[0] ?? "")).toContain(
      "knowledge api unavailable — mentions sent as plain text"
    )
  })
})

describe("menu rows", () => {
  it("dedupes chunks to one row per document, best score wins", () => {
    const rows = dedupeByDocument([
      hit("d1", "weak chunk", 0.4),
      hit("d2", "other doc", 0.6),
      hit("d1", "best chunk", 0.9),
    ])
    expect(rows).toHaveLength(2)
    expect(rows[0]?.snippet).toBe("best chunk")
    expect(rows[1]?.snippet).toBe("other doc")
  })

  it("rows carry label, fitted snippet and the accept slug", () => {
    const rows = suggestionRows(
      [hit("d1", "Identity Module handles auth.\nmore text", 0.9)],
      (id) => (id === "d1" ? "Identity Module" : undefined),
      40
    )
    expect(rows[0]?.label).toBe("Identity Module")
    expect(rows[0]?.mention).toBe("identity-module")
    expect(rows[0]?.snippet.length).toBeLessThanOrEqual(
      40 - "Identity Module".length - 1
    )
  })
})

/**
 * Transcript entry view-model (issue #74) — pure unit tests: the
 * part union maps to entry kinds, consecutive tool parts group with
 * a count, ids stay stable across rebuilds, collapsed summaries
 * answer what+state and expanded detail reveals the exact payload
 * with omitted-line counts.
 */

import { describe, expect, test } from "bun:test"
import type { MessagePart } from "../contracts/_generated/http/types/MessagePart"
import type { HarnessMessage, HarnessSession } from "../harness/state"
import { sessionId, turnRequestId } from "../harness/state"
import { createI18nFor } from "../locales"
import {
  buildTranscriptEntries,
  collapsibleIds,
  diffCounts,
  diffPath,
  entryCollapsible,
  entryLines,
  formatDurationMs,
  formatTokenCount,
  lastCollapsibleId,
  MAX_PAYLOAD_LINES,
  summarizeToolArgs,
  toolEntryState,
  toolGroupState,
  type EntryRenderContext,
} from "./entries"
import { lineText } from "./styled"

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const CREATED_AT = Date.parse("2026-01-01T12:34:56Z")

function message(overrides: Partial<HarnessMessage>): HarnessMessage {
  return {
    id: "m-1",
    role: "assistant",
    content: "",
    createdAtUnixMs: CREATED_AT,
    ...overrides,
  }
}

function sessionWith(
  transcript: readonly HarnessMessage[]
): HarnessSession {
  return {
    identity: { kind: "remote", id: sessionId("s-1") },
    projectId: null,
    title: "Test",
    createdAtUnixMs: 0,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "loaded" },
    transcript,
    queue: [],
  }
}

async function context(
  expanded: ReadonlySet<string> = new Set()
): Promise<EntryRenderContext> {
  return { i18n: await createI18nFor("en"), width: 80, expanded }
}

const RICH_PARTS: MessagePart[] = [
  { kind: "thinking", text: "I should check memory.", tokens: 120 },
  { kind: "tool", name: "memory.recall", inputJson: '{"query":"identity","top":5}', status: "ok", outputJson: '{"hits":[]}', durationMs: 220 },
  { kind: "tool", name: "fs.read", inputJson: '{"path":"a.ts"}', status: "ok" },
  { kind: "tool", name: "shell.run", inputJson: '{"cmd":"ls"}', status: "failed" },
  { kind: "text", markdown: "Here is the answer." },
  { kind: "code", language: "ts", source: "const a = 1", path: "src/a.ts", startLine: 3 },
  { kind: "code", language: "diff", source: "--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new" },
  { kind: "plan", nodes: [{ id: "n1", title: "write it", profileKey: "coder", brief: "write it" }], edges: [] },
  { kind: "handoff", query: "follow-up task" },
]

// ---------------------------------------------------------------------------
// Mapping
// ---------------------------------------------------------------------------

describe("buildTranscriptEntries — part union → kinds", () => {
  test("rich assistant message decomposes in wire order with one tool group", () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({ id: "m-1", view: { id: "m-1", role: "assistant", content: "", toolName: null, parts: RICH_PARTS, meta: null, createdAt: "" } }),
      ])
    )
    expect(entries.map((entry) => entry.kind)).toEqual([
      "thinking",
      "tool-group",
      "assistant",
      "code",
      "diff",
      "plan",
      "handoff",
    ])
    const group = entries.find((entry) => entry.kind === "tool-group")
    expect(group && group.kind === "tool-group" ? group.tools.length : 0).toBe(3)
  })

  test("non-consecutive tool runs form separate groups", () => {
    const parts = [
      { kind: "tool", name: "a", inputJson: "{}", status: "ok" },
      { kind: "text", markdown: "between" },
      { kind: "tool", name: "b", inputJson: "{}", status: "ok" },
    ] as MessagePart[]
    const entries = buildTranscriptEntries(
      sessionWith([message({ view: { id: "m-1", role: "assistant", content: "", toolName: null, parts, meta: null, createdAt: "" } })])
    )
    const groups = entries.filter((entry) => entry.kind === "tool-group")
    expect(groups).toHaveLength(2)
  })

  test("ids are stable across rebuilds", () => {
    const build = () =>
      buildTranscriptEntries(
        sessionWith([
          message({ view: { id: "m-1", role: "assistant", content: "", toolName: null, parts: RICH_PARTS, meta: null, createdAt: "" } }),
        ])
      ).map((entry) => entry.id)
    expect(build()).toEqual(build())
  })

  test("content fallback (no parts) yields one prefixed assistant entry", () => {
    const entries = buildTranscriptEntries(
      sessionWith([message({ role: "assistant", content: "plain answer" })])
    )
    expect(entries).toHaveLength(1)
    expect(entries[0]).toMatchObject({ kind: "assistant", prefix: true })
  })

  test("user and system roles map to message entries; diagram maps to code", () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({ id: "u1", role: "user", content: "hi" }),
        message({ id: "s1", role: "system", content: "note" }),
        message({
          id: "d1",
          view: {
            id: "d1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [{ kind: "diagram", dialect: "mermaid", source: "A-->B" }] as MessagePart[],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    expect(entries[0]).toMatchObject({ kind: "message", role: "user" })
    expect(entries[1]).toMatchObject({ kind: "message", role: "system" })
    expect(entries[2]).toMatchObject({ kind: "code", language: "mermaid" })
  })

  test("the thinking turn appends one streaming entry", () => {
    const session = {
      ...sessionWith([]),
      turn: {
        kind: "thinking" as const,
        requestId: turnRequestId("turn-live"),
        accumulatedText: "live text",
      },
    }
    const entries = buildTranscriptEntries(session)
    expect(entries).toHaveLength(1)
    expect(entries[0]).toMatchObject({ kind: "streaming", text: "live text" })
    expect(entryCollapsible(entries[0]!)).toBe(false)
  })
})

// ---------------------------------------------------------------------------
// Formatting ports
// ---------------------------------------------------------------------------

describe("formatting ports", () => {
  test("summarizeToolArgs — quoted strings, bare numbers, array counts, budget", () => {
    expect(summarizeToolArgs('{"query":"identity module","top":5}')).toBe(
      `"identity module", 5`
    )
    expect(summarizeToolArgs('{"ids":["a","b"]}')).toBe(`2 ids`)
    expect(summarizeToolArgs('{"nested":{"deep":1}}')).toBe("")
    expect(summarizeToolArgs("not json")).toBe("")
    const long = `{"q":"${"x".repeat(80)}"}`
    expect(summarizeToolArgs(long).length).toBeLessThanOrEqual(41)
  })

  test("formatDurationMs + formatTokenCount", () => {
    expect(formatDurationMs(120)).toBe("120ms")
    expect(formatDurationMs(3_400)).toBe("3.4s")
    expect(formatDurationMs(125_000)).toBe("2m 5s")
    expect(formatTokenCount(40)).toBe("40 tok")
    expect(formatTokenCount(1_234)).toBe("1.2k tok")
  })

  test("toolEntryState + toolGroupState", () => {
    expect(toolEntryState("ok")).toBe("ok")
    expect(toolEntryState("failed")).toBe("error")
    expect(toolEntryState("running")).toBe("running")
    const tools = [
      { name: "a", inputJson: "{}", status: "ok", outputJson: null, durationMs: null },
      { name: "b", inputJson: "{}", status: "failed", outputJson: null, durationMs: null },
    ]
    expect(toolGroupState(tools)).toBe("error")
    expect(toolGroupState(tools.slice(0, 1))).toBe("ok")
  })
})

// ---------------------------------------------------------------------------
// Diff helpers
// ---------------------------------------------------------------------------

describe("diff summaries", () => {
  test("counts skip file headers", () => {
    const source = "--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n-old1\n-old2\n+new\n ctx"
    expect(diffCounts(source)).toEqual({ added: 1, removed: 2 })
    expect(diffPath(source)).toBe("x")
  })
})

// ---------------------------------------------------------------------------
// Entry lines — progressive disclosure
// ---------------------------------------------------------------------------

describe("entryLines — collapse, expand, omit", () => {
  test("collapsed tool-group summary answers what+state, hides payloads", async () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              {
                kind: "tool",
                name: "memory.recall",
                inputJson: '{"query":"identity"}',
                status: "ok",
                outputJson: '{"marker":"HIDDEN-OUTPUT-MARKER"}',
                durationMs: 220,
              },
            ],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    const ctx = await context()
    const collapsed = entries.flatMap((entry) => entryLines(entry, ctx))
    const collapsedText = collapsed.map(lineText).join("\n")
    expect(collapsedText).toContain("tool memory.recall")
    expect(collapsedText).toContain("ok")
    expect(collapsedText).not.toContain("HIDDEN-OUTPUT-MARKER")
    // The detail-exists marker rides the collapsed line.
    expect(collapsedText).toContain("…")
  })

  test("expanded tool-group reveals payloads, stamp and correlation id", async () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              {
                kind: "tool",
                name: "memory.recall",
                inputJson: '{"query":"identity"}',
                status: "ok",
                outputJson: '{"marker":"HIDDEN-OUTPUT-MARKER"}',
                durationMs: null,
              },
            ],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    const ids = collapsibleIds(entries)
    const ctxExpanded = await context(new Set(ids))
    const expanded = entries.flatMap((entry) => entryLines(entry, ctxExpanded))
    const text = expanded.map(lineText).join("\n")
    expect(text).toContain("HIDDEN-OUTPUT-MARKER")
    expect(text).toContain("at 12:34:56")
    expect(text).toContain("id m-1")
  })

  test("oversized payloads report omitted lines", async () => {
    const huge = JSON.stringify({ rows: Array.from({ length: MAX_PAYLOAD_LINES + 7 }, (_, i) => i) })
    // The omitted count is measured on the PRETTY-PRINTED payload.
    const prettyLines = JSON.stringify(JSON.parse(huge), null, 2).split("\n").length
    const expectedOmitted = prettyLines - MAX_PAYLOAD_LINES
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              { kind: "tool", name: "shell.run", inputJson: "{}", status: "ok", outputJson: huge },
            ],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    const ctxAll = await context(new Set(collapsibleIds(entries)))
    const text = entries
      .flatMap((entry) => entryLines(entry, ctxAll))
      .map(lineText)
      .join("\n")
    expect(text).toContain(`+${expectedOmitted} lines omitted`)
  })

  test("grouped summary carries the count", async () => {
    const parts: MessagePart[] = [
      { kind: "tool", name: "a", inputJson: "{}", status: "ok" },
      { kind: "tool", name: "b", inputJson: "{}", status: "ok" },
      { kind: "tool", name: "c", inputJson: "{}", status: "failed" },
    ]
    const entries = buildTranscriptEntries(
      sessionWith([message({ view: { id: "m-1", role: "assistant", content: "", toolName: null, parts, meta: null, createdAt: "" } })])
    )
    const ctx = await context()
    const summary = entries
      .flatMap((entry) => entryLines(entry, ctx))
      .map(lineText)
      .join("\n")
    expect(summary).toContain("tools ×3")
    expect(summary).toContain("1 error")
    expect(summary).toContain("2 ok")
  })

  test("thinking collapses to one line; expanded renders the text muted", async () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [{ kind: "thinking", text: "deep thought", tokens: 1200 }] as MessagePart[],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    const ctx = await context()
    const collapsedText = entries
      .flatMap((entry) => entryLines(entry, ctx))
      .map(lineText)
      .join("\n")
    expect(collapsedText).toContain("thinking")
    expect(collapsedText).toContain("1.2k tok")
    expect(collapsedText).not.toContain("deep thought")

    const ctxAll = await context(new Set(collapsibleIds(entries)))
    const expandedText = entries
      .flatMap((entry) => entryLines(entry, ctxAll))
      .map(lineText)
      .join("\n")
    expect(expandedText).toContain("deep thought")
  })

  test("diff summary counts; expanded renders signed colored rows", async () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              {
                kind: "code",
                language: "diff",
                source: "--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new",
              },
            ],
            meta: null,
            createdAt: "",
          },
        }),
      ])
    )
    const ctx = await context()
    const collapsedText = entries
      .flatMap((entry) => entryLines(entry, ctx))
      .map(lineText)
      .join("\n")
    expect(collapsedText).toContain("diff x")
    expect(collapsedText).toContain("+1")
    expect(collapsedText).toContain("−1")
    expect(collapsedText).not.toContain("-old")

    const ctxAll = await context(new Set(collapsibleIds(entries)))
    const expanded = entries.flatMap((entry) => entryLines(entry, ctxAll))
    const oldLine = expanded.find((line) => lineText(line) === "  -old")
    expect(oldLine?.[0]?.tone).toBe("error")
    const newLine = expanded.find((line) => lineText(line) === "  +new")
    expect(newLine?.[0]?.tone).toBe("ok")
  })

  test("assistant markdown renders in full (never collapsible) with the role prefix", async () => {
    const entries = buildTranscriptEntries(
      sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              { kind: "text", markdown: "# Title\n\nBody with `code`." },
              { kind: "text", markdown: "Second block keeps no prefix." },
            ],
            meta: { model: "test-model", tokensIn: 10, tokensOut: 20 },
            createdAt: "",
          },
        }),
      ])
    )
    const ctx = await context()
    const text = entries
      .flatMap((entry) => entryLines(entry, ctx))
      .map(lineText)
      .join("\n")
    expect(text).toContain("comuki › Title")
    expect(text).toContain("Body with")
    expect(text).toContain("Second block keeps no prefix.")
    expect(text).not.toContain("comuki › Second block")
    expect(text).toContain("test-model")
    expect(text).toContain("10→20 tok")
  })

  test("lastCollapsibleId picks the LAST collapsible; streaming never qualifies", async () => {
    const entries = buildTranscriptEntries({
      ...sessionWith([
        message({
          view: {
            id: "m-1",
            role: "assistant",
            content: "",
            toolName: null,
            parts: [
              { kind: "tool", name: "a", inputJson: "{}", status: "ok" },
              { kind: "text", markdown: "answer" },
            ],
            meta: null,
            createdAt: "",
          },
        }),
      ]),
      turn: {
        kind: "thinking" as const,
        requestId: turnRequestId("turn-live"),
        accumulatedText: "…",
      },
    })
    expect(lastCollapsibleId(entries)).toBe("m-1#tools0")
    expect(collapsibleIds(entries)).toEqual(["m-1#tools0"])
  })
})

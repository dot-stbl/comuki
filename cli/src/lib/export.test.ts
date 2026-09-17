/**
 * Markdown export tests (`/export`): exact-document asserts for the
 * section grammar (`## you` / `## comuki`, collapsed event quotes,
 * fenced code, plan lists) and the default filename derivation.
 */
import { describe, expect, it } from "bun:test"
import { exportFileName, exportMarkdown } from "./export"
import { colors } from "../theme"
import type { ChatBlock } from "./sessions"
import type { ChatMessageView, MessagePart } from "./client"

function message(
  role: string,
  content: string,
  parts: readonly MessagePart[] | null = null,
  toolName: string | null = null
): ChatMessageView {
  return {
    id: `${role}-${Math.random().toString(36).slice(2, 8)}`,
    role,
    content,
    toolName,
    parts,
    meta: null,
    createdAt: "2026-09-18T10:00:00Z",
  }
}

function block(messageValue: ChatMessageView): ChatBlock {
  return { kind: "message", key: messageValue.id, message: messageValue }
}

describe("exportMarkdown", () => {
  it("renders user and assistant turns as sections", () => {
    const markdown = exportMarkdown([
      block(message("user", "fix the login flow")),
      block(message("assistant", "Looking at it.")),
    ])
    expect(markdown).toBe(
      "## you\n\nfix the login flow\n\n## comuki\n\nLooking at it.\n"
    )
  })

  it("renders assistant parts in wire order — collapsed quotes, text verbatim", () => {
    const parts: readonly MessagePart[] = [
      { kind: "thinking", text: "hmm", tokens: 4100, durationMs: 6200 },
      {
        kind: "tool",
        name: "memory.recall",
        inputJson: '{"query":"identity module","top":5}',
        status: "ok",
        durationMs: 41,
      },
      { kind: "text", markdown: "**Done.**" },
    ]
    const markdown = exportMarkdown([block(message("assistant", "", parts))])
    expect(markdown).toBe(
      [
        "## comuki",
        "",
        "> thinking · 4.1k tok · 6.2s",
        "",
        '> memory.recall("identity module", 5) · ok · 41ms',
        "",
        "**Done.**",
        "",
      ].join("\n")
    )
  })

  it("keeps code fenced with its language and path anchor", () => {
    const parts: readonly MessagePart[] = [
      {
        kind: "code",
        language: "ts",
        source: "const answer = 42",
        path: "src/a.ts",
        startLine: 3,
      },
    ]
    const markdown = exportMarkdown([block(message("assistant", "", parts))])
    expect(markdown).toBe(
      "## comuki\n\n> code · src/a.ts:3\n```ts\nconst answer = 42\n```\n"
    )
  })

  it("renders a plan as a list with dependencies", () => {
    const parts: readonly MessagePart[] = [
      {
        kind: "plan",
        nodes: [
          { key: "n1", profileKey: "coder", brief: "write the parser", dependsOn: [] },
          { key: "n2", profileKey: "reviewer", brief: "review it\nline two is dropped", dependsOn: ["n1"] },
        ],
        edges: [{ from: "n1", to: "n2" }],
      },
    ]
    const markdown = exportMarkdown([block(message("assistant", "", parts))])
    expect(markdown).toBe(
      "## comuki\n\n- **coder** → write the parser\n- **reviewer** → review it — depends on n1\n"
    )
  })

  it("renders journal rows as quiet quotes", () => {
    const markdown = exportMarkdown([
      block(message("system", "worker 3 stopped")),
      block(message("tool", "", null, "docker.run")),
    ])
    expect(markdown).toBe("> system: worker 3 stopped\n\n> docker.run\n")
  })

  it("renders line blocks ANSI-stripped as quotes and drops empties", () => {
    const markdown = exportMarkdown([
      {
        kind: "lines",
        key: "l1",
        lines: ["", `${colors.bright}renamed to X${colors.reset}`, ""],
      },
    ])
    expect(markdown).toBe("> renamed to X\n")
  })

  it("returns an empty document for an empty transcript", () => {
    expect(exportMarkdown([])).toBe("")
  })
})

describe("exportFileName", () => {
  const at = new Date(2026, 8, 18, 14, 5) // local 2026-09-18 14:05

  it("composes comuki-{slug}-{yyyymmdd-hhmm}.md", () => {
    expect(exportFileName("fix the login", at)).toBe(
      "comuki-fix-the-login-20260918-1405.md"
    )
  })

  it("collapses path-hostile characters and falls back to session", () => {
    expect(exportFileName('a/b\\c: d?"e', at)).toBe(
      "comuki-a-b-c-d-e-20260918-1405.md"
    )
    expect(exportFileName("///", at)).toBe("comuki-session-20260918-1405.md")
  })
})

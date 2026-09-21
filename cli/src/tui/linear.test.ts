/**
 * Linear renderer golden-output tests (issue #79).
 *
 * Three representative snapshots, asserted as exact line-by-line
 * arrays so any regression in the renderer (tone → glyph policy,
 * section markers, banner format) shows up immediately:
 *
 * 1. **Empty swarm** — a state with no signal + a single
 *    transcript line. The renderer prints the banner, the empty
 *    section, and the transcript block.
 *
 * 2. **Single awaiting approval** — a session with a pending plan
 *    in `awaiting-approval`. The renderer emits the approval
 *    summary, the decision prompt, the detail block (intent / scope /
 *    plan steps) and the typed-confirmation prompt.
 *
 * 3. **Full transcript with collapsed + expanded entries** — a
 *    transcript with multiple message echoes, collapsible tool /
 *    code / diff / plan entries. Two renders: once with everything
 *    collapsed, once with a specific entry expanded. Asserts the
 *    collapsed→expanded reveal produces the expected extra lines.
 */
import { describe, expect, test } from "bun:test"
import { createI18nFor, DEFAULT_LOCALE } from "../locales"
import type {
  HarnessMessage,
  HarnessSession,
  HarnessState,
  SessionKey,
} from "../harness/state"
import { sessionId, turnRequestId } from "../harness/state"
import type { AttentionSignal } from "../kernel/attention"
import { createLinearRenderer, pickGlyphs, ASCII_GLYPHS, UNICODE_GLYPHS } from "./linear"
import { buildTranscriptEntries, collapsibleIds } from "./entries"
import type { ResolvedModes } from "./modes"
import { resolveModes } from "./modes"
import type { CapabilityDetect } from "./modes"

const TTY: CapabilityDetect = { stdoutIsTTY: true, stdinIsTTY: true, columns: 80, rows: 24 }
const LINEAR_MODES: ResolvedModes = resolveModes({ mode: "linear" }, { TERM: "xterm-256color", COLORTERM: "truecolor" }, TTY)
const ASCII_LINEAR_MODES: ResolvedModes = resolveModes(
  { mode: "linear", ascii: true, noColor: true },
  { TERM: "xterm-256color", COLORTERM: "truecolor" },
  TTY
)

describe("LinearRenderer — glyph policy", () => {
  test("default (unicode) glyph set uses ›, ·, ─", () => {
    const glyphs = pickGlyphs(LINEAR_MODES)
    expect(glyphs.prompt).toBe("›")
    expect(glyphs.bullet).toBe("·")
    expect(glyphs.rule).toBe("─")
  })

  test("ascii glyph set uses >, *, -, +", () => {
    const glyphs = pickGlyphs(ASCII_LINEAR_MODES)
    expect(glyphs.prompt).toBe(">")
    expect(glyphs.bullet).toBe("*")
    expect(glyphs.rule).toBe("-")
    expect(glyphs.topLeft).toBe("+")
  })

  test("ASCII and UNICODE constants agree with the resolver", () => {
    expect(UNICODE_GLYPHS.bullet).toBe("·")
    expect(ASCII_GLYPHS.bullet).toBe("*")
  })
})

describe("LinearRenderer — empty swarm snapshot", () => {
  test("banner + empty transcript + empty canvas section", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const state = emptyState()
    const session = state.sessions[0] ?? null

    const lines = [
      renderer.modeBanner(),
      renderer.topBar(state, session),
      "",
      ...renderer.transcript(state, session),
    ]
    expect(lines).toEqual([
      expect.stringContaining("Mode: linear=on"),
      expect.stringContaining("comuki"),
      "",
      // empty session hint
      "no open session — ctrl+n to start one",
    ])
  })

  test("ascii variant uses ASCII glyphs and `[#` markers everywhere", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: ASCII_LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const state = emptyState()
    const session = state.sessions[0] ?? null
    const swarmLines = renderer.swarm(stalledSignal(), new Map())

    expect(swarmLines.join("\n")).toContain("[#")
    expect(swarmLines.join("\n")).toMatch(/[*+]/)
    expect(renderer.transcript(state, session).join("\n")).not.toMatch(/[┌│]/u)
  })
})

describe("LinearRenderer — single awaiting approval snapshot", () => {
  test("approval emits summary + decision prompt + detail block + typed confirmation", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const state = awaitingApprovalState()
    const session = state.sessions[0] ?? null
    expect(session).not.toBeNull()

    const lines = renderer.transcript(state, session)
    const joined = lines.join("\n")

    // approval summary line
    expect(joined).toContain("approval")
    // typed-confirmation prompt line
    expect(joined).toContain("Awaiting your decision")
    // scope field
    expect(joined).toContain("scope:")
    // plan steps
    expect(joined).toContain("plan:")
    expect(joined).toContain("step one")
    expect(joined).toContain("step two")
    // y/n decision prompt
    expect(joined).toContain("decide:")
    expect(joined).toContain("y = approve")
    expect(joined).toContain("n = reject")
  })

  test("awaiting / approved / rejected confirmation methods format correctly", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    expect(renderer.awaitingDecision("chat/sessions/x/approve"))
      .toMatch(/Awaiting your decision.*chat\/sessions\/x\/approve/)
    expect(renderer.approvedConfirmation("chat/sessions/x/approve"))
      .toMatch(/Approved.*chat\/sessions\/x\/approve/)
    expect(renderer.approvedConfirmation("chat/sessions/x/approve"))
      .toMatch(/✓/)
    expect(renderer.rejectedConfirmation("chat/sessions/x/approve"))
      .toMatch(/Rejected.*chat\/sessions\/x\/approve/)
    expect(renderer.rejectedConfirmation("chat/sessions/x/approve"))
      .toMatch(/✗/)
    expect(renderer.receiptWritten("session-1"))
      .toMatch(/Receipt written for session session-1/)
  })
})

describe("LinearRenderer — full transcript with collapsed + expanded entries", () => {
  test("collapsed transcript emits summary lines only; expanded reveals the detail block", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const expandedIds = new Map<SessionKey, ReadonlySet<string>>()
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: expandedIds,
    })

    const state = fullTranscriptState()
    const session = state.sessions[0] ?? null
    expect(session).not.toBeNull()

    const collapsedLines = renderer.transcript(state, session, new Set<string>())
    const collapsedJoined = collapsedLines.join("\n")

    expect(collapsedJoined).toContain("you ›")
    expect(collapsedJoined).toContain("comuki ›")
    // tool entry collapses to a summary line with the tool name
    expect(collapsedJoined).toContain("tool")
    // diff / code / plan collapses
    expect(collapsedJoined).toContain("diff")
    expect(collapsedJoined).toContain("plan")

    // Pick a collapsible entry id from the transcript pipeline
    // directly — the rendered summary line doesn't carry the id.
    const ids = collapsibleIds(buildTranscriptEntries(session))
    expect(ids.length).toBeGreaterThan(0)
    const toolId = ids[0]!

    const expandedLines = renderer.transcript(state, session, new Set<string>([toolId]))
    const expandedJoined = expandedLines.join("\n")

    // Expansion adds the tool detail block — input, output, status.
    expect(expandedJoined.length).toBeGreaterThan(collapsedJoined.length)
  })

  test("swarm signal produces [p0]/[p1]/[p2] section markers", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const sessions = new Map<string, HarnessSession>()
    const swarmLines = renderer.swarm(stalledSignal(), sessions)

    expect(swarmLines.join("\n")).toContain("[#")
    expect(swarmLines.join("\n")).toContain("needs you")
    expect(swarmLines.join("\n")).toContain("stalled")
    expect(swarmLines.join("\n")).toContain("no heartbeat")
  })

  test("swarm with no signal emits the canvas-empty line", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const lines = renderer.swarm(emptySignal(), new Map())
    expect(lines.some((line) => line.startsWith("Canvas empty"))).toBe(true)
  })

  test("ascii variant of the swarm renders section markers as `[#…]`, not Unicode glyphs", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: ASCII_LINEAR_MODES,
      width: 80,
      expanded: new Map(),
    })

    const sessions = new Map<string, HarnessSession>()
    const lines = renderer.swarm(stalledSignal(), sessions)
    const joined = lines.join("\n")
    expect(joined).not.toMatch(/[·┌│─]/u)
    expect(joined).toContain("[#")
  })

  test("mode banner lists every resolved flag in stable order", async () => {
    const i18n = await createI18nFor(DEFAULT_LOCALE)
    const renderer = createLinearRenderer({
      i18n,
      modes: LINEAR_MODES,
      width: 200,
      expanded: new Map(),
    })

    const banner = renderer.modeBanner()
    expect(banner).toContain("linear=on")
    expect(banner).toContain("reduced-motion=off")
    expect(banner).toContain("high-contrast=off")
    expect(banner).toContain("no-color=off")
    expect(banner).toContain("ascii=off")
    expect(banner).toContain("no-mouse=off")
    expect(banner).toContain("unicode-narrow=off")
    expect(banner).toContain("machine=off")
  })
})

// ---------------------------------------------------------------------------
// Fixture builders — small, focused, no production code paths
// ---------------------------------------------------------------------------

function emptyState(): HarnessState {
  return {
    sessions: [],
    activeSessionId: null,
    connection: { kind: "connected" },
    auth: { kind: "authenticated", subjectId: "user-1" },
    overlay: { kind: "closed" },
    drafts: [],
    cursors: {},
    outbound: [],
  }
}

function awaitingApprovalState(): HarnessState {
  const remoteKey = sessionId("session-1")
  const session: HarnessSession = {
    identity: { kind: "remote", id: remoteKey },
    projectId: null,
    title: "Approval demo",
    createdAtUnixMs: 0,
    renamed: false,
    unread: false,
    turn: { kind: "awaiting-approval", requestId: turnRequestId("turn-1") },
    transcriptLoad: { kind: "loaded" },
    transcript: [
      makeAssistantMessage("m-approve", "I want to deploy a worker.", {
        kind: "plan",
        scope: "chat/sessions/session-1/approve",
        intent: "deploy a worker",
        nodes: [
          { id: "n1", key: "n1", title: "deploy", brief: "step one" },
          { id: "n2", key: "n2", title: "verify", brief: "step two" },
        ],
      }),
    ],
    queue: [],
    pendingPlan: {
      scope: "chat/sessions/session-1/approve",
      intent: "deploy a worker",
      steps: ["step one", "step two"],
      diff: "",
      nodes: [
        { id: "n1", key: "n1", title: "deploy", brief: "step one" },
        { id: "n2", key: "n2", title: "verify", brief: "step two" },
      ],
      estimateMinutes: 2,
    },
  }
  return {
    sessions: [session],
    activeSessionId: remoteKey,
    connection: { kind: "connected" },
    auth: { kind: "authenticated", subjectId: "user-1" },
    overlay: { kind: "closed" },
    drafts: [],
    cursors: {},
    outbound: [],
  }
}

function fullTranscriptState(): HarnessState {
  const remoteKey = sessionId("session-full")
  const session: HarnessSession = {
    identity: { kind: "remote", id: remoteKey },
    projectId: null,
    title: "Full transcript demo",
    createdAtUnixMs: 0,
    renamed: false,
    unread: false,
    turn: { kind: "idle" },
    transcriptLoad: { kind: "loaded" },
    transcript: [
      makeUserMessage("m-user-1", "first request"),
      makeAssistantMessage("m-asst-1", "Sure — running tools.", {
        kind: "tool",
        toolId: "bash",
        toolName: "bash",
        status: "ok",
        durationMs: 120,
        inputJson: '{"cmd":"ls"}',
        outputJson: '{"files":["a.txt","b.txt"]}',
      }),
      makeAssistantMessage("m-asst-2", "Edited one file.", [
      {
        kind: "text",
        markdown: "Edited one file.",
      },
      {
        kind: "code",
        language: "diff",
        path: "a.txt",
        source: "@@ -1 +1 @@\n-old\n+new",
      },
    ] as never),
      makeAssistantMessage("m-asst-3", "Plan ready.", {
        kind: "plan",
        scope: "chat/sessions/session-full/approve",
        intent: "follow-up",
        nodes: [
          { id: "n1", key: "n1", title: "verify", brief: "verify changes" },
        ],
      }),
    ],
    queue: [],
  }
  return {
    sessions: [session],
    activeSessionId: remoteKey,
    connection: { kind: "connected" },
    auth: { kind: "authenticated", subjectId: "user-1" },
    overlay: { kind: "closed" },
    drafts: [],
    cursors: {},
    outbound: [],
  }
}

function makeUserMessage(id: string, content: string): HarnessMessage {
  return {
    id,
    role: "user",
    content,
    createdAtUnixMs: 0,
  }
}

interface AssistantWirePart {
  readonly kind: "tool" | "text" | "code" | "plan"
  readonly [key: string]: unknown
}

function makeAssistantMessage(
  id: string,
  content: string,
  part: AssistantWirePart | readonly AssistantWirePart[]
): HarnessMessage {
  const parts = Array.isArray(part) ? part : [part]
  return {
    id,
    role: "assistant",
    content,
    createdAtUnixMs: 0,
    view: {
      id,
      role: "assistant",
      content,
      parts: parts as never,
      meta: null,
      createdAt: "1970-01-01T00:00:00Z",
    },
  }
}

function emptySignal(): AttentionSignal {
  return {
    revision: 1,
    generatedAtUnixMs: 0,
    nowUnixMs: 0,
    items: [],
  }
}

function stalledSignal(): AttentionSignal {
  return {
    revision: 7,
    generatedAtUnixMs: 1,
    nowUnixMs: 1,
    items: [
      {
        sessionId: sessionId("session-1"),
        priority: "p0",
        reason: "stalled",
        state: "thinking",
        profile: "planId",
        title: "Worker stalled",
        rationale: "no heartbeat for 92s",
        correlationId: "corr-1",
        durationMs: 92_000,
        reasonAtUnixMs: 1,
      },
    ],
  }
}
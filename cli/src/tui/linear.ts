/**
 * Linear screen-reader-friendly renderer (issue #79).
 *
 * The OpenTUI host paints styled lines with tone, weight, and
 * box-drawing characters; a screen reader, plain SSH terminal, or
 * agent harness cannot read those — it needs plain text with
 * semantic labels.
 *
 * `LinearRenderer` reads the SAME inputs the OpenTUI host consumes
 * (`StyledLine[]`, `HarnessState`, `AttentionSignal`) and emits
 * plain-text lines: `[#section]` markers, `>` bullets, numbered
 * choices, typed-confirmation prompts. No cursor positioning, no
 * ANSI colour codes, no box-drawing characters. Scrollback preserved.
 *
 * Glyph policy:
 * - `ascii` mode → `>`, `*`, `-`, `+`, `|`, `=`; sections as `[#foo]`.
 * - default (unicode) → `›`, `·`, `─`, `┌`, `│`, etc.
 *
 * Width clamping when `unicodeNarrow` is on: ambiguous-width CJK
 * characters count as 1 column (we never go below 1). Emoji ZWJ
 * sequences are skipped with a single per-session warning.
 *
 * Pure data: the host writes the returned lines to stdout in order.
 * No I/O at module load.
 */

import { TUI_NAMESPACE, tr, type I18nInstance } from "../locales"
import type { AttentionSignal } from "../kernel/attention"
import { activeSession } from "../harness/selectors"
import type {
  HarnessMessage,
  HarnessSession,
  HarnessState,
  SessionKey,
} from "../harness/state"
import {
  buildTranscriptEntries,
  entryLines,
  type EntryRenderContext,
} from "./entries"
import {
  deriveSwarmSummary,
  type SwarmRow,
  type SwarmSummary,
} from "./swarm"
import { pendingApprovalEntry } from "./approvals"
import { approvalDetail, approvalSummary, lineText, type StyledLine } from "./styled"
import type { ResolvedModes } from "./modes"

// ---------------------------------------------------------------------------
// Glyph policy — ascii / unicode
// ---------------------------------------------------------------------------

/**
 * The set of glyphs the linear renderer uses. ASCII mode replaces
 * the unicode defaults so a serial console, screen reader, or any
 * agent harness sees only `[#section]`, `>`, `*`, `+`, `-`, `|`,
 * `=` and friends.
 */
export interface GlyphSet {
  /** Section header marker — `[#foo]` → `# foo` (ascii) or `#foo` */
  readonly sectionPrefix: string
  readonly sectionSuffix: string
  /** Unordered list bullet — `·` → `*` (ascii) */
  readonly bullet: string
  /** Ordered list marker — `1.` (same in both, but kept here) */
  readonly enumerate: (index: number) => string
  /** Quote / indent bar — `│` → `|` (ascii) */
  readonly bar: string
  /** Horizontal rule — `─` → `-` (ascii) */
  readonly rule: string
  /** Top-of-frame — `┌` → `+` (ascii) */
  readonly topLeft: string
  readonly topRight: string
  /** Box sides — `│` → `|` */
  readonly side: string
  /** Bottom-of-frame — `└` → `+` */
  readonly bottomLeft: string
  readonly bottomRight: string
  /** Mid horizontal — `┬` → `+` */
  readonly teeDown: string
  /** Prompt glyph — `›` → `>` (ascii) */
  readonly prompt: string
  /** Emphasis — wraps a segment in `**` (markdown-friendly) */
  readonly emphasisOpen: string
  readonly emphasisClose: string
  /** Approval status — `✓` → `+` (ascii) */
  readonly approved: string
  /** Approval status — `✗` → `x` (ascii) */
  readonly rejected: string
}

const UNICODE_GLYPHS: GlyphSet = {
  sectionPrefix: "[#",
  sectionSuffix: "]",
  bullet: "·",
  enumerate: (index) => `${index}.`,
  bar: "│",
  rule: "─",
  topLeft: "┌",
  topRight: "┐",
  side: "│",
  bottomLeft: "└",
  bottomRight: "┘",
  teeDown: "┬",
  prompt: "›",
  emphasisOpen: "*",
  emphasisClose: "*",
  approved: "✓",
  rejected: "✗",
}

export const ASCII_GLYPHS: GlyphSet = {
  sectionPrefix: "[#",
  sectionSuffix: "]",
  bullet: "*",
  enumerate: (index) => `${index}.`,
  bar: "|",
  rule: "-",
  topLeft: "+",
  topRight: "+",
  side: "|",
  bottomLeft: "+",
  bottomRight: "+",
  teeDown: "+",
  prompt: ">",
  emphasisOpen: "*",
  emphasisClose: "*",
  approved: "+",
  rejected: "x",
}

export { UNICODE_GLYPHS }

/** Pick the glyph set the resolved modes ask for. */
export function pickGlyphs(modes: ResolvedModes): GlyphSet {
  return modes.ascii ? ASCII_GLYPHS : UNICODE_GLYPHS
}

// ---------------------------------------------------------------------------
// Renderer — pure, side-effect-free
// ---------------------------------------------------------------------------

export interface LinearRenderContext {
  readonly i18n: I18nInstance
  readonly modes: ResolvedModes
  readonly width: number
  /**
   * Per-session expansion state — same shape the OpenTUI host keeps.
   * Linear renderer does not toggle it; the host owns mutation.
   */
  readonly expanded: ReadonlyMap<SessionKey, ReadonlySet<string>>
}

/**
 * Build a renderer bound to one resolution. The renderer is a plain
 * object: each method is pure and reads the supplied state. Tests
 * build one with a synthetic `i18n` + `modes` and assert the output.
 */
export interface LinearRenderer {
  readonly glyphs: GlyphSet
  /** Top-bar chrome — equivalent to `topBarContent()`. */
  topBar(state: HarnessState, session: HarnessSession | null): string
  /**
   * Render the full transcript for the active session — message
   * echoes, assistant markdown, collapsible entries (collapsed by
   * default), approvals, queued line, failed alert.
   */
  transcript(
    state: HarnessState,
    session: HarnessSession | null,
    expandedOverride?: ReadonlySet<string>
  ): readonly string[]
  /** Render the swarm canvas — P0/P1/P2 with row + optional detail. */
  swarm(signal: AttentionSignal, sessions: ReadonlyMap<string, HarnessSession>): readonly string[]
  /** Banner line that names the resolved mode — first line of output. */
  modeBanner(): string
  /** Approve confirmation — `Approved: <scope>` line. */
  approvedConfirmation(scope: string): string
  /** Reject confirmation — `Rejected: <scope>` line. */
  rejectedConfirmation(scope: string): string
  /** Receipt-written confirmation — issued after `recordDecision`. */
  receiptWritten(sessionId: string): string
  /** The plain-text confirmation that an approval is awaiting. */
  awaitingDecision(scope: string): string
  /** Width-clamped wrapper for host-side writes. */
  clamp(text: string): string
}

export function createLinearRenderer(context: LinearRenderContext): LinearRenderer {
  const glyphs = pickGlyphs(context.modes)
  const width = Math.max(40, context.width)
  const i18n = context.i18n

  const emojiWarned = new Set<string>()

  function clamp(text: string): string {
    return clampWidth(text, width, context.modes.unicodeNarrow, emojiWarned)
  }

  /**
   * Replace Unicode glyphs with their ASCII equivalents when the
   * resolved modes ask for it. The OpenTUI i18n keys carry `·`,
   * `›`, etc.; in ASCII mode we swap them to `*`, `>`, etc.
   */
  function glyphify(text: string): string {
    if (!context.modes.ascii) {
      return text
    }
    return text
      .replace(/·/g, "*")
      .replace(/›/g, ">")
      .replace(/[┌┐└┘┬]/g, "+")
      .replace(/[─━]/g, "-")
      .replace(/│/g, "|")
      .replace(/✓/g, "+")
      .replace(/✗/g, "x")
  }

  function transcriptFor(
    state: HarnessState,
    session: HarnessSession | null,
    expandedOverride?: ReadonlySet<string>
  ): readonly string[] {
    if (session === null) {
      return [clamp(tr(i18n, "transcript.emptySession"))]
    }
    const expanded =
      expandedOverride ??
      context.expanded.get(session.identity.id) ??
      new Set<string>()
    const renderContext: EntryRenderContext = { i18n, width, expanded }
    const lines: string[] = []
    lines.push(clamp(section(tr(i18n, "transcript.linear.transcriptHeader"))))
    lines.push("")
    for (const entry of buildTranscriptEntries(session)) {
      for (const styled of entryLines(entry, renderContext)) {
        lines.push(clamp(glyphify(linearize(styled))))
      }
    }
    if (session.turn.kind === "awaiting-approval") {
      const last = lastAssistantMessage(session)
      const approval = pendingApprovalEntry({
        session,
        messageId: last?.id ?? "awaiting-approval",
        requester: last?.id ?? "",
        createdAtUnixMs: last?.createdAtUnixMs ?? 0,
        trailingMeta: last?.view?.meta ?? null,
      })
      if (approval !== null) {
        lines.push(clamp(glyphify(linearize(approvalSummary(approval, i18n)))))
        lines.push(awaitingDecisionText(approval.applicability.scope))
        for (const styled of approvalDetail(approval, renderContext)) {
          lines.push(clamp(glyphify(linearize(styled))))
        }
      }
    }
    if (session.turn.kind === "failed") {
      lines.push(
        clamp(
          `${tr(i18n, "transcript.failed")} ${session.turn.error.message}`
        )
      )
    }
    void state
    return lines
  }

  function swarmFor(
    signal: AttentionSignal,
    sessions: ReadonlyMap<string, HarnessSession>
  ): readonly string[] {
    const summary: SwarmSummary = deriveSwarmSummary(signal, {
      awaiting: tr(i18n, "transcript.swarm.awaiting"),
      stalled: tr(i18n, "transcript.swarm.stalled"),
      evidence: tr(i18n, "transcript.swarm.evidence"),
      failed: tr(i18n, "transcript.swarm.failed"),
      active: tr(i18n, "transcript.swarm.active"),
    })
    if (
      summary.p0.length === 0 &&
      summary.p1.length === 0 &&
      summary.p2.length === 0
    ) {
      return [clamp(tr(i18n, "transcript.linear.canvasEmpty"))]
    }
    const lines: string[] = []
    lines.push(clamp(section(tr(i18n, "transcript.linear.swarmHeader"))))
    lines.push("")
    for (const [bucket, label] of buckets(summary, i18n)) {
      if (bucket.length === 0) {
        continue
      }
      lines.push(clamp(section(glyphify(label))))
      for (const row of bucket) {
        lines.push(clamp(glyphify(rowSummary(row))))
        const detail = rowDetailText(row, sessions, i18n)
        for (const line of detail) {
          lines.push(clamp(glyphify(line)))
        }
      }
      lines.push("")
    }
    while (lines.length > 0 && lines[lines.length - 1] === "") {
      lines.pop()
    }
    return lines
  }

  return {
    glyphs,
    topBar(state, session) {
      const connection = connectionLabel(state, i18n)
      const title =
        session !== null && session.title.length > 0 ? ` ${glyphs.bullet} ${session.title}` : ""
      const chrome = tr(i18n, "chrome.titleCompact").trim()
      return clamp(`${chrome} ${glyphs.bullet} ${connection}${title}`)
    },
    transcript: transcriptFor,
    swarm: swarmFor,
    modeBanner() {
      return clamp(modeBannerText(context.modes, i18n, glyphs))
    },
    approvedConfirmation(scope) {
      return clamp(
        `${glyphs.approved} ${interpolate(i18n, "transcript.linear.approved", { scope })}`
      )
    },
    rejectedConfirmation(scope) {
      return clamp(
        `${glyphs.rejected} ${interpolate(i18n, "transcript.linear.rejected", { scope })}`
      )
    },
    receiptWritten(sessionId) {
      return clamp(
        interpolate(i18n, "transcript.linear.receiptWritten", { id: sessionId })
      )
    },
    awaitingDecision(scope) {
      return awaitingDecisionText(scope)
    },
    clamp,
  }

  function awaitingDecisionText(scope: string): string {
    return clamp(
      interpolate(i18n, "transcript.linear.awaiting", { scope })
    )
  }
}

// ---------------------------------------------------------------------------
// Helpers — pure
// ---------------------------------------------------------------------------

function section(label: string): string {
  return `[#${label}]`
}

function connectionLabel(state: HarnessState, i18n: I18nInstance): string {
  switch (state.connection.kind) {
    case "connected":
      return tr(i18n, "connection.connected")
    case "connecting":
      return tr(i18n, "connection.connecting")
    case "reconnecting":
      return tr(i18n, "connection.reconnecting")
    case "disconnected":
      return tr(i18n, "connection.disconnected")
  }
}

/**
 * The last assistant message on a session — used to attribute an
 * approval entry to the message that announced it. Mirrors the
 * host's `lastAssistantMessage` (issue #76).
 */
function lastAssistantMessage(session: HarnessSession): HarnessMessage | null {
  for (let index = session.transcript.length - 1; index >= 0; index -= 1) {
    const message = session.transcript[index]
    if (message !== undefined && message.role === "assistant") {
      return message
    }
  }
  return null
}

/**
 * Reduce a `StyledLine` (list of styled segments) to its plain text.
 * Pure ANSI-strip / weight-strip — no glyph translation. The host
 * applies that via the glyph set when rendering banners and bullet
 * markers around the plain text.
 */
export function linearize(line: StyledLine): string {
  return lineText(line)
}

/**
 * Width-clamp + unicode-narrow handling. `unicodeNarrow` mode treats
 * every CJK / ambiguous-width rune as 1 column (East Asian Width
 * would otherwise count some as 2). Emoji ZWJ sequences are stripped
 * with a per-session warning (collected via the `warned` set).
 */
export function clampWidth(
  text: string,
  width: number,
  unicodeNarrow: boolean,
  warned?: Set<string>
): string {
  if (text.length <= width) {
    return text
  }
  const narrowWidth = (s: string): number => {
    if (!unicodeNarrow) {
      return s.length
    }
    let cols = 0
    for (const ch of s) {
      // East Asian Wide / Fullwidth → 1 column in narrow mode.
      // Use code-point heuristic: ambiguous-width range.
      const cp = ch.codePointAt(0) ?? 0
      if (cp >= 0x1100 && cp <= 0x115f) {
        cols += 1
      } else if (cp >= 0x2e80 && cp <= 0x9fff) {
        cols += 1
      } else if (cp >= 0xac00 && cp <= 0xd7a3) {
        cols += 1
      } else if (cp >= 0xff00 && cp <= 0xff60) {
        cols += 1
      } else if (cp >= 0xffe0 && cp <= 0xffe6) {
        cols += 1
      } else if (cp >= 0x20000 && cp <= 0x2fffd) {
        cols += 1
      } else if (cp >= 0x30000 && cp <= 0x3fffd) {
        cols += 1
      } else {
        cols += ch.length > 1 ? 2 : 1
      }
    }
    return cols
  }
  // Skip ZWJ emoji sequences (those start with high-surrogate that
  // doesn't pair cleanly). We do a best-effort sweep; one warning
  // per session keeps the test output stable.
  if (text.includes("\u200d")) {
    if (warned !== undefined && !warned.has("zwj")) {
      warned.add("zwj")
      console.warn(
        "linear renderer: emoji ZWJ sequence detected and stripped (one warning per session)"
      )
    }
    text = text.replace(/\u200d/g, "")
  }
  const used = narrowWidth(text)
  if (used <= width) {
    return text
  }
  // Hard-truncate by characters, leaving 1 col for the ellipsis.
  let lo = 0
  let hi = text.length
  let best = text
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    const slice = text.slice(0, mid)
    if (narrowWidth(slice) <= width - 1) {
      best = slice
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return `${best}…`
}

function interpolate(
  i18n: I18nInstance,
  key: string,
  params: Record<string, string | number>
): string {
  const value = i18n.t(key, { ns: TUI_NAMESPACE, ...params })
  if (typeof value === "string" && value.length > 0 && value !== key) {
    return value
  }
  throw new Error(
    `i18n: missing or empty key '${key}' in locale '${i18n.language}' (namespace '${TUI_NAMESPACE}')`
  )
}

// Reserved hook for future densities. The transcript renderer now
// calls `awaitingDecisionText(scope)` directly.

function rowSummary(row: SwarmRow): string {
  const badge = row.badge.length > 0 ? ` [${row.badge}]` : ""
  const id = row.correlationId.length > 0 ? ` (${row.correlationId})` : ""
  return `${row.headline}${badge}${id}`
}

function buckets(
  summary: SwarmSummary,
  i18n: I18nInstance
): readonly (readonly [readonly SwarmRow[], string])[] {
  return [
    [summary.p0, tr(i18n, "transcript.swarm.priority.p0")],
    [summary.p1, tr(i18n, "transcript.swarm.priority.p1")],
    [summary.p2, tr(i18n, "transcript.swarm.priority.p2")],
  ] as const
}

function rowDetailText(
  row: SwarmRow,
  sessions: ReadonlyMap<string, HarnessSession>,
  i18n: I18nInstance
): readonly string[] {
  const detail = row.detail
  if (detail === null) {
    return []
  }
  const lines: string[] = []
  const session = sessions.get(row.sessionId)
  switch (detail.kind) {
    case "awaiting-approval":
      lines.push(`  scope: ${detail.scope}`)
      for (const node of detail.planNodes) {
        lines.push(`    ${node.id} ${node.profile} → ${node.brief}`)
      }
      void session
      break
    case "stalled":
      lines.push(
        `  ${interpolate(i18n, "transcript.swarm.leaseHint", { seconds: detail.seconds })}`
      )
      lines.push(`  reason: ${detail.reason}`)
      break
    case "evidence":
      lines.push(`  ${detail.hint}`)
      break
    case "failed":
      lines.push(
        `  ${interpolate(i18n, "transcript.swarm.failureHint", { code: detail.code.length > 0 ? detail.code : "error" })}`
      )
      if (detail.message.length > 0) {
        lines.push(`  message: ${detail.message}`)
      }
      break
    case "active":
      lines.push(`  ${tr(i18n, "transcript.swarm.active")}`)
      break
  }
  return lines
}

function modeBannerText(
  modes: ResolvedModes,
  i18n: I18nInstance,
  glyphs: GlyphSet
): string {
  const flags = [
    `linear=${modes.linear ? "on" : "off"}`,
    `reduced-motion=${modes.reducedMotion ? "on" : "off"}`,
    `high-contrast=${modes.highContrast ? "on" : "off"}`,
    `no-color=${modes.noColor ? "on" : "off"}`,
    `ascii=${modes.ascii ? "on" : "off"}`,
    `no-mouse=${modes.noMouse ? "on" : "off"}`,
    `unicode-narrow=${modes.unicodeNarrow ? "on" : "off"}`,
    `machine=${modes.machine ? "on" : "off"}`,
  ]
  void glyphs
  const template = tr(i18n, "transcript.linear.mode")
  // The mode key uses {{flags}} placeholder; substitute.
  const merged = template.replace("{{flags}}", flags.join(", "))
  return merged
}

// ---------------------------------------------------------------------------
// Public surface — kept at the bottom for readability
// ---------------------------------------------------------------------------

/** The active-session selector re-exported so callers don't import it twice. */
export { activeSession }
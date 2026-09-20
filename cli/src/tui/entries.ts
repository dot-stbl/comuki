/**
 * Structured transcript entries with progressive disclosure (issue #74).
 *
 * Pure view-model: `HarnessMessage` + the live `TurnState` map to a
 * flat list of typed entries — message echoes, assistant markdown,
 * thinking, grouped tool calls, code, diff, plan, handoff and the
 * live streaming entry. Every collapsible entry answers WHAT
 * happened and its CURRENT state on one summary line; the expandable
 * detail block reveals the exact payload, timestamps, correlation id
 * and model meta, reporting omitted-line counts when truncated.
 *
 * Rendering-behavior ports of `lib/format.ts` (collapsed summaries,
 * tool-arg summaries, duration/token formatting) live here as pure
 * functions; the markdown/diff line renderers live in `./styled.ts`.
 * No OpenTUI imports — the host renders the styled lines.
 */

import type { HarnessMessage, HarnessSession } from "../harness/state"
import { TUI_NAMESPACE, tr, type I18nInstance } from "../locales"
import {
  blankLine,
  codeFrameLines,
  diffLines,
  isDiffContent,
  markdownLines,
  seg,
  truncateTail,
  wrapSegments,
  type Segment,
  type SegmentStyle,
  type StyledLine,
  type Tone,
} from "./styled"

// ---------------------------------------------------------------------------
// Wire shapes — indexed off the harness state (tui/ never imports ../lib)
// ---------------------------------------------------------------------------

type WireView = NonNullable<HarnessMessage["view"]>
type WirePart = NonNullable<WireView["parts"]>[number]
export type WirePlanItem = Extract<WirePart, { kind: "plan" }>["nodes"][number]
export type PlanEdgeView = Extract<WirePart, { kind: "plan" }>["edges"][number]
export type WireMeta = NonNullable<WireView["meta"]>

// ---------------------------------------------------------------------------
// Entry model
// ---------------------------------------------------------------------------

/** Aggregate state of an entry — colour never carries it alone. */
export type EntryState = "ok" | "error" | "running"

interface EntryBase {
  /** Stable identity across renders — expansion state keys on it. */
  readonly id: string
  readonly createdAtUnixMs: number
  /** Message correlation id — revealed in the expanded detail. */
  readonly messageId: string
  /** Wire meta of the source message when this is its last entry. */
  readonly trailingMeta: WireMeta | null
}

export interface MessageEntry extends EntryBase {
  readonly kind: "message"
  readonly role: "user" | "system" | "tool"
  readonly content: string
  readonly toolName: string | null
}

export interface AssistantEntry extends EntryBase {
  readonly kind: "assistant"
  readonly markdown: string
  /** Role prefix on the first line — set on the first text entry of the message. */
  readonly prefix: boolean
}

export interface ThinkingEntry extends EntryBase {
  readonly kind: "thinking"
  readonly text: string
  readonly tokens: number | null
}

/** One tool call inside a group — the wire `tool` part. */
export interface ToolCall {
  readonly name: string
  readonly inputJson: string
  readonly status: string
  readonly outputJson: string | null
  readonly durationMs: number | null
}

export interface ToolGroupEntry extends EntryBase {
  readonly kind: "tool-group"
  readonly tools: readonly ToolCall[]
}

export interface CodeEntry extends EntryBase {
  readonly kind: "code"
  readonly language: string
  readonly source: string
  readonly path: string | null
  readonly startLine: number | null
}

export interface DiffEntry extends EntryBase {
  readonly kind: "diff"
  readonly source: string
}

export interface PlanEntry extends EntryBase {
  readonly kind: "plan"
  readonly nodes: readonly WirePlanItem[]
  readonly edges: readonly PlanEdgeView[]
}

export interface HandoffEntry extends EntryBase {
  readonly kind: "handoff"
  readonly query: string
}

/** The live thinking turn's text — never collapsible. */
export interface StreamingEntry extends EntryBase {
  readonly kind: "streaming"
  readonly text: string
}

export type TranscriptEntry =
  | MessageEntry
  | AssistantEntry
  | ThinkingEntry
  | ToolGroupEntry
  | CodeEntry
  | DiffEntry
  | PlanEntry
  | HandoffEntry
  | StreamingEntry

// ---------------------------------------------------------------------------
// Truncation budgets (exported for tests)
// ---------------------------------------------------------------------------

/** Max pretty-printed lines one tool payload shows in detail. */
export const MAX_PAYLOAD_LINES = 200

/** Max body lines a code/diff detail shows. */
export const MAX_BODY_LINES = 400

// ---------------------------------------------------------------------------
// Formatting ports (lib/format.ts behavior)
// ---------------------------------------------------------------------------

/**
 * Positional argument summary with a total char budget (default 40):
 * strings quoted, numbers/booleans bare, arrays count as `n key`,
 * nested objects stay silent. Broken json → empty string.
 */
export function summarizeToolArgs(inputJson: string, maxChars = 40): string {
  let input: Record<string, unknown>
  try {
    input = JSON.parse(inputJson) as Record<string, unknown>
  } catch {
    return ""
  }
  const pieces: string[] = []
  let used = 0
  for (const [key, value] of Object.entries(input)) {
    if (value === null || value === undefined) {
      continue
    }
    let piece: string
    if (typeof value === "string") {
      piece = JSON.stringify(value)
    } else if (typeof value === "number" || typeof value === "boolean") {
      piece = String(value)
    } else if (Array.isArray(value)) {
      piece = `${value.length} ${key}`
    } else {
      continue
    }
    const separator = pieces.length > 0 ? 2 : 0
    if (used + separator + piece.length > maxChars) {
      const room = maxChars - used - separator
      if (room > 1) {
        pieces.push(piece.slice(0, room - 1) + "…")
      }
      break
    }
    pieces.push(piece)
    used += separator + piece.length
  }
  return pieces.join(", ")
}

/** The kubb-generated wire types allow `number | string` for nullable
 * integers (OpenAPI quirk: `int32?` serialises as `(number | string) | null`).
 * Internal entry fields are plain `number | null`; coerce at the boundary
 * and drop anything that doesn't parse to a non-negative integer. */
function toNumberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null
  const n = typeof value === "number" ? value : Number(value)
  if (!Number.isFinite(n) || n < 0) return null
  return n
}

/** `120ms`, `3.4s`, `2m 5s` — compact durations for summary lines. */
export function formatDurationMs(durationMs: number): string {
  if (durationMs < 1000) {
    return `${Math.round(durationMs)}ms`
  }
  if (durationMs < 60_000) {
    return `${(durationMs / 1000).toFixed(1)}s`
  }
  const minutes = Math.floor(durationMs / 60_000)
  const seconds = Math.round((durationMs % 60_000) / 1000)
  return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`
}

/** `40 tok`, `1.2k tok` — thinking size when duration is unknown. */
export function formatTokenCount(tokens: number): string {
  return tokens < 1000 ? `${tokens} tok` : `${(tokens / 1000).toFixed(1)}k tok`
}

/** Wire tool status → entry state (`failed`/`error` → error, `running` → running, else ok). */
export function toolEntryState(status: string): EntryState {
  const lowered = status.toLowerCase()
  if (lowered === "failed" || lowered === "error") {
    return "error"
  }
  if (lowered === "running" || lowered === "pending") {
    return "running"
  }
  return "ok"
}

function stateTone(state: EntryState): Tone {
  if (state === "error") {
    return "error"
  }
  if (state === "running") {
    return "accent"
  }
  return "ok"
}

/** `HH:MM:SS` (UTC) — deterministic timestamp for detail lines. */
export function utcClock(unixMs: number): string {
  return new Date(unixMs).toISOString().slice(11, 19)
}

// ---------------------------------------------------------------------------
// i18n helpers
// ---------------------------------------------------------------------------

/** `tr` with interpolation params — same loud-miss contract. */
function trParams(
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

/** Role prefix labels — the `transcript.*` keys view.ts introduced. */
export function rolePrefixLabel(
  role: "user" | "assistant" | "system" | "tool",
  i18n: I18nInstance
): string {
  switch (role) {
    case "user":
      return tr(i18n, "transcript.you")
    case "assistant":
      return tr(i18n, "transcript.comuki")
    case "system":
      return tr(i18n, "transcript.system")
    case "tool":
      return tr(i18n, "transcript.tool")
  }
}

// ---------------------------------------------------------------------------
// Message → entries
// ---------------------------------------------------------------------------

/** The streaming entry's stable id. */
export const STREAMING_ENTRY_ID = "live#streaming"

/**
 * The active session's transcript + live turn → the flat entry list.
 * Pure: stable ids (`<messageId>#<marker>`), consecutive tool parts
 * of one message grouped into a single entry.
 */
export function buildTranscriptEntries(
  session: HarnessSession | null
): readonly TranscriptEntry[] {
  if (session === null) {
    return []
  }
  const entries: TranscriptEntry[] = []
  for (const message of session.transcript) {
    entries.push(...messageEntries(message))
  }
  if (session.turn.kind === "thinking") {
    entries.push({
      kind: "streaming",
      id: STREAMING_ENTRY_ID,
      createdAtUnixMs: 0,
      messageId: "",
      trailingMeta: null,
      text: session.turn.accumulatedText,
    })
  }
  return entries
}

function messageEntries(message: HarnessMessage): readonly TranscriptEntry[] {
  const base = {
    id: message.id,
    createdAtUnixMs: message.createdAtUnixMs,
    messageId: message.id,
    trailingMeta: null as WireMeta | null,
  }
  if (message.role !== "assistant") {
    return [
      {
        ...base,
        kind: "message",
        role: message.role,
        content: message.content,
        toolName: message.view?.toolName ?? null,
      },
    ]
  }
  const parts = message.view?.parts ?? null
  const fallback: readonly TranscriptEntry[] = [
    {
      ...base,
      kind: "assistant",
      markdown: message.content,
      prefix: message.content.trim().length > 0,
      trailingMeta: message.view?.meta ?? null,
    },
  ]
  if (parts === null || parts.length === 0) {
    return fallback
  }
  const decomposed = partEntries(parts, message.id, message.createdAtUnixMs)
  if (decomposed.length === 0) {
    return fallback
  }
  // The first assistant-text entry of the message carries the role
  // prefix; the last entry of the message carries the meta tail.
  for (let index = 0; index < decomposed.length; index += 1) {
    const entry = decomposed[index]
    if (entry !== undefined && entry.kind === "assistant") {
      decomposed[index] = { ...entry, prefix: true }
      break
    }
  }
  const lastIndex = decomposed.length - 1
  const last = decomposed[lastIndex]
  if (last !== undefined) {
    decomposed[lastIndex] = { ...last, trailingMeta: message.view?.meta ?? null }
  }
  return decomposed
}

function partEntries(
  parts: readonly WirePart[],
  messageId: string,
  createdAtUnixMs: number
): TranscriptEntry[] {
  const entries: TranscriptEntry[] = []
  const stamp = { createdAtUnixMs, messageId, trailingMeta: null as WireMeta | null }
  let toolRun: ToolCall[] = []
  const flushTools = (): void => {
    if (toolRun.length === 0) {
      return
    }
    entries.push({
      kind: "tool-group",
      id: `${messageId}#tools${entries.length}`,
      ...stamp,
      tools: toolRun,
    })
    toolRun = []
  }
  parts.forEach((part, index) => {
    switch (part.kind) {
      case "tool":
        toolRun.push({
          name: part.name,
          inputJson: part.inputJson,
          status: part.status,
          outputJson: part.outputJson ?? null,
          durationMs: toNumberOrNull(part.durationMs),
        })
        return
      case "thinking":
        flushTools()
        entries.push({
          kind: "thinking",
          id: `${messageId}#p${index}`,
          ...stamp,
          text: part.text,
          tokens: toNumberOrNull(part.tokens),
        })
        return
      case "text":
        flushTools()
        entries.push({
          kind: "assistant",
          id: `${messageId}#p${index}`,
          ...stamp,
          markdown: part.markdown,
          prefix: false,
        })
        return
      case "code":
        flushTools()
        if (isDiffContent(part.language, part.source)) {
          entries.push({
            kind: "diff",
            id: `${messageId}#p${index}`,
            ...stamp,
            source: part.source,
          })
        } else {
          entries.push({
            kind: "code",
            id: `${messageId}#p${index}`,
            ...stamp,
            language: part.language,
            source: part.source,
            path: part.path ?? null,
            startLine: toNumberOrNull(part.startLine),
          })
        }
        return
      case "diagram":
        flushTools()
        entries.push({
          kind: "code",
          id: `${messageId}#p${index}`,
          ...stamp,
          language: part.dialect,
          source: part.source,
          path: null,
          startLine: null,
        })
        return
      case "plan":
        flushTools()
        entries.push({
          kind: "plan",
          id: `${messageId}#p${index}`,
          ...stamp,
          nodes: part.nodes,
          edges: part.edges,
        })
        return
      case "handoff":
        flushTools()
        entries.push({
          kind: "handoff",
          id: `${messageId}#p${index}`,
          ...stamp,
          query: part.query,
        })
        return
    }
  })
  flushTools()
  return entries
}

// ---------------------------------------------------------------------------
// Collapsibility
// ---------------------------------------------------------------------------

/** Entries whose detail an explicit action can reveal. */
export function entryCollapsible(entry: TranscriptEntry): boolean {
  switch (entry.kind) {
    case "thinking":
    case "tool-group":
    case "code":
    case "diff":
    case "plan":
      return true
    default:
      return false
  }
}

/** Every collapsible entry id, in transcript order. */
export function collapsibleIds(
  entries: readonly TranscriptEntry[]
): readonly string[] {
  return entries.filter(entryCollapsible).map((entry) => entry.id)
}

/** The LAST collapsible entry id — ctrl+o's target. */
export function lastCollapsibleId(
  entries: readonly TranscriptEntry[]
): string | null {
  for (let index = entries.length - 1; index >= 0; index -= 1) {
    const entry = entries[index]!
    if (entryCollapsible(entry)) {
      return entry.id
    }
  }
  return null
}

// ---------------------------------------------------------------------------
// Entry → styled lines
// ---------------------------------------------------------------------------

export interface EntryRenderContext {
  readonly i18n: I18nInstance
  readonly width: number
  readonly expanded: ReadonlySet<string>
}

/** The `* ` event marker every collapsed/expanded entry header leads with. */
const EVENT_MARK = "* "

/** Faint detail-exists marker appended to a collapsed collapsible line. */
const DETAIL_MARKER = " …"

/**
 * One entry → styled lines. Collapsed collapsible entries render the
 * summary line only; expanded entries keep the summary as the header
 * of the revealed block (the Ink contract).
 */
export function entryLines(
  entry: TranscriptEntry,
  context: EntryRenderContext
): StyledLine[] {
  const lines = entryBodyLines(entry, context)
  if (entry.trailingMeta !== null) {
    lines.push(...metaLines(entry.trailingMeta, context.i18n))
  }
  return lines
}

function entryBodyLines(
  entry: TranscriptEntry,
  context: EntryRenderContext
): StyledLine[] {
  switch (entry.kind) {
    case "message":
      return messageLines(entry, context)
    case "assistant":
      return assistantLines(entry, context)
    case "thinking":
      return collapsibleEntryLines(entry, context, thinkingSummary, thinkingDetail)
    case "tool-group":
      return collapsibleEntryLines(entry, context, toolGroupSummary, toolGroupDetail)
    case "code":
      return collapsibleEntryLines(entry, context, codeSummary, codeDetail)
    case "diff":
      return collapsibleEntryLines(entry, context, diffSummary, diffDetail)
    case "plan":
      return collapsibleEntryLines(entry, context, planSummary, planDetail)
    case "handoff":
      return handoffLines(entry, context.i18n)
    case "streaming":
      return streamingLines(entry, context)
  }
}

// -- plain messages ---------------------------------------------------------

function messageLines(entry: MessageEntry, context: EntryRenderContext): StyledLine[] {
  const i18n = context.i18n
  if (entry.role === "user") {
    return prefixedBlock(
      [seg(rolePrefixLabel("user", i18n), "muted"), seg(" ", "muted")],
      entry.content.split("\n"),
      "text",
      { bold: true },
      context.width
    )
  }
  // system / tool journal rows — one quiet muted line (the Ink shape).
  const label = entry.toolName ?? rolePrefixLabel(entry.role, i18n)
  const body = entry.content.trim()
  const tail = body.length > 0 ? `: ${truncateTail(body, 72)}` : ""
  return [[seg(EVENT_MARK, "faint"), seg(`${label}${tail}`, "muted")]]
}

/**
 * `prefix first-line` + wrapped continuation — the `pushPrefixed`
 * port over styled segments: existing newlines split paragraphs,
 * long paragraphs word-wrap, a lone overlong word hard-splits.
 */
function prefixedBlock(
  prefix: StyledLine,
  paragraphs: readonly string[],
  tone: Tone,
  style: SegmentStyle,
  width: number
): StyledLine[] {
  const prefixText = prefix.map((segment) => segment.text).join("")
  const room = Math.max(8, width - prefixText.length - 1)
  const continuation = [seg(" ".repeat(prefixText.length + 1), tone)]
  const lines: StyledLine[] = []
  let isFirstLine = true
  for (const paragraph of paragraphs) {
    const pieces =
      paragraph.length > 0 ? wrapSegments([seg(paragraph, tone, style)], room) : []
    if (pieces.length === 0) {
      lines.push(isFirstLine ? [...prefix, seg(" ", tone)] : blankLine())
      isFirstLine = false
      continue
    }
    for (const piece of pieces) {
      lines.push(isFirstLine ? [...prefix, ...piece] : [...continuation, ...piece])
      isFirstLine = false
    }
  }
  return lines
}

// -- assistant markdown -----------------------------------------------------

function assistantLines(
  entry: AssistantEntry,
  context: EntryRenderContext
): StyledLine[] {
  const prefixLabel = rolePrefixLabel("assistant", context.i18n)
  const room = entry.prefix
    ? Math.max(8, context.width - prefixLabel.length - 1)
    : context.width
  const rendered = markdownLines(entry.markdown, room)
  if (rendered.length === 0) {
    return []
  }
  if (!entry.prefix) {
    return rendered
  }
  const prefix = [seg(prefixLabel, "muted"), seg(" ", "muted")]
  return [[...prefix, ...rendered[0]!], ...rendered.slice(1)]
}

/** Trailing dim meta line — `model …, in→out tok` (the Ink cost line). */
function metaLines(meta: WireMeta, _i18n: I18nInstance): StyledLine[] {
  if (typeof meta.model !== "string" || meta.model.length === 0) {
    return []
  }
  const tokens =
    typeof meta.tokensIn === "number" && typeof meta.tokensOut === "number"
      ? `, ${meta.tokensIn}→${meta.tokensOut} tok`
      : ""
  return [[seg(`  · ${meta.model}${tokens}`, "faint")]]
}

// -- collapsible entry skeleton ---------------------------------------------

interface SummaryFn<E extends TranscriptEntry> {
  (entry: E, i18n: I18nInstance): StyledLine
}

interface DetailFn<E extends TranscriptEntry> {
  (entry: E, context: EntryRenderContext): StyledLine[]
}

function collapsibleEntryLines<E extends TranscriptEntry>(
  entry: E,
  context: EntryRenderContext,
  summary: SummaryFn<E>,
  detail: DetailFn<E>
): StyledLine[] {
  const header = summary(entry, context.i18n)
  if (!context.expanded.has(entry.id)) {
    return [withDetailMarker(header)]
  }
  return [header, ...detail(entry, context)]
}

/** Appends the faint `…` detail-exists marker to a collapsed line. */
function withDetailMarker(line: StyledLine): StyledLine {
  return [...line, seg(DETAIL_MARKER, "faint")]
}

/** Detail preamble: timestamp + correlation id (UTC clock, stable). */
function detailStamp(entry: EntryBase, i18n: I18nInstance): StyledLine {
  const at =
    entry.createdAtUnixMs > 0
      ? ` ${tr(i18n, "transcript.entry.at")} ${utcClock(entry.createdAtUnixMs)}`
      : ""
  return [
    seg("    ", "faint"),
    seg(`${at}${at.length > 0 ? " " : ""}${tr(i18n, "transcript.entry.id")} ${entry.messageId}`, "faint"),
  ]
}

// -- thinking ----------------------------------------------------------------

function thinkingSummary(entry: ThinkingEntry, i18n: I18nInstance): StyledLine {
  const segments: Segment[] = [
    seg(EVENT_MARK, "faint"),
    seg(tr(i18n, "transcript.entry.thinking"), "muted"),
  ]
  if (entry.tokens !== null && entry.tokens > 0) {
    segments.push(seg(`  ${formatTokenCount(entry.tokens)}`, "faint"))
  }
  return segments
}

function thinkingDetail(
  entry: ThinkingEntry,
  context: EntryRenderContext
): StyledLine[] {
  const body = markdownLines(entry.text, Math.max(8, context.width - 4)).map(
    (line) => [seg("    ", "faint"), ...line.map((s) => seg(s.text, "muted", s.style))]
  )
  return [detailStamp(entry, context.i18n), ...body]
}

// -- tool group --------------------------------------------------------------

function stateLabel(state: EntryState, i18n: I18nInstance): string {
  switch (state) {
    case "ok":
      return tr(i18n, "transcript.entry.stateOk")
    case "error":
      return tr(i18n, "transcript.entry.stateError")
    case "running":
      return tr(i18n, "transcript.entry.stateRunning")
  }
}

/** Aggregate group state: any error → error, any running → running, else ok. */
export function toolGroupState(tools: readonly ToolCall[]): EntryState {
  const states = tools.map((tool) => toolEntryState(tool.status))
  if (states.includes("error")) {
    return "error"
  }
  if (states.includes("running")) {
    return "running"
  }
  return "ok"
}

function toolSummaryLine(tool: ToolCall, i18n: I18nInstance): StyledLine {
  const args = summarizeToolArgs(tool.inputJson)
  const call = args.length > 0 ? `${tool.name}(${args})` : tool.name
  const state = toolEntryState(tool.status)
  const duration = tool.durationMs !== null ? ` ${formatDurationMs(tool.durationMs)}` : ""
  return [
    seg(EVENT_MARK, "faint"),
    seg(`${tr(i18n, "transcript.entry.tool")} `, "muted"),
    seg(call, "muted"),
    seg(` — ${stateLabel(state, i18n)}`, stateTone(state)),
    seg(duration, "faint"),
  ]
}

function toolGroupSummary(entry: ToolGroupEntry, i18n: I18nInstance): StyledLine {
  if (entry.tools.length === 1) {
    return toolSummaryLine(entry.tools[0]!, i18n)
  }
  const counts = new Map<EntryState, number>()
  for (const tool of entry.tools) {
    const state = toolEntryState(tool.status)
    counts.set(state, (counts.get(state) ?? 0) + 1)
  }
  const parts: string[] = []
  for (const state of ["error", "running", "ok"] as const) {
    const count = counts.get(state)
    if (count !== undefined) {
      parts.push(
        count === entry.tools.length
          ? stateLabel(state, i18n)
          : `${count} ${stateLabel(state, i18n)}`
      )
    }
  }
  return [
    seg(EVENT_MARK, "faint"),
    seg(`${tr(i18n, "transcript.entry.tools")} ×${entry.tools.length} — `, "muted"),
    seg(parts.join(" · "), toolGroupState(entry.tools) === "error" ? "error" : "muted"),
  ]
}

function toolGroupDetail(
  entry: ToolGroupEntry,
  context: EntryRenderContext
): StyledLine[] {
  const lines: StyledLine[] = [detailStamp(entry, context.i18n)]
  entry.tools.forEach((tool, index) => {
    if (entry.tools.length > 1) {
      lines.push([seg(`    ${index + 1}. `, "faint"), seg(tool.name, "muted")])
    }
    lines.push(...jsonBlock("transcript.entry.input", tool.inputJson, context.i18n))
    if (tool.outputJson !== null && tool.outputJson.length > 0) {
      lines.push(...jsonBlock("transcript.entry.output", tool.outputJson, context.i18n))
    }
  })
  return lines
}

/** Pretty-printed JSON payload, capped; reports omitted lines when truncated. */
function jsonBlock(labelKey: string, json: string, i18n: I18nInstance): StyledLine[] {
  let body = json
  try {
    body = JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    // Not parseable json — the raw payload is the honest view.
  }
  const allLines = body.split("\n")
  const lines: StyledLine[] = [
    [seg(`    ${tr(i18n, labelKey)}:`, "faint")],
  ]
  const shown = allLines.slice(0, MAX_PAYLOAD_LINES)
  for (const line of shown) {
    lines.push([seg(`    ${line}`, "muted")])
  }
  const omitted = allLines.length - shown.length
  if (omitted > 0) {
    lines.push([
      seg(
        `    ${trParams(i18n, "transcript.entry.omitted", { lines: omitted })}`,
        "waiting"
      ),
    ])
  }
  return lines
}

// -- code ---------------------------------------------------------------------

function codeSummary(entry: CodeEntry, i18n: I18nInstance): StyledLine {
  const anchor =
    entry.path !== null
      ? ` ${entry.path}${entry.startLine !== null ? `:${entry.startLine}` : ""}`
      : ""
  const lineCount = entry.source.split("\n").length
  return [
    seg(EVENT_MARK, "faint"),
    seg(
      `${tr(i18n, "transcript.entry.code")} ${entry.language}${anchor} · ${trParams(i18n, "transcript.entry.lineCount", { lines: lineCount })}`,
      "muted"
    ),
  ]
}

function codeDetail(entry: CodeEntry, context: EntryRenderContext): StyledLine[] {
  const stamp = detailStamp(entry, context.i18n)
  const bodyLines = entry.source.replace(/\n+$/, "").split("\n")
  const shown = bodyLines.slice(0, MAX_BODY_LINES)
  const lines: StyledLine[] = [
    stamp,
    ...codeFrameLines(entry.language, shown.join("\n"), context.width),
  ]
  const omitted = bodyLines.length - shown.length
  if (omitted > 0) {
    lines.push([
      seg(`  ${trParams(context.i18n, "transcript.entry.omitted", { lines: omitted })}`, "waiting"),
    ])
  }
  return lines
}

// -- diff ---------------------------------------------------------------------

/** Added/deleted counts by line marker (context and headers ignored). */
export function diffCounts(source: string): { readonly added: number; readonly removed: number } {
  let added = 0
  let removed = 0
  for (const line of source.split("\n")) {
    if (line.startsWith("+") && !line.startsWith("+++")) {
      added += 1
    } else if (line.startsWith("-") && !line.startsWith("---")) {
      removed += 1
    }
  }
  return { added, removed }
}

/** Best-effort file path from a unified-diff header (`+++ b/path`). */
export function diffPath(source: string): string | null {
  for (const line of source.split("\n")) {
    if (line.startsWith("+++ b/")) {
      return line.slice(6).trim() || null
    }
  }
  return null
}

function diffSummary(entry: DiffEntry, i18n: I18nInstance): StyledLine {
  const path = diffPath(entry.source)
  const counts = diffCounts(entry.source)
  const where = path !== null ? ` ${path}` : ""
  return [
    seg(EVENT_MARK, "faint"),
    seg(`${tr(i18n, "transcript.entry.diff")}${where} `, "muted"),
    seg(`+${counts.added}`, "ok"),
    seg(" ", "muted"),
    seg(`−${counts.removed}`, "error"),
  ]
}

function diffDetail(entry: DiffEntry, context: EntryRenderContext): StyledLine[] {
  const bodyLines = entry.source.replace(/\n+$/, "").split("\n")
  const shown = bodyLines.slice(0, MAX_BODY_LINES)
  const lines: StyledLine[] = [
    detailStamp(entry, context.i18n),
    ...diffLines(shown.join("\n"), Math.max(8, context.width - 2)).map((line) =>
      line.map((s) => seg(`  ${s.text}`, s.tone, s.style))
    ),
  ]
  const omitted = bodyLines.length - shown.length
  if (omitted > 0) {
    lines.push([
      seg(`  ${trParams(context.i18n, "transcript.entry.omitted", { lines: omitted })}`, "waiting"),
    ])
  }
  return lines
}

// -- plan ---------------------------------------------------------------------

function planSummary(entry: PlanEntry, i18n: I18nInstance): StyledLine {
  return [
    seg(EVENT_MARK, "faint"),
    seg(
      `${tr(i18n, "transcript.entry.plan")} · ${trParams(i18n, "transcript.entry.nodeCount", { nodes: entry.nodes.length })}`,
      "muted"
    ),
  ]
}

function planDetail(entry: PlanEntry, context: EntryRenderContext): StyledLine[] {
  const i18n = context.i18n
  const lines: StyledLine[] = [detailStamp(entry, i18n)]
  // Map each node to its incoming edges (upstream node ids) so the detail
  // line for a node can show `<- from, from` once. The wire stores edges
  // as `{from, to}` pairs, not as `dependsOn` on the node — that shape
  // moved in PR #114 (generated plan contract).
  const incoming = new Map<string, string[]>()
  for (const edge of entry.edges) {
    const list = incoming.get(edge.to) ?? []
    list.push(edge.from)
    incoming.set(edge.to, list)
  }
  for (const node of entry.nodes) {
    const brief = node.brief.split("\n")[0]?.trim() || "(no brief)"
    const from = incoming.get(node.id)
    const deps =
      from && from.length > 0 ? [seg(`  <- ${from.join(", ")}`, "faint")] : []
    lines.push([
      seg("    . ", "accent"),
      seg(node.id, "faint"),
      seg(" ", "faint"),
      seg(node.profileKey, "text", { bold: true }),
      seg(" -> ", "faint"),
      seg(brief, "muted"),
      ...deps,
    ])
  }
  return lines
}

// -- handoff ------------------------------------------------------------------

function handoffLines(entry: HandoffEntry, i18n: I18nInstance): StyledLine[] {
  return [
    [
      seg("  -> ", "accent"),
      seg(`${tr(i18n, "transcript.entry.handoff")} `, "muted"),
      seg(entry.query, "text"),
    ],
  ]
}

// -- streaming ----------------------------------------------------------------

function streamingLines(
  entry: StreamingEntry,
  context: EntryRenderContext
): StyledLine[] {
  const prefix = [seg(tr(context.i18n, "transcript.thinking"), "muted")]
  if (entry.text.trim().length === 0) {
    // The live marker alone — no text has arrived yet.
    return [prefix]
  }
  return prefixedBlock(
    [...prefix, seg(" ", "muted")],
    entry.text.split("\n"),
    "accent",
    {},
    context.width
  )
}

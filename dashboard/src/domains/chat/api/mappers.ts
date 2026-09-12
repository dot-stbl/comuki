import type {
  SeedChatMessage,
  SeedChatProposal,
  SeedChatSession,
  SeedMessagePart,
  SeedSlashCommand,
  SeedToolCall,
} from "@/shared/api/mock/chat.seed"

import type {
  ChatMessage,
  ChatSession,
  CommandScope,
  MessagePart,
  MessageKind,
  PlanEdge,
  PlanNode,
  Proposal,
  SlashCommand,
  ToolCall,
  ToolStatus,
} from "@/domains/chat/model/types"

import type { ChatMessagesPageView } from "@/shared/api/_generated/types/ChatMessagesPageView"
import type { ChatMessageView } from "@/shared/api/_generated/types/ChatMessageView"
import type { ChatSessionView } from "@/shared/api/_generated/types/ChatSessionView"
import type { ChatSlashCommand } from "@/shared/api/_generated/types/ChatSlashCommand"
import type { MessagePart as WireMessagePart } from "@/shared/api/_generated/types/MessagePart"
import type { PlanEdge as WirePlanEdge } from "@/shared/api/_generated/types/PlanEdge"
import type { PlanNode as WirePlanNode } from "@/shared/api/_generated/types/PlanNode"

/**
 * The seam between the mock's shapes and the domain's.
 *
 * They agree today, and this looks like copying — but the domain's rule is
 * that nothing in `ui/` ever holds a transport type, and the seed *is* the
 * transport until the Orchestration API arrives. When it does, this file is
 * the only one that changes: `SeedChatSession` becomes a generated DTO and
 * every component above keeps its types.
 */

function toToolCall(seed: SeedToolCall): ToolCall {
  return {
    name: seed.name,
    args: seed.args,
    status: seed.status,
    result: seed.result,
  }
}

function toProposal(seed: SeedChatProposal): Proposal {
  return {
    id: seed.id,
    act: seed.act,
    summary: seed.summary,
    projectId: seed.projectId,
    subject: seed.subject,
    steps: seed.steps?.map((step) => ({ ...step })),
    decision: seed.decision,
  }
}

/**
 * One seeded part → one domain part.
 *
 * The two unions are written to be the same union in two places — the seed is
 * the transport until the Orchestration API arrives, and this is the seam that
 * says so out loud. The copy is structural rather than a cast, because a cast
 * would keep compiling on the day one side grows a field the other does not
 * have, which is exactly the day it needs to stop.
 */
function toMessagePart(seed: SeedMessagePart): MessagePart {
  switch (seed.kind) {
    case "text":
      return { kind: "text", markdown: seed.markdown }
    case "code":
      return {
        kind: "code",
        language: seed.language,
        source: seed.source,
        path: seed.path,
        startLine: seed.startLine,
      }
    case "diagram":
      return { kind: "diagram", dialect: seed.dialect, source: seed.source }
    case "thinking":
      return { kind: "thinking", text: seed.text, tokens: seed.tokens }
    case "tool":
      return {
        kind: "tool",
        name: seed.name,
        inputJson: seed.inputJson,
        status: seed.status,
        outputJson: seed.outputJson,
        durationMs: seed.durationMs,
      }
    case "handoff":
      return { kind: "handoff", query: seed.query }
    case "plan":
      return {
        kind: "plan",
        nodes: seed.nodes.map((node) => ({ ...node })),
        edges: seed.edges.map((edge) => ({ ...edge })),
      }
  }
}

export function toChatMessage(seed: SeedChatMessage): ChatMessage {
  return {
    id: seed.id,
    kind: seed.kind,
    parts: seed.parts?.map(toMessagePart),
    text: seed.text,
    streaming: seed.streaming,
    tool: seed.tool ? toToolCall(seed.tool) : undefined,
    proposal: seed.proposal ? toProposal(seed.proposal) : undefined,
    handoff: seed.handoff,
    at: seed.at,
  }
}

export function toChatSession(seed: SeedChatSession): ChatSession {
  return {
    id: seed.id,
    title: seed.title,
    age: seed.age,
    messages: seed.messages.map(toChatMessage),
  }
}

/**
 * A command the client declared in its own git, as the menu sees it.
 *
 * Its scope is `implied` and never `required`, and that is a fact about where
 * it came from rather than a choice: a custom command exists *because* one
 * project's repository declared it, so the project is already named by the
 * command itself. Offering a chip beside it would be offering to run somebody
 * else's recipe somewhere it was never written for.
 */
export function toSlashCommand(seed: SeedSlashCommand): SlashCommand {
  return {
    name: seed.name,
    description: seed.description,
    origin: "client",
    scope: "implied",
    permission: "inbox.take",
    projectId: seed.projectId,
  }
}

export function toCustomCommands(seed: SeedSlashCommand[]): SlashCommand[] {
  return seed.map(toSlashCommand)
}

/**
 * Wire → domain mappers for the kubb-generated clients.
 *
 * Below: real-mode shapes (`ChatSessionView`, `ChatSlashCommand`) translated
 * into the same domain types the seed mappers above produce. The screen
 * reads one shape (`ChatSession`, `SlashCommand`); the seam is here, not in
 * the components.
 *
 * See `mappers.ts`'s top-of-file comment for the larger contract.
 */

/**
 * The seam between the host's chat wire shapes and the domain's richer
 * conversation/command types.
 *
 * The wire is intentionally minimal — `ChatSessionView` carries `id`,
 * `projectId`, `title`, `status`, `createdAt`, `updatedAt`, and the messages
 * live behind a separate paginated endpoint. The domain's `ChatSession`
 * carries `messages: ChatMessage[]` because the console renders the open
 * conversation inline; mock mode keeps messages on the same record because
 * it has no backend. Real mode routes through this mapper, which leaves
 * `messages: []` until the open conversation's own query lands them
 * (`useGetApiV1ChatSessionsSessionidMessages`) — the split is the same one
 * the kubb-generated clients make.
 *
 * The chat console's command menu takes `SlashCommand` shaped per the
 * dashboard's domain. The host's `ChatSlashCommand` is closer to the wire
 * (key, name, description, body, source); we drop `body` (the prompt body
 * never reached the dashboard's domain — it lived on the seed). `origin` is
 * the dashboard's own taxonomy ("built-in" vs "client"); the host's `source`
 * is the same vocabulary at a different level.
 *
 * The two `name` fields are false friends and cost the menu everything it
 * had: the domain's `name` is "the way it is typed and the way it is shown"
 * (`/help`), and the wire's is the human label (`Help`, `Initialize
 * workspace`). Copying one to the other produced menu rows that
 * `commandMenuQuery` could never match — every query starts with a slash —
 * so no command from the host was reachable by typing. The typeable name is
 * the wire's **`key`**, and that is what this mapper builds it from.
 */

/**
 * The wire's `source` vocabulary, spelled the way the host spells it.
 *
 * `ChatSlashSources` (`Comuki.Modules.Chat.Application.Slash`) is the
 * authority and it names two: a graph-native built-in is **`builtin`** and a
 * command that came out of the control plane's `chat-commands/` pack is
 * **`control-plane`**. This table used to read `built_in` / `client`, which
 * the host has never sent — so every command came back with
 * `origin: undefined` and, because the same two literals decided the scope
 * and the project, with the source label sitting in `projectId`.
 *
 * kubb types `source` as a bare `string`, so nothing in the type system keeps
 * this honest. The constants are here so the next drift is visible in one
 * place rather than spread over three ternaries.
 */
const WIRE_COMMAND_SOURCE_BUILTIN = "builtin"
const WIRE_COMMAND_SOURCE_CONTROL_PLANE = "control-plane"

const WIRE_TO_DOMAIN_COMMAND_ORIGIN: Record<
  string,
  SlashCommand["origin"] | undefined
> = {
  [WIRE_COMMAND_SOURCE_BUILTIN]: "built-in",
  [WIRE_COMMAND_SOURCE_CONTROL_PLANE]: "client",
}

/**
 * What a source this bundle has never heard of becomes.
 *
 * `client`, and never `undefined`: `origin` is required on `SlashCommand` and
 * a menu row that carries none is the defect this table just had. `client`
 * rather than `built-in` because the platform's built-in set ships *inside*
 * this bundle (`model/commands.ts`), so a label this build does not
 * recognise did not come from it — claiming `built-in` would be the
 * dashboard vouching for provenance it cannot check, and the menu marks a
 * client command precisely so an operator can tell the two apart.
 */
const UNKNOWN_COMMAND_ORIGIN: SlashCommand["origin"] = "client"

/**
 * The wire's `key` → the name the operator types.
 *
 * The host's keys are bare (`help`, `restart`) and the domain's names carry
 * the slash, so the slash is added here. Normalised first, because the key
 * reaches the host from a control-plane document somebody hand-wrote: it is
 * trimmed and lower-cased (the menu matches a lower-cased query, so a
 * `Restart` in a pack file would be a command nobody could type), and a
 * leading slash the author already wrote is stripped rather than doubled —
 * `//restart` would match no query and `commandOf` would never resolve it.
 */
function slashCommandName(key: string): string {
  return `/${key.trim().toLowerCase().replace(/^\/+/, "")}`
}

export function chatSlashCommandToDomainCommand(
  command: ChatSlashCommand
): SlashCommand {
  const builtIn = command.source === WIRE_COMMAND_SOURCE_BUILTIN
  const described = command.description.trim()
  return {
    name: slashCommandName(command.key),
    // The wire's own `name` is a human label, and the menu already shows the
    // typed name beside the description — so the label is only worth
    // carrying when the author wrote no description at all. Never both: a
    // row reading "Restart — Restart the run" says one thing twice.
    description: described.length > 0 ? command.description : command.name,
    origin:
      WIRE_TO_DOMAIN_COMMAND_ORIGIN[command.source] ?? UNKNOWN_COMMAND_ORIGIN,
    // The dashboard's seed treats every non-built-in command as `implied`: a
    // declared command exists *because* something outside the graph declared
    // it, so what it acts on is already named by the command itself. The
    // wire's `source` is the same vocabulary; the scope is therefore
    // `implied` for those and `none` for built-ins.
    scope: builtIn ? ("none" as CommandScope) : ("implied" as CommandScope),
    // The host does not yet send the act's permission; the chat console's
    // permission gate keys off `permission` to filter the menu, so without
    // it every command is "no permission required". The screen renders an
    // unsorted menu rather than refusing commands when this is missing —
    // a follow-up wire shape can carry it.
    permission: undefined,
    // Neither source the host sends names a project: `builtin` is the graph's
    // own command and `control-plane` is a document in the swarm's command
    // pack. This used to hold the source label itself, which
    // `availableCommands` then read as a project id — and filtered every wire
    // command straight back out of the menu. Nothing on `ChatSlashCommand`
    // names a project today, so the honest answer is none.
    projectId: undefined,
  }
}

export function chatSlashCommandsToDomainCommands(
  commands: ChatSlashCommand[]
): SlashCommand[] {
  return commands.map(chatSlashCommandToDomainCommand)
}

export function chatSessionViewToDomainSession(
  view: ChatSessionView
): ChatSession {
  return {
    id: view.id,
    title: view.title,
    age: formatAge(view.updatedAt),
    // Real-mode conversations load their messages on demand: the wire's
    // session row carries none, and the open conversation's own query
    // (`useChatMessagesQuery` in `queries.ts`, over
    // `GET /api/v1/chat/sessions/{id}/messages`) lands them. The session list
    // reads `messages.length` only to render the empty-state for an unread
    // conversation, so `[]` is the honest shape here.
    messages: [],
  }
}

function formatAge(updatedAt: string): string {
  const updated = new Date(updatedAt).getTime()
  if (Number.isNaN(updated)) {
    return ""
  }
  const delta = Date.now() - updated
  if (delta < 60_000) {
    return "just now"
  }
  const minutes = Math.floor(delta / 60_000)
  if (minutes < 60) {
    return `${minutes}m ago`
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours}h ago`
  }
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

export function chatSessionViewsToDomainSessions(
  views: ChatSessionView[]
): ChatSession[] {
  return views.map(chatSessionViewToDomainSession)
}

/**
 * The transcript's roles, as the host spells them.
 *
 * `ChatMessageView.Role` is `ChatMessageRole` lower-cased — a closed set of
 * four (`user | assistant | system | tool`) that kubb types as a bare
 * `string`, so nothing in the type system keeps this table honest.
 *
 * Two of the four are obvious and two are decisions:
 *
 * - **`system` → `reply`.** The domain has no system kind and this change is
 *   not the one that adds one. A system row is the memory digest the graph
 *   fed the brain, journaled for audit (see `ChatMessage`'s docblock on the
 *   host: "the table is the session history AND the audit journal"). It is
 *   prose, so `reply` is the only kind that renders it at all — `error`
 *   would announce a failure with `role="alert"` when nothing failed, and
 *   `tool` would claim a call the row never made. The cost is that a digest
 *   reads as something the console said, which is close to true: the console
 *   is what said it, to the brain.
 * - **anything else → `reply`**, for the same reason. An unrecognised role
 *   from a host this bundle is older than still carries text an operator is
 *   entitled to read, and dropping the row would leave a thread with a hole
 *   in it that nothing on screen explains.
 */
const WIRE_TO_DOMAIN_MESSAGE_KIND: Record<string, MessageKind | undefined> = {
  user: "person",
  assistant: "reply",
  system: "reply",
  tool: "tool",
}

const UNKNOWN_MESSAGE_KIND: MessageKind = "reply"

/**
 * `HH:MM`, local.
 *
 * `ChatMessage.at` is a pre-formatted local clock string rather than a
 * timestamp — a contract the larger console change is on the hook for, not
 * this one. Until then the two stamps have to agree, so this is `clock()`
 * from `shared/api/mock/chat.store.ts` reading a wire `date-time` instead of
 * `Date.now()`; a mock thread and a real one read identically.
 */
function clockOf(createdAt: string): string {
  const at = new Date(createdAt)
  if (Number.isNaN(at.getTime())) {
    return ""
  }
  const hh = `${at.getHours()}`.padStart(2, "0")
  const mm = `${at.getMinutes()}`.padStart(2, "0")
  return `${hh}:${mm}`
}

/* ==========================================================================
 * THE WIRE → DOMAIN PART SEAM.
 *
 * The host ships `ChatMessageView.parts` (`Comuki.Shared.Contracts.Chat`
 * `MessagePart`, a `kind`-discriminated union over the same frozen seven the
 * domain declares), and kubb emits it as a proper TypeScript discriminated
 * union — so the mapping below narrows on `kind` rather than casting, and a
 * kind added to either side without the other stops compiling here.
 *
 * Three things the wire does not say the way the domain does:
 *
 *  - **Numbers arrive as `number | string`.** kubb widens every `int32` /
 *    `int64` that way, because JSON may carry a 64-bit value quoted rather
 *    than lose precision. The domain says `number`, so each one goes through
 *    `wireInteger`, which drops a value it cannot read rather than handing
 *    the renderer a `NaN`.
 *  - **`PlanNode` is spelled differently on each side.** The wire's node is
 *    `{ id, title, profileKey, brief }` — the canonical `Plan` shape the
 *    approve card and the run graph share — and the domain's is
 *    `{ id, label, profile }`. `brief` has nowhere to land: the console's
 *    plan renderer is a stub that lists the steps and says the dependencies
 *    in words, so carrying the worker brief into a type nothing reads it
 *    from would be inventing a field rather than preserving one.
 *  - **`status` on a tool part is a bare `string`.** `ToolPartStatuses`
 *    names three and kubb types none of them, so the table below is what
 *    keeps the domain union honest.
 *
 * `meta` (`ChatMessageMeta` — model, tokens, cost, latency, stop reason) is
 * on the wire now too and is deliberately *not* mapped: `ChatMessage` has no
 * field for it, and giving it one is a change to what the thread draws
 * rather than to this seam.
 * ========================================================================== */

/**
 * A wire integer, as a number the renderer can use.
 *
 * kubb types every `int32` / `int64` as `number | string | null`, because a
 * 64-bit value may legitimately arrive quoted. Anything that does not read
 * as a finite number becomes `undefined` — the domain's own spelling for
 * "the turn did not report this" — rather than a `NaN` that renders as the
 * word `NaN` beside a line number.
 */
function wireInteger(
  value: number | string | null | undefined
): number | undefined {
  if (value === null || value === undefined) {
    return undefined
  }
  const parsed = typeof value === "number" ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

/** The three statuses `ToolPartStatuses` names; see the seam header. */
const KNOWN_TOOL_STATUSES = new Set<ToolStatus>([
  "running",
  "success",
  "failed",
])

/**
 * What a tool status this bundle does not know becomes.
 *
 * `success`, and the choice is between three lies. `running` would spin a
 * pending indicator forever on a call that is already journaled and
 * therefore terminal; `failed` would raise an alarm about a call that may
 * well have worked. `success` is also what the flat tool path above already
 * assumes, for the same reason — the host appends a tool row *after* the
 * call returned — so the two paths agree rather than disagreeing per row.
 */
const UNKNOWN_TOOL_STATUS: ToolStatus = "success"

function wireToolStatus(value: string): ToolStatus {
  if (KNOWN_TOOL_STATUSES.has(value as ToolStatus)) {
    return value as ToolStatus
  }
  return UNKNOWN_TOOL_STATUS
}

function wirePlanNode(node: WirePlanNode): PlanNode {
  return {
    id: node.id,
    label: node.title,
    profile: node.profileKey,
  }
}

function wirePlanEdge(edge: WirePlanEdge): PlanEdge {
  return { from: edge.from, to: edge.to }
}

/**
 * One wire part → one domain part, or nothing at all.
 *
 * `undefined` means "this bundle does not know this kind". The union is
 * frozen at seven and two more (`question`, `decision`) are already named as
 * P2, so a host newer than this bundle is a certainty rather than a
 * hypothetical — and the precedent for that is `normalizeTicketStatus` in
 * `domains/inbox/api/mappers.ts`: a partial backend rollout degrades the
 * row, never the screen. The host takes the same line one layer down —
 * `MessagePartsJson` catches the unknown-discriminator exception and reads
 * the whole row as "no parts", because "the flat content projection is
 * always there to fall back on".
 *
 * So an unknown part is dropped: not thrown on, and not drawn as a
 * placeholder an operator can do nothing with. `wireMessageParts` below
 * carries the consequence — when dropping leaves nothing, the row falls back
 * to the flat `content` the host guarantees is populated, which is the same
 * words with the structure removed rather than a hole in the thread.
 */
function wireMessagePart(part: WireMessagePart): MessagePart | undefined {
  switch (part.kind) {
    case "text":
      return { kind: "text", markdown: part.markdown }
    case "code":
      return {
        kind: "code",
        language: part.language,
        source: part.source,
        path: part.path ?? undefined,
        startLine: wireInteger(part.startLine),
      }
    case "diagram":
      return { kind: "diagram", dialect: part.dialect, source: part.source }
    case "thinking":
      return {
        kind: "thinking",
        text: part.text,
        tokens: wireInteger(part.tokens),
      }
    case "tool":
      return {
        kind: "tool",
        name: part.name,
        inputJson: part.inputJson,
        status: wireToolStatus(part.status),
        outputJson: part.outputJson ?? undefined,
        durationMs: wireInteger(part.durationMs),
      }
    case "handoff":
      return { kind: "handoff", query: part.query }
    case "plan":
      return {
        kind: "plan",
        nodes: part.nodes.map(wirePlanNode),
        edges: part.edges.map(wirePlanEdge),
      }
    default:
      return unknownMessagePart(part)
  }
}

/**
 * A `kind` the generated union does not carry.
 *
 * The parameter is `never`, which is the compile-time half of the contract:
 * the switch above is exhaustive today, so nothing reaches here — and the day
 * the host adds `question`, `part` stops narrowing to `never` and this call
 * fails to compile until somebody writes the case. At runtime it answers
 * `undefined` rather than throwing, because a host newer than this bundle
 * must degrade the part and not the thread.
 */
function unknownMessagePart(part: never): undefined {
  void part
  return undefined
}

/**
 * The parts of one transcript row, or `undefined` when it has none.
 *
 * Never `[]`. An empty list is a claim — "this turn had no body" — that
 * `model/parts.ts` honours by drawing a stated blank, and it is the wrong
 * claim about a row that carries prose in `content`. Three cases collapse to
 * `undefined` for that one reason:
 *
 *  - `parts: null` — a row written before parts existed, or one whose
 *    payload no longer parses. `ChatMessageView`'s own docblock on the host
 *    says exactly that, and the flat derivation renders it the way it
 *    rendered every row before this function was written.
 *  - `parts: []` — the host does not send it (`MessagePartsJson.TryParse`
 *    reads an empty payload as no parts), but if a later one does, the flat
 *    projection is still the honest reading.
 *  - every part dropped as unknown — see `wireMessagePart`.
 */
function wireMessageParts(view: ChatMessageView): MessagePart[] | undefined {
  if (!view.parts) {
    return undefined
  }

  const parts: MessagePart[] = []
  for (const part of view.parts) {
    const mapped = wireMessagePart(part)
    if (mapped) {
      parts.push(mapped)
    }
  }

  return parts.length > 0 ? parts : undefined
}

/**
 * One transcript row → one domain message.
 *
 * The wire is thinner than the domain in both directions that matter. It has
 * no proposal and no hand-off, because the host does not yet send either as
 * a message — the turn result carries `awaitingApproval` instead — and it has
 * no `streaming`, because a page of history is by definition already
 * arrived. Those fields stay absent rather than being invented here.
 *
 * A tool row is the one that needs assembling: the wire journals the
 * *observation* (`toolName` plus the result as `content`) and not the call,
 * so `args` is empty rather than fabricated and the status is `success` —
 * the host appends a tool row after the call returned, and a turn that
 * failed is journaled as its own message.
 */
export function chatMessageViewToDomainMessage(
  view: ChatMessageView
): ChatMessage {
  const kind = WIRE_TO_DOMAIN_MESSAGE_KIND[view.role] ?? UNKNOWN_MESSAGE_KIND
  const tool: ToolCall | undefined =
    kind === "tool"
      ? {
          name: view.toolName ?? "",
          args: "",
          status: "success",
          result: view.content,
        }
      : undefined

  return {
    id: view.id,
    kind,
    // The rich body when the row has one. `undefined` when it does not —
    // and then the flat fields below are what the thread renders, through
    // the same derivation the seed's older messages go through.
    parts: wireMessageParts(view),
    // A tool row's prose *is* its result, and the card reads it off the tool
    // record. A second copy on `text` would render the same line twice the
    // day somebody widens the prose branch in `ui/chat-message.tsx`.
    text: tool ? undefined : view.content,
    tool,
    at: clockOf(view.createdAt),
  }
}

/**
 * One page of the transcript → the thread.
 *
 * `items` arrive oldest-first and the log renders oldest-first, so the order
 * is carried through untouched. Paging is not wired: the console asks for the
 * first page and the query names the size (`queries.ts`), which is the same
 * "one window of history" the graph's own `HistoryWindow` works with.
 */
export function chatMessagesPageToDomainMessages(
  page: ChatMessagesPageView
): ChatMessage[] {
  return page.items.map(chatMessageViewToDomainMessage)
}

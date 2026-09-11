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
  Proposal,
  SlashCommand,
  ToolCall,
} from "@/domains/chat/model/types"

import type { ChatMessagesPageView } from "@/shared/api/_generated/types/ChatMessagesPageView"
import type { ChatMessageView } from "@/shared/api/_generated/types/ChatMessageView"
import type { ChatSessionView } from "@/shared/api/_generated/types/ChatSessionView"
import type { ChatSlashCommand } from "@/shared/api/_generated/types/ChatSlashCommand"

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
/* ==========================================================================
 * THE WIRE → DOMAIN PART SEAM. One function, and it is deliberately empty.
 *
 * TODO(chat-parts-wire): when the host ships `ChatMessageView.parts`, this is
 * the only function that has to be written — map each wire part onto the
 * matching `MessagePart` the way `toMessagePart` maps the seed's, and delete
 * the `undefined` below. The integration step is:
 *
 *   1. the sibling change on `platform/` adds the parts array to
 *      `ChatMessageView` (the frozen list: text, code, diagram, thinking,
 *      tool, handoff, plan — `question` and `decision` are P2);
 *   2. somebody runs `bun run generate-api`, which regenerates
 *      `shared/api/_generated/types/ChatMessageView.ts`;
 *   3. this function stops returning `undefined` and starts reading
 *      `view.parts`, and `chatMessageViewToDomainMessage` below passes the
 *      result straight through.
 *
 * Nothing above this line and nothing in `ui/` changes, because the flat path
 * stays: a message with no parts is derived from its own fields in
 * `model/parts.ts`, which is what every wire row does today and what an older
 * host will keep doing after the field lands.
 *
 * The wire types **do not exist yet** and this bundle must not invent them:
 * `_generated/` is machine-written from the host's OpenAPI document, and a
 * hand-edited shape there is a lie that survives exactly until the next
 * generate.
 * ========================================================================== */
function wireMessageParts(): MessagePart[] | undefined {
  return undefined
}

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
    // `undefined` until the host sends parts — see the seam above. The flat
    // fields below are what the thread renders in the meantime, through the
    // same derivation the seed's older messages go through.
    parts: wireMessageParts(),
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

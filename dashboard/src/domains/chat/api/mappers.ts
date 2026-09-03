import type {
  SeedChatMessage,
  SeedChatProposal,
  SeedChatSession,
  SeedSlashCommand,
  SeedToolCall,
} from "@/shared/api/mock/chat.seed"

import type {
  ChatMessage,
  ChatSession,
  CommandScope,
  Proposal,
  SlashCommand,
  ToolCall,
} from "@/domains/chat/model/types"

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

export function toChatMessage(seed: SeedChatMessage): ChatMessage {
  return {
    id: seed.id,
    kind: seed.kind,
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
 * never reached the dashboard's domain — it lived on the seed) and map
 * `source` onto `projectId` when it names one. `origin` is the dashboard's
 * own taxonomy ("built-in" vs "client"); the host's `source` is the
 * same vocabulary at a different level.
 */

const WIRE_TO_DOMAIN_COMMAND_ORIGIN: Record<
  ChatSlashCommand["source"],
  SlashCommand["origin"]
> = {
  built_in: "built-in",
  client: "client",
}

export function chatSlashCommandToDomainCommand(
  command: ChatSlashCommand
): SlashCommand {
  return {
    name: command.name,
    description: command.description,
    origin: WIRE_TO_DOMAIN_COMMAND_ORIGIN[command.source],
    // The dashboard's seed treats every client command as `implied`: a custom
    // command exists *because* one project's repository declared it, so the
    // project is already named by the command itself. The wire's `source` is
    // the same vocabulary; the scope is therefore `implied` for client
    // commands and `none` for built-ins.
    scope:
      command.source === "built_in"
        ? ("none" as CommandScope)
        : ("implied" as CommandScope),
    // The host does not yet send the act's permission; the chat console's
    // permission gate keys off `permission` to filter the menu, so without
    // it every command is "no permission required". The screen renders an
    // unsorted menu rather than refusing commands when this is missing —
    // a follow-up wire shape can carry it.
    permission: undefined,
    // The host's `source` is the dashboard's `projectId` for client commands;
    // built-in commands arrive with `source: "built_in"`, and the dashboard's
    // domain reads `projectId` as a value, never an enum, so the literal
    // string is the honest default.
    projectId:
      command.source === "built_in" ? undefined : command.source,
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
    // Real-mode conversations load their messages on demand (see
    // `useChatMessagesQuery`). The session list reads `messages.length` only
    // to render the empty-state for an unread conversation — the open
    // conversation replaces the empty list with the fetched messages — so
    // `[]` is the honest shape.
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

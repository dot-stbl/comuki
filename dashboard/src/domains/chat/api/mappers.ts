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
  Proposal,
  SlashCommand,
  ToolCall,
} from "@/domains/chat/model/types"

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

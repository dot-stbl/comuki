import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  chatSessionViewsToDomainSessions,
  chatSlashCommandsToDomainCommands,
  toChatSession,
  toCustomCommands,
} from "@/domains/chat/api/mappers"
import type {
  ChatSession,
  ProposalDecision,
  SlashCommand,
} from "@/domains/chat/model/types"
import { getApiV1ChatSessions } from "@/shared/api/_generated/clients/getApiV1ChatSessions"
import { getApiV1ChatSlash } from "@/shared/api/_generated/clients/getApiV1ChatSlash"
import { postApiV1ChatSessions } from "@/shared/api/_generated/clients/postApiV1ChatSessions"
import { postApiV1ChatSessionsSessionidApprove } from "@/shared/api/_generated/clients/postApiV1ChatSessionsSessionidApprove"
import { postApiV1ChatSessionsSessionidMessages } from "@/shared/api/_generated/clients/postApiV1ChatSessionsSessionidMessages"
import {
  decideChatProposal,
  listChatSessions,
  listCustomCommands,
  sendChatMessage,
  startChatSession,
} from "@/shared/api/mock/chat.store"
import { runsQueryKey } from "@/domains/runs/api/queries"
import { env } from "@/shared/config/env"

export const chatSessionsQueryKey = ["chat", "sessions"] as const
export const chatCommandsQueryKey = ["chat", "commands"] as const

async function listSessions(): Promise<ChatSession[]> {
  if (env.useMock) {
    return listChatSessions().map(toChatSession)
  }
  const views = await getApiV1ChatSessions()
  return chatSessionViewsToDomainSessions(views)
}

/**
 * The client's own commands.
 *
 * A query rather than a constant, because that is what they are: they live in
 * the client's git and reach the dashboard over the wire. Modelling them as an
 * import would have made the composer's menu a place where the platform's
 * commands and the client's are indistinguishable in the code, which is
 * exactly the distinction the menu has to draw on screen.
 *
 * Real mode calls `GET /api/v1/chat/slash` (the platform's built-in commands)
 * and relies on the host to merge client-declared commands on the same wire
 * response. The dashboard's `SlashCommand` shape carries the same vocabulary;
 * the mapper in `mappers.ts` translates the host's `source` to the dashboard's
 * `origin`.
 */
async function listCommands(): Promise<SlashCommand[]> {
  if (env.useMock) {
    return toCustomCommands(listCustomCommands())
  }
  const commands = await getApiV1ChatSlash()
  return chatSlashCommandsToDomainCommands(commands)
}

export function useChatSessionsQuery() {
  return useQuery({ queryKey: chatSessionsQueryKey, queryFn: listSessions })
}

export function useChatCommandsQuery() {
  return useQuery({ queryKey: chatCommandsQueryKey, queryFn: listCommands })
}

export interface SendMessageInput {
  sessionId: string
  text: string
  /** The composer's scope, when the command needed one. */
  projectId?: string
}

export function useSendMessageMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ sessionId, text, projectId }: SendMessageInput) => {
      if (env.useMock) {
        const result = sendChatMessage(sessionId, text, projectId)
        if (!result) {
          throw new Error(`chat session ${sessionId} not found`)
        }
        return listChatSessions().map(toChatSession)
      }
      await postApiV1ChatSessionsSessionidMessages(sessionId, {
        message: text,
      })
      void queryClient.invalidateQueries({ queryKey: chatSessionsQueryKey })
      return listSessions()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(chatSessionsQueryKey, next)
    },
  })
}

export interface DecideProposalInput {
  sessionId: string
  proposalId: string
  decision: ProposalDecision
}

/**
 * A human decided, and the decision goes where every other decision goes.
 *
 * The runs cache is invalidated on success because a confirmed proposal writes
 * to the *run* store — a run stopped from the console is a run stopped, and
 * the duty list has to say so without a reload. That invalidation is the whole
 * of "chat is another way in, never a second system of record", spelled as
 * code.
 *
 * Real mode calls `POST /api/v1/chat/sessions/{id}/approve`; the wire's
 * `ChatTurnResultView` carries the assistant's next message and an
 * `awaitingApproval` flag (used by the proposal card to re-render the
 * confirm button). For now we only invalidate the run cache; the proposal
 * card refetches its own message stream when the open conversation's
 * message query refires. The wire does not yet expose the proposal id the
 * dashboard sends — only `approved` and an optional `reason` — so the
 * confirmation passes the boolean and lets the host's turn-result decide
 * which proposal was current.
 */
export function useProposalDecisionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      sessionId,
      proposalId,
      decision,
    }: DecideProposalInput) => {
      if (env.useMock) {
        decideChatProposal(sessionId, proposalId, decision)
        return listChatSessions().map(toChatSession)
      }
      await postApiV1ChatSessionsSessionidApprove(sessionId, {
        approved: decision === "confirmed",
      })
      void queryClient.invalidateQueries({ queryKey: chatSessionsQueryKey })
      void queryClient.invalidateQueries({ queryKey: runsQueryKey })
      return listSessions()
    },
    onSuccess: (next) => {
      queryClient.setQueryData(chatSessionsQueryKey, next)
    },
  })
}

export function useStartSessionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      if (env.useMock) {
        const session = startChatSession()
        queryClient.setQueryData(
          chatSessionsQueryKey,
          listChatSessions().map(toChatSession)
        )
        return toChatSession(session)
      }
      const created = await postApiV1ChatSessions({})
      void queryClient.invalidateQueries({ queryKey: chatSessionsQueryKey })
      const mapped = chatSessionViewsToDomainSessions([created])
      const next = mapped[0]
      if (!next) {
        throw new Error("chat session not created")
      }
      return next
    },
  })
}

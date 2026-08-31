import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  toChatSession,
  toCustomCommands,
} from "@/domains/chat/api/mappers"
import type {
  ChatSession,
  ProposalDecision,
  SlashCommand,
} from "@/domains/chat/model/types"
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

function guard(): void {
  if (!env.useMock) {
    throw new Error("chat API not implemented — set VITE_USE_MOCK=true")
  }
}

async function listSessions(): Promise<ChatSession[]> {
  guard()
  return listChatSessions().map(toChatSession)
}

/**
 * The client's own commands.
 *
 * A query rather than a constant, because that is what they are: they live in
 * the client's git and reach the dashboard over the wire. Modelling them as an
 * import would have made the composer's menu a place where the platform's
 * commands and the client's are indistinguishable in the code, which is
 * exactly the distinction the menu has to draw on screen.
 */
async function listCommands(): Promise<SlashCommand[]> {
  guard()
  return toCustomCommands(listCustomCommands())
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
      guard()
      const result = sendChatMessage(sessionId, text, projectId)
      if (!result) {
        throw new Error(`chat session ${sessionId} not found`)
      }
      return listChatSessions().map(toChatSession)
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
 */
export function useProposalDecisionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      sessionId,
      proposalId,
      decision,
    }: DecideProposalInput) => {
      guard()
      decideChatProposal(sessionId, proposalId, decision)
      return listChatSessions().map(toChatSession)
    },
    onSuccess: (next) => {
      queryClient.setQueryData(chatSessionsQueryKey, next)
      void queryClient.invalidateQueries({ queryKey: runsQueryKey })
    },
  })
}

export function useStartSessionMutation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      guard()
      const session = startChatSession()
      queryClient.setQueryData(
        chatSessionsQueryKey,
        listChatSessions().map(toChatSession)
      )
      return toChatSession(session)
    },
  })
}

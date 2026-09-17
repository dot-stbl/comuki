/**
 * `comuki chat` — the interactive REPL.
 *
 * Flow: resolve config → identity (best effort) → project scope (optional)
 * → create session → best-effort SignalR join → prompt loop. Message
 * turns are synchronous REST calls (`POST …/messages` returns the whole
 * turn result); SignalR chunks only animate the wait, so a dead socket
 * degrades to a quiet spinner, never a broken chat.
 */
import { Text, useApp } from "ink"
import React, { useCallback, useEffect, useRef, useState } from "react"
import type { HubConnection } from "@microsoft/signalr"
import { ComukiApiError, ComukiClient, type ChatMessageView } from "../lib/client"
import { whoAmI } from "../lib/auth"
import { renderPendingPlan } from "../lib/format"
import type { ResolvedConfig } from "../lib/config"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  startChatHubConnection,
} from "../lib/signalr"
import { colors, symbols } from "../theme"
import { ChatMessage } from "../components/ChatMessage"
import { PromptInput } from "../components/PromptInput"
import { StatusLine } from "../components/StatusLine"
import { TypingIndicator } from "../components/TypingIndicator"

type Phase = "connecting" | "ready" | "thinking"

interface MessageBlock {
  readonly kind: "message"
  readonly key: string
  readonly message: ChatMessageView
}

interface LinesBlock {
  readonly kind: "lines"
  readonly key: string
  readonly lines: readonly string[]
}

type ChatBlock = MessageBlock | LinesBlock

const HELP_LINES = [
  `${colors.accent}commands${colors.reset}`,
  `  exit, quit, q     leave the session`,
  `  clear             wipe the screen`,
  `  help              this list`,
  `  approve           release a pending plan`,
  `  reject [reason]   decline a pending plan`,
]

export interface ChatCommandProps {
  readonly config: ResolvedConfig
  /** Project id, slug or name; falls back to config.defaultProject. */
  readonly project?: string
}

export function ChatApp({ config, project }: ChatCommandProps) {
  const { exit } = useApp()
  const [phase, setPhase] = useState<Phase>("connecting")
  const [blocks, setBlocks] = useState<ChatBlock[]>([])
  const [history, setHistory] = useState<string[]>([])
  const [liveText, setLiveText] = useState("")
  const [identity, setIdentity] = useState("connecting…")
  const [projectLabel, setProjectLabel] = useState<string | undefined>(project)
  const [connectError, setConnectError] = useState<string | null>(null)

  const clientRef = useRef<ComukiClient | null>(null)
  const sessionRef = useRef<string | null>(null)
  const hubRef = useRef<HubConnection | null>(null)
  const awaitingApprovalRef = useRef(false)

  const pushLines = useCallback((lines: readonly string[]) => {
    setBlocks((current) => [
      ...current,
      { kind: "lines", key: `lines-${current.length}`, lines },
    ])
  }, [])

  const pushMessages = useCallback((messages: readonly ChatMessageView[]) => {
    setBlocks((current) => [
      ...current,
      ...messages.map((message, index) => ({
        kind: "message" as const,
        key: `msg-${message.id}-${current.length}-${index}`,
        message,
      })),
    ])
  }, [])

  // -- connect ---------------------------------------------------------------

  useEffect(() => {
    let disposed = false
    let hub: HubConnection | null = null

    void (async () => {
      const client = new ComukiClient(config)
      clientRef.current = client

      const who = await whoAmI(client)
      if (disposed) {
        return
      }
      setIdentity(who.label)

      let projectId: string | undefined
      let label: string | undefined
      const wanted = project ?? config.defaultProject
      if (wanted) {
        try {
          const projects = await client.projects()
          const match = projects.find(
            (candidate) =>
              candidate.id === wanted ||
              candidate.slug === wanted ||
              candidate.name === wanted
          )
          if (match) {
            projectId = match.id
            label = match.slug
          } else {
            pushLines([
              `${colors.red}${symbols.cross} project not found: ${wanted}${colors.reset}`,
            ])
          }
        } catch {
          label = wanted
        }
      }
      if (disposed) {
        return
      }
      setProjectLabel(label)

      try {
        const session = await client.createSession({
          projectId,
          title: `comuki-cli ${new Date().toISOString().slice(0, 16)}`,
        })
        if (disposed) {
          return
        }
        sessionRef.current = session.id

        // Best-effort live progress; the REST turn is authoritative.
        const connection = await startChatHubConnection({
          hubUrl: client.hubUrl(),
          headers: client.hubHeaders(),
        })
        if (connection && !disposed) {
          hub = connection
          hubRef.current = connection
          bindChatEvents(
            connection,
            (chunk) => {
              setLiveText((current) => (current + chunk.text).slice(-4000))
            },
            () => {
              // ChatTurnComplete — the POST result renders the turn.
            }
          )
          await joinChatGroup(connection, session.id)
        }
        if (disposed) {
          return
        }
        setPhase("ready")
      } catch (error) {
        if (disposed) {
          return
        }
        setConnectError(describeError(error))
        setPhase("ready")
      }
    })()

    return () => {
      disposed = true
      const connection = hub ?? hubRef.current
      if (connection) {
        const sessionId = sessionRef.current
        if (sessionId) {
          void leaveChatGroup(connection, sessionId)
        }
        void connection.stop()
      }
    }
  }, [config, project, pushLines])

  // -- turns -----------------------------------------------------------------

  const describeTurn = useCallback(
    (result: {
      messages: readonly ChatMessageView[]
      awaitingApproval: boolean
      pendingPlan: unknown
    }) => {
      pushMessages(result.messages)
      awaitingApprovalRef.current = result.awaitingApproval
      if (result.awaitingApproval) {
        pushLines([
          "",
          `${colors.yellow}${symbols.bullet} awaiting approval${colors.reset}`,
          ...renderPendingPlan(result.pendingPlan),
          `${colors.dim}  type approve or reject [reason] to decide${colors.reset}`,
        ])
      }
    },
    [pushLines, pushMessages]
  )

  const runTurn = useCallback(
    async (
      kind: "message" | "approve",
      payload: { message?: string; approved?: boolean; reason?: string }
    ) => {
      const client = clientRef.current
      const sessionId = sessionRef.current
      if (!client || !sessionId) {
        return
      }
      setPhase("thinking")
      setLiveText("")
      try {
        const result =
          kind === "message"
            ? await client.postMessage(sessionId, payload.message ?? "")
            : await client.approve(sessionId, payload.approved ?? true, payload.reason)
        describeTurn(result)
      } catch (error) {
        pushLines(["", `${colors.red}${symbols.cross} ${describeError(error)}${colors.reset}`])
      } finally {
        setLiveText("")
        setPhase("ready")
      }
    },
    [describeTurn, pushLines]
  )

  const handleSubmit = useCallback(
    (raw: string) => {
      const value = raw.trim()
      if (value.length === 0 || phase === "thinking" || phase === "connecting") {
        return
      }
      setHistory((current) => [...current, value])

      const lowered = value.toLowerCase()
      if (["exit", "quit", "q"].includes(lowered)) {
        exit()
        return
      }
      if (lowered === "clear") {
        setBlocks([])
        return
      }
      if (lowered === "help") {
        pushLines(HELP_LINES)
        return
      }
      if (lowered === "approve" || lowered.startsWith("reject")) {
        if (!awaitingApprovalRef.current) {
          pushLines([
            `${colors.dim}  nothing to approve — the brain did not interrupt${colors.reset}`,
          ])
          return
        }
        const approved = lowered === "approve"
        const reason = value.slice(6).trim() || undefined
        pushLines([
          approved
            ? `${colors.green}${symbols.checkmark} approving…${colors.reset}`
            : `${colors.yellow}${symbols.bullet} rejecting…${colors.reset}`,
        ])
        void runTurn("approve", { approved, reason })
        return
      }

      pushMessages([
        {
          id: `user-${Date.now()}`,
          role: "user",
          content: value,
          toolName: null,
          parts: null,
          meta: null,
          createdAt: new Date().toISOString(),
        },
      ])
      void runTurn("message", { message: value })
    },
    [exit, phase, pushLines, pushMessages, runTurn]
  )

  useEffect(() => {
    if (connectError) {
      process.exitCode = 1
    }
  }, [connectError])

  if (connectError) {
    return (
      <>
        <StatusLine identity={identity} project={projectLabel} />
        <Text>
          {"  "}
          <Text color="red">
            {symbols.cross} {connectError}
          </Text>
        </Text>
        <Text dimColor>{"  "}check COMUKI_URL / COMUKI_API_KEY, or run comuki login</Text>
      </>
    )
  }

  return (
    <>
      <StatusLine identity={identity} project={projectLabel} />
      {blocks.map((block) =>
        block.kind === "message" ? (
          <ChatMessage key={block.key} message={block.message} />
        ) : (
          <React.Fragment key={block.key}>
            {block.lines.map((line, index) => (
              <Text key={index}>{line}</Text>
            ))}
          </React.Fragment>
        )
      )}
      {phase === "thinking" ? <TypingIndicator liveText={liveText} /> : null}
      {phase === "ready" ? (
        <PromptInput onSubmit={handleSubmit} history={history} />
      ) : null}
      {phase === "connecting" ? (
        <Text dimColor>
          {"     "}
          {symbols.bullet} connecting to {config.url}…
        </Text>
      ) : null}
    </>
  )
}

/** Human shape of any failure: HTTP problems keep their code + detail. */
export function describeError(error: unknown): string {
  if (error instanceof ComukiApiError) {
    return `HTTP ${error.status}${error.code ? ` (${error.code})` : ""}: ${error.detail ?? "request failed"}`
  }
  const message = error instanceof Error ? error.message : String(error)
  if (/unable to connect|fetch failed|econnrefused|connection refused/i.test(message)) {
    return "server unreachable"
  }
  return message
}

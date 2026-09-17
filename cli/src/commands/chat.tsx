/**
 * `comuki` (default) — the multi-session REPL.
 *
 * N parallel brain sessions switched like browser tabs: the tab strip
 * on top, the active transcript in the middle, session badges + hotkey
 * legend at the bottom. Turns are synchronous REST calls per session,
 * fired unawaited — a thinking tab keeps working in the background and
 * only marks itself unread. The laptop stays cold.
 *
 * One Signalr connection carries every session's chunks (`ChatChunk`
 * holds `sessionId`, so routing is a patch over tabs). A pending tab
 * (`local-…` id) becomes a server session lazily on its first message.
 *
 * The transcript scrolls inside the app (irssi/htop model), not in the
 * terminal scrollback: the flattened lines render into a fixed-height
 * viewport (`lib/viewport.ts` + `TranscriptViewport`) whose offset 0
 * follows the bottom; PgUp suspends follow, `↓ new messages` marks
 * fresh output below, End resumes. The prompt and footer are pinned
 * outside the viewport and never scroll away.
 */
import { Box, Text, useApp, useInput } from "ink"
import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type { HubConnection } from "@microsoft/signalr"
import {
  ComukiApiError,
  ComukiClient,
  type ChatMessageView,
} from "../lib/client"
import { whoAmI } from "../lib/auth"
import { flattenTranscript } from "../lib/transcript"
import type { ResolvedConfig } from "../lib/config"
import { useStdoutDimensions } from "../hooks/useStdoutDimensions"
import { useCopyLastAnswer } from "../hooks/useCopyLastAnswer"
import { useHomeEndKeys } from "../hooks/useHomeEndKeys"
import { useSpinnerFrame } from "../hooks/useSpinnerFrame"
import { useTranscriptScroll } from "../hooks/useTranscriptScroll"
import { lastAssistantText } from "../lib/history"
import {
  addSession,
  adoptServerId,
  appendBlocks,
  appendHistory,
  appendLiveText,
  fromPersisted,
  markUnread,
  newPendingSession,
  patchSession,
  readSessionsFile,
  removeSession,
  renameSession,
  retryMessage,
  setBlocks,
  titleForFirstMessage,
  stepActive,
  writeSessionsFile,
  PENDING_PREFIX,
  type ChatBlock,
  type Session,
  type SessionsState,
} from "../lib/sessions"
import { resolveSlashAction, slashHelpLines } from "../lib/slash"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  rejoinChatGroups,
  startChatHubConnection,
  type HubConnectionState,
} from "../lib/signalr"
import { colors, symbols } from "../theme"
import { PromptInput } from "../components/PromptInput"
import { SessionFooter } from "../components/SessionFooter"
import { SessionOverview } from "../components/SessionOverview"
import { StatusLine } from "../components/StatusLine"
import { TabBar } from "../components/TabBar"
import { TranscriptViewport } from "../components/TranscriptViewport"
import { Welcome, type PlatformStats } from "../components/Welcome"

const EMPTY_TAB_HINT = `${colors.dim}  no open sessions — ctrl+n to start one${colors.reset}`

const NOTHING_TO_RETRY = `${colors.dim}  nothing to retry — no message sent yet${colors.reset}`

export interface ChatCommandProps {
  readonly config: ResolvedConfig
  /** Project id, slug or name; falls back to config.defaultProject. */
  readonly project?: string
}

export function ChatApp({ config, project }: ChatCommandProps) {
  const { exit } = useApp()
  const { columns, rows } = useStdoutDimensions()
  const [tabs, setTabs] = useState<SessionsState>({
    sessions: [],
    activeIndex: -1,
  })
  const [identity, setIdentity] = useState("connecting…")
  const [projectLabel, setProjectLabel] = useState<string | undefined>(project)
  const [connectError, setConnectError] = useState<string | null>(null)
  const [noticeLines, setNoticeLines] = useState<readonly string[]>([])
  const [overviewVisible, setOverviewVisible] = useState(false)
  /** The welcome screen never returns once the first message is sent. */
  const [welcomeDismissed, setWelcomeDismissed] = useState(false)
  const [stats, setStats] = useState<PlatformStats | null>(null)
  const [bootstrapped, setBootstrapped] = useState(false)
  /**
   * True once the SSE attempt has resolved (success OR failure). The
   * StatusLine placeholder text only shows while we are "truly
   * disconnected" — once the hub attempt finishes, fallback to REST-only
   * is a stable state and the placeholder goes away even when the hub
   * never came up.
   */
  const [hubAttempted, setHubAttempted] = useState(false)
  /** Hub chip for the status line; driven by the factory's state feed. */
  const [hubState, setHubState] = useState<HubConnectionState>("connecting")
  /** Chat-send EMA from the client — the status line latency badge. */
  const [latencyMs, setLatencyMs] = useState<number | null>(null)

  const clientRef = useRef<ComukiClient | null>(null)
  const hubRef = useRef<HubConnection | null>(null)
  const projectIdRef = useRef<string | undefined>(undefined)
  const activeIdRef = useRef<string | undefined>(undefined)
  const overviewRef = useRef(false)
  /** Reconnect re-join needs the session ids without a closure snapshot. */
  const sessionsRef = useRef<readonly Session[]>([])

  const activeSession =
    tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
  const activeSessionId = activeSession?.id
  const activeHydrated = activeSession?.hydrated

  // ctrl+y copies the last assistant answer; hint is rendered near the
  // prompt (getter is kept fresh by the hook, no stale transcript).
  const { hint: copyHint } = useCopyLastAnswer(() =>
    lastAssistantText(activeSession?.blocks ?? [])
  )

  // -- transcript viewport ------------------------------------------------------

  const thinking = activeSession?.status === "thinking"
  const typingFrame = useSpinnerFrame(thinking)

  const transcriptLines = useMemo(
    () =>
      flattenTranscript(
        activeSession
          ? {
              blocks: activeSession.blocks,
              awaitingApproval: activeSession.awaitingApproval,
              pendingPlan: activeSession.pendingPlan,
              thinking,
              liveText: activeSession.liveText,
            }
          : undefined,
        columns,
        typingFrame,
        noticeLines
      ),
    [activeSession, columns, typingFrame, noticeLines, thinking]
  )

  // Pinned chrome rows: status line + tab strip + footer + the prompt
  // block (the transient ctrl+y hint takes its own row). Everything
  // left belongs to the scrolling viewport.
  const showFooter = tabs.sessions.length > 0 && !overviewVisible
  const viewportHeight = Math.max(
    1,
    rows -
      1 - // StatusLine
      (tabs.sessions.length > 0 ? 1 : 0) - // TabBar
      (showFooter ? 1 : 0) - // SessionFooter
      (1 + (copyHint ? 1 : 0)) // prompt (+ transient copy hint row)
  )

  const scroll = useTranscriptScroll(
    transcriptLines.length,
    viewportHeight,
    activeSessionId
  )

  // Home/End are invisible to ink 5's key flags — matched as raw
  // escape sequences on the same input channel useInput listens on.
  useHomeEndKeys(scroll.toTop, scroll.toBottom, !overviewVisible)

  useEffect(() => {
    activeIdRef.current = activeSessionId
  }, [activeSessionId])

  useEffect(() => {
    sessionsRef.current = tabs.sessions
  }, [tabs.sessions])

  useEffect(() => {
    overviewRef.current = overviewVisible
  }, [overviewVisible])

  const persist = useCallback((state: SessionsState) => {
    void writeSessionsFile(state).catch(() => {
      // Restore is best-effort; a failed write never breaks the chat.
    })
  }, [])

  // -- connect + restore -------------------------------------------------------

  useEffect(() => {
    let disposed = false
    let hub: HubConnection | null = null

    void (async () => {
      const client = new ComukiClient(config, {
        onLatencySample: setLatencyMs,
      })
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
          }
        } catch {
          label = wanted
        }
      }
      if (disposed) {
        return
      }
      projectIdRef.current = projectId
      setProjectLabel(label)

      try {
        // Best-effort live progress; the REST turns are authoritative.
        // A reconnect gets a fresh connection id — every chat group is
        // gone, so re-join the open sessions (pending tabs excluded).
        const connection = await startChatHubConnection({
          hubUrl: client.hubUrl(),
          headers: client.hubHeaders(),
          onStateChange: setHubState,
          onReconnected: () => {
            const hub = hubRef.current
            if (hub) {
              const sessionIds = sessionsRef.current
                .map((session) => session.id)
                .filter((id) => !id.startsWith(PENDING_PREFIX))
              void rejoinChatGroups(hub, sessionIds)
            }
          },
        })
        if (connection && !disposed) {
          hub = connection
          hubRef.current = connection
          bindChatEvents(
            connection,
            (chunk) => {
              // One connection, many sessions — route by payload id.
              setTabs((current) => ({
                ...current,
                sessions: appendLiveText(
                  current.sessions,
                  chunk.sessionId,
                  chunk.text
                ),
              }))
            },
            () => {
              // ChatTurnComplete — the POST result renders the turn.
            }
          )
        }
        // Hub attempt resolved (connect or fallback). Whichever path the
        // transport took, the placeholder text in the StatusLine is no
        // longer accurate — we are not "truly disconnected" anymore, we
        // are either live or we are on the REST-only path that works.
        if (!disposed) {
          setHubAttempted(true)
        }

        const restored = fromPersisted(await readSessionsFile())
        if (!disposed) {
          setTabs(restored)
          if (hub) {
            for (const session of restored.sessions) {
              await joinChatGroup(hub, session.id)
            }
          }
          if (restored.sessions.length > 0) {
            setWelcomeDismissed(true)
          }
          setBootstrapped(true)
        }
      } catch (error) {
        if (!disposed) {
          setConnectError(describeError(error))
        }
      }

      // Welcome stats — workers running + knowledge docs, best effort.
      try {
        const [compute, knowledge] = await Promise.all([
          client.compute(),
          client.knowledgeDocuments(1, 1),
        ])
        if (!disposed) {
          setStats({
            workers: compute.pools.reduce((sum, pool) => sum + pool.running, 0),
            memory: knowledge.total,
          })
        }
      } catch {
        // The welcome line just omits itself offline.
      }
    })()

    return () => {
      disposed = true
      const connection = hub ?? hubRef.current
      if (connection) {
        void connection.stop()
      }
    }
  }, [config, project])

  // -- lazy hydration for restored tabs ----------------------------------------
  // Deps are the stable identity fields — transcript updates must not
  // restart an in-flight fetch.

  useEffect(() => {
    if (
      !bootstrapped ||
      !activeSessionId ||
      activeHydrated ||
      activeSessionId.startsWith(PENDING_PREFIX)
    ) {
      return
    }
    const client = clientRef.current
    const sessionId = activeSessionId
    let cancelled = false

    void (async () => {
      if (!client) {
        return
      }
      try {
        const first = await client.listMessages(sessionId, 1, 50)
        const lastPage = Math.max(1, Math.ceil(first.total / 50))
        const page =
          lastPage === 1
            ? first
            : await client.listMessages(sessionId, lastPage, 50)
        if (cancelled) {
          return
        }
        const blocks: ChatBlock[] = page.items.map((message, index) => ({
          kind: "message",
          key: `${sessionId}-h${index}`,
          message,
        }))
        // `/retry` survives a restart — the server transcript still knows
        // the last user text even though the local tab state was rebuilt.
        const lastUser = [...page.items]
          .reverse()
          .find((item) => item.role === "user")
        setTabs((current) => ({
          ...current,
          sessions: patchSession(
            setBlocks(current.sessions, sessionId, blocks),
            sessionId,
            {
              hydrated: true,
              status: "done",
              ...(lastUser ? { lastUserMessage: lastUser.content } : {}),
            }
          ),
        }))
      } catch (error) {
        if (cancelled) {
          return
        }
        if (error instanceof ComukiApiError && error.status === 404) {
          // Server session is gone — drop the tab.
          setTabs((current) =>
            removeSession(
              current,
              current.sessions.findIndex(
                (candidate) => candidate.id === sessionId
              )
            )
          )
          return
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(current.sessions, sessionId, {
            hydrated: true,
          }),
        }))
      }
    })()

    return () => {
      cancelled = true
    }
  }, [bootstrapped, activeSessionId, activeHydrated])

  // -- persist the tab list on every change ------------------------------------

  useEffect(() => {
    if (bootstrapped) {
      persist(tabs)
    }
  }, [bootstrapped, tabs, persist])

  // -- turns --------------------------------------------------------------------

  const describeTurn = useCallback(
    (
      sessionId: string,
      result: {
        messages: readonly ChatMessageView[]
        awaitingApproval: boolean
        pendingPlan: unknown
      }
    ) => {
      setTabs((current) => {
        let sessions = appendBlocks(
          current.sessions,
          sessionId,
          result.messages.map((message) => ({
            kind: "message" as const,
            message,
          }))
        )
        sessions = patchSession(sessions, sessionId, {
          awaitingApproval: result.awaitingApproval,
          pendingPlan: result.pendingPlan,
          status: "done",
          liveText: "",
        })
        if (sessionId !== activeIdRef.current) {
          sessions = markUnread(sessions, sessionId, activeIdRef.current)
        }
        return { ...current, sessions }
      })
    },
    []
  )

  const runTurn = useCallback(
    async (
      sessionId: string,
      kind: "message" | "approve",
      payload: { message?: string; approved?: boolean; reason?: string }
    ) => {
      const client = clientRef.current
      if (!client) {
        return
      }
      setTabs((current) => ({
        ...current,
        sessions: patchSession(current.sessions, sessionId, {
          status: "thinking",
          liveText: "",
        }),
      }))
      try {
        const result =
          kind === "message"
            ? await client.postMessage(sessionId, payload.message ?? "")
            : await client.approve(
                sessionId,
                payload.approved ?? true,
                payload.reason
              )
        describeTurn(sessionId, result)
      } catch (error) {
        setTabs((current) => ({
          ...current,
          sessions: patchSession(
            appendBlocks(current.sessions, sessionId, [
              {
                kind: "lines",
                lines: [
                  "",
                  `${colors.red}${symbols.cross} ${describeError(error)}${colors.reset}`,
                ],
              },
            ]),
            sessionId,
            { status: "idle" }
          ),
        }))
      } finally {
        setTabs((current) => ({
          ...current,
          sessions: patchSession(current.sessions, sessionId, {
            liveText: "",
          }),
        }))
      }
    },
    [describeTurn]
  )

  // -- session lifecycle --------------------------------------------------------

  const openPendingTab = useCallback(() => {
    setTabs((current) =>
      addSession(
        current,
        newPendingSession(Date.now(), current.sessions.length)
      )
    )
  }, [])

  const closeSession = useCallback(
    (index: number) => {
      const closing = tabs.sessions[index]
      if (closing && !closing.id.startsWith(PENDING_PREFIX)) {
        const hub = hubRef.current
        if (hub) {
          void leaveChatGroup(hub, closing.id)
        }
      }
      // The task keeps running on the server — only the tab goes away.
      setTabs((current) => removeSession(current, index))
    },
    [tabs.sessions]
  )

  const focusSession = useCallback((index: number) => {
    setTabs((current) => {
      if (index < 0 || index >= current.sessions.length) {
        return current
      }
      const chosen = current.sessions[index]
      if (!chosen) {
        return current
      }
      return {
        ...current,
        activeIndex: index,
        sessions: patchSession(current.sessions, chosen.id, {
          unread: false,
        }),
      }
    })
  }, [])

  const selectSession = useCallback(
    (index: number) => {
      focusSession(index)
      setOverviewVisible(false)
    },
    [focusSession]
  )

  /** Sends `message`, creating the server session first when pending. */
  const sendMessage = useCallback(
    async (target: Session | undefined, message: string) => {
      const client = clientRef.current
      if (!client) {
        return
      }
      setWelcomeDismissed(true)
      setNoticeLines([])
      const name = titleForFirstMessage(target, message)
      const userEcho: readonly string[] = [
        `${colors.accent}you  ${symbols.prompt}${colors.reset} ${message}`,
      ]

      if (!target || target.id.startsWith(PENDING_PREFIX)) {
        try {
          const session = await client.createSession({
            projectId: projectIdRef.current,
            title: name,
          })
          const hub = hubRef.current
          if (hub) {
            await joinChatGroup(hub, session.id)
          }
          setTabs((current) => {
            const next = target
              ? adoptServerId(current, target.id, session.id, name)
              : addSession(current, {
                  ...newPendingSession(),
                  id: session.id,
                  name,
                  hydrated: true,
                  // The welcome-screen turn has no tab yet — seed the
                  // new session's recall history with its first message.
                  history: [message],
                })
            return {
              ...next,
              sessions: patchSession(
                appendBlocks(next.sessions, session.id, [
                  { kind: "lines", lines: userEcho },
                ]),
                session.id,
                { lastUserMessage: message }
              ),
            }
          })
          await runTurn(session.id, "message", { message })
        } catch (error) {
          setNoticeLines([
            "",
            `${colors.red}${symbols.cross} ${describeError(error)}${colors.reset}`,
          ])
        }
        return
      }

      setTabs((current) => ({
        ...current,
        sessions: patchSession(
          appendBlocks(current.sessions, target.id, [
            { kind: "lines", lines: userEcho },
          ]),
          target.id,
          { lastUserMessage: message }
        ),
      }))
      await runTurn(target.id, "message", { message })
    },
    [runTurn]
  )

  const pushLines = useCallback(
    (sessionId: string, lines: readonly string[]) => {
      setTabs((current) => ({
        ...current,
        sessions: appendBlocks(current.sessions, sessionId, [
          { kind: "lines", lines },
        ]),
      }))
    },
    []
  )

  // -- input ----------------------------------------------------------------------

  const handleSubmit = useCallback(
    (raw: string) => {
      const value = raw.trim()
      const target =
        tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
      if (value.length === 0 || target?.status === "thinking") {
        return
      }
      // Per-session recall history — rides the session record, so it
      // persists with the tab and survives the pending → live adoption.
      if (target) {
        setTabs((current) => ({
          ...current,
          sessions: appendHistory(current.sessions, target.id, value),
        }))
      }

      const action = resolveSlashAction(value)

      switch (action.kind) {
        case "exit": {
          exit()
          return
        }
        case "clear": {
          if (target) {
            setTabs((current) => ({
              ...current,
              sessions: setBlocks(current.sessions, target.id, []),
            }))
          }
          return
        }
        case "help": {
          const lines = slashHelpLines()
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
          return
        }
        case "sessions": {
          setOverviewVisible(true)
          return
        }
        case "new": {
          openPendingTab()
          return
        }
        case "retry": {
          const last = retryMessage(target)
          if (!last) {
            if (target) {
              pushLines(target.id, [NOTHING_TO_RETRY])
            } else {
              setNoticeLines([NOTHING_TO_RETRY])
            }
            return
          }
          void sendMessage(target, last)
          return
        }
        case "rename": {
          if (!target) {
            setNoticeLines([
              `${colors.dim}  no active session to rename${colors.reset}`,
            ])
            return
          }
          if (action.title.length === 0) {
            pushLines(target.id, [
              `${colors.dim}  usage: /rename <title>${colors.reset}`,
            ])
            return
          }
          setTabs((current) => ({
            ...current,
            sessions: renameSession(current.sessions, target.id, action.title),
          }))
          pushLines(target.id, [
            `${colors.green}${symbols.checkmark} renamed to ${action.title}${colors.reset}`,
          ])
          return
        }
        case "approve":
        case "reject": {
          if (!target) {
            return
          }
          if (!target.awaitingApproval) {
            pushLines(target.id, [
              `${colors.dim}  nothing to approve — the brain did not interrupt${colors.reset}`,
            ])
            return
          }
          const approved = action.kind === "approve"
          const reason = action.kind === "reject" ? action.reason : undefined
          pushLines(target.id, [
            approved
              ? `${colors.green}${symbols.checkmark} approving…${colors.reset}`
              : `${colors.yellow}${symbols.bullet} rejecting…${colors.reset}`,
          ])
          void runTurn(target.id, "approve", { approved, reason })
          return
        }
        case "message": {
          void sendMessage(target, value)
          return
        }
      }
    },
    [exit, openPendingTab, pushLines, runTurn, sendMessage, tabs]
  )

  // Global hotkeys — active in every state; the editor ignores these keys.
  useInput((input, key) => {
    if (overviewRef.current) {
      return // the overview's own handler owns the keys
    }
    // PgUp/PgDn: ink parses the standard sequences (`\x1b[5~` / `\x1b[6~`,
    // what ConPTY sends) into `key.pageUp` / `key.pageDown`; a sequence
    // the parser does not know still reaches us raw in `input` (leading
    // ESC already stripped) — both shapes scroll.
    if (key.pageUp || input === "\x1b[5~" || input === "[5~") {
      scroll.pageUp()
      return
    }
    if (key.pageDown || input === "\x1b[6~" || input === "[6~") {
      scroll.pageDown()
      return
    }
    // ↑/↓ scroll the transcript by one line while it is scrolled up;
    // at the bottom they keep their prompt-history-recall meaning
    // (PromptInput checks `historyRecallEnabled` for the same flag).
    if (key.upArrow && scroll.scrolledUp) {
      scroll.lineUp()
      return
    }
    if (key.downArrow && scroll.scrolledUp) {
      scroll.lineDown()
      return
    }
    if (key.tab) {
      setTabs((current) => {
        if (current.sessions.length === 0) {
          return current
        }
        const nextIndex = stepActive(
          current.sessions.length,
          current.activeIndex,
          key.shift ? -1 : 1
        )
        const chosen = current.sessions[nextIndex]
        return {
          ...current,
          activeIndex: nextIndex,
          sessions: chosen
            ? patchSession(current.sessions, chosen.id, { unread: false })
            : current.sessions,
        }
      })
      return
    }
    if (key.escape) {
      setOverviewVisible((current) => !current)
      return
    }
    if (key.ctrl && input === "n") {
      openPendingTab()
      return
    }
    if (key.ctrl && input === "w") {
      closeSession(tabs.activeIndex)
    }
  })

  useEffect(() => {
    if (connectError) {
      process.exitCode = 1
    }
  }, [connectError])

  useEffect(() => {
    if (connectError) {
      process.exitCode = 1
    }
  }, [connectError])

  // The placeholder lives in the StatusLine identity slot. We show it only
  // while we are "truly disconnected" (hub attempt not yet decided);
  // once the attempt resolves — connect or fallback to REST-only — the
  // real identity takes over and the placeholder disappears.
  const headerIdentity = hubAttempted ? identity : "connecting…"

  if (connectError) {
    return (
      <Box
        flexDirection="column"
        width={columns}
        height={rows}
        alignItems="center"
        justifyContent="center"
      >
        <StatusLine
          identity={headerIdentity}
          project={projectLabel}
          connection={hubState}
          serverUrl={config.url}
          latencyMs={latencyMs}
        />
        <Box marginTop={1}>
          <Text>
            {"  "}
            <Text color="red">
              {symbols.cross} {connectError}
            </Text>
          </Text>
        </Box>
        <Box marginTop={1}>
          <Text dimColor>
            {"  "}check COMUKI_URL / COMUKI_API_KEY, or run comuki login
          </Text>
        </Box>
      </Box>
    )
  }

  const showWelcome = !welcomeDismissed && tabs.sessions.length === 0
  const promptEnabled =
    !overviewVisible && (!activeSession || activeSession.status !== "thinking")

  // Header: status line + (when tabs exist) the tab strip — both single rows.
  // Content: the scrolling transcript viewport, filling everything the
  // chrome does not claim. Footer: session badges + legend — single row.
  // Prompt block: pinned last, never scrolled away.
  return (
    <Box flexDirection="column" width={columns} height={rows}>
      <StatusLine
        identity={headerIdentity}
        project={projectLabel}
        connection={hubState}
        serverUrl={config.url}
        latencyMs={latencyMs}
      />
      {tabs.sessions.length > 0 ? (
        <TabBar sessions={tabs.sessions} activeIndex={tabs.activeIndex} />
      ) : null}
      <Box
        flexDirection="column"
        flexGrow={1}
        flexShrink={1}
        minHeight={0}
        overflow="hidden"
      >
        {overviewVisible ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <SessionOverview
              sessions={tabs.sessions}
              activeIndex={tabs.activeIndex}
              onSelect={selectSession}
              onNewSession={() => {
                openPendingTab()
                setOverviewVisible(false)
              }}
              onClose={() => setOverviewVisible(false)}
            />
          </Box>
        ) : showWelcome ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <Welcome stats={stats} />
            {noticeLines.map((line, index) => (
              <Text key={index}>{line}</Text>
            ))}
          </Box>
        ) : activeSession ? (
          <TranscriptViewport
            lines={transcriptLines}
            height={viewportHeight}
            offset={scroll.offset}
            newBelow={scroll.newBelow}
          />
        ) : (
          <Text>{EMPTY_TAB_HINT}</Text>
        )}
      </Box>
      {showFooter ? (
        <SessionFooter
          sessions={tabs.sessions}
          activeIndex={tabs.activeIndex}
        />
      ) : null}
      {!overviewVisible ? (
        <>
          {copyHint ? (
            <Text dimColor>{`  ${copyHint}`}</Text>
          ) : null}
          <PromptInput
            onSubmit={handleSubmit}
            history={activeSession?.history ?? []}
            active={promptEnabled}
            historyRecallEnabled={!scroll.scrolledUp}
          />
        </>
      ) : null}
    </Box>
  )
}

/** Human shape of any failure: HTTP problems keep their code + detail. */
export function describeError(error: unknown): string {
  if (error instanceof ComukiApiError) {
    return `HTTP ${error.status}${error.code ? ` (${error.code})` : ""}: ${error.detail ?? "request failed"}`
  }
  const message = error instanceof Error ? error.message : String(error)
  if (
    /unable to connect|fetch failed|econnrefused|connection refused/i.test(
      message
    )
  ) {
    return "server unreachable"
  }
  return message
}

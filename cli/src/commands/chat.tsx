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
  isAbortError,
  type ChatMessageView,
} from "../lib/client"
import { whoAmI } from "../lib/auth"
import {
  expandHintLine,
  findMatches,
  flattenTranscript,
  hasCollapsedThinking,
  nextMatchIndex,
} from "../lib/transcript"
import type { ResolvedConfig } from "../lib/config"
import { readConfigFile, writeConfigFile } from "../lib/config"
import { exportFileName, exportMarkdown } from "../lib/export"
import { terminalTitle, turnDoneSequences, writeTerminal } from "../lib/term"
import {
  expandMentions,
  extractMentions,
  mentionNoticeLines,
  stripMentionPreamble,
  type MentionExpansion,
} from "../lib/mentions"
import { useMentionMenu } from "../components/MentionMenu"
import { useStdoutDimensions } from "../hooks/useStdoutDimensions"
import { useCopyLastAnswer } from "../hooks/useCopyLastAnswer"
import { useHomeEndKeys } from "../hooks/useHomeEndKeys"
import { useSpinnerFrame } from "../hooks/useSpinnerFrame"
import { useTerminalTitle } from "../hooks/useTerminalTitle"
import { useTranscriptScroll } from "../hooks/useTranscriptScroll"
import { lastAssistantText } from "../lib/history"
import {
  dequeueMessage,
  enqueueMessage,
  queuedNoticeLine,
  queueHintLine,
  stoppedNoticeLine,
} from "../lib/queue"
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
  toggleBlocksExpanded,
  writeSessionsFile,
  PENDING_PREFIX,
  type ChatBlock,
  type Session,
  type SessionsState,
} from "../lib/sessions"
import { resolveSlashAction, slashHelpLines } from "../lib/slash"
import {
  mergeRunsFeedRefresh,
  planPanelLines,
  projectListingLines,
  projectSwitchedLine,
  renderRunsFeedPanel,
  resolveProject,
  RUNS_FEED_REFRESH_MS,
} from "../lib/runsfeed"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  rejoinChatGroups,
  startChatHubConnection,
  type HubConnectionState,
} from "../lib/signalr"
import { colors, palette, symbols } from "../theme"
import { PromptInput } from "../components/PromptInput"
import { SessionFooter } from "../components/SessionFooter"
import { SessionOverview } from "../components/SessionOverview"
import { StatusLine } from "../components/StatusLine"
import { TabBar } from "../components/TabBar"
import { TranscriptSearch } from "../components/TranscriptSearch"
import { TranscriptViewport } from "../components/TranscriptViewport"
import { Welcome, type PlatformStats } from "../components/Welcome"
import { fetchRunsFeedPanel } from "./runsfeed"

const EMPTY_TAB_HINT = `${colors.faint}  no open sessions — ctrl+n to start one${colors.reset}`

const NOTHING_TO_RETRY = `${colors.faint}  nothing to retry — no message sent yet${colors.reset}`

const NOTHING_TO_STOP = `${colors.faint}  nothing to stop — no turn is running${colors.reset}`

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
  /**
   * BEL on turn completion (config `bell`, default on). OSC 9 toasts
   * are always emitted — `/bell off` only silences the audible byte.
   */
  const [bellEnabled, setBellEnabled] = useState(config.bell)
  /** Mirror for callbacks — `describeTurn` reads the live value. */
  const bellRef = useRef(config.bell)
  /** Rendered height of the prompt block (menu rows + wrapped lines). */
  const [promptRows, setPromptRows] = useState(1)

  const clientRef = useRef<ComukiClient | null>(null)
  const hubRef = useRef<HubConnection | null>(null)
  const projectIdRef = useRef<string | undefined>(undefined)
  const activeIdRef = useRef<string | undefined>(undefined)
  const overviewRef = useRef(false)
  /**
   * The slash menu owns tab/esc/↑/↓ while it is open — the shell's own
   * useInput reads this ref to yield those keys for those keystrokes.
   */
  const slashMenuOpenRef = useRef(false)
  /** One AbortController per in-flight turn — /stop aborts through it. */
  const turnControllersRef = useRef(new Map<string, AbortController>())
  /**
   * Sessions whose aborted turn may still stream SignalR chunks (the
   * server keeps processing); chunks are ignored until the next turn.
   */
  const mutedSessionsRef = useRef(new Set<string>())
  /** Reconnect re-join needs the session ids without a closure snapshot. */
  const sessionsRef = useRef<readonly Session[]>([])

  const activeSession =
    tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
  const activeSessionId = activeSession?.id
  const activeHydrated = activeSession?.hydrated

  // The window title follows the active tab: ⏳ while its turn is in
  // flight, ✓ once it settles; a bare "comuki" when no tab is open.
  useTerminalTitle(
    terminalTitle(activeSession?.name, activeSession?.status === "thinking")
  )

  // ctrl+y copies the last assistant answer; hint is rendered near the
  // prompt (getter is kept fresh by the hook, no stale transcript).
  const { hint: copyHint } = useCopyLastAnswer(() =>
    lastAssistantText(activeSession?.blocks ?? [])
  )

  // Prompt-block reporting — stable callbacks so the effects inside
  // PromptInput (menu flip, row count) don't re-fire every render.
  const handleMenuOpenChange = useCallback((open: boolean) => {
    slashMenuOpenRef.current = open
  }, [])
  const handlePromptRows = useCallback((rows: number) => {
    setPromptRows(rows)
  }, [])

  // -- @mentions ------------------------------------------------------------

  /**
   * Client-side mention resolution (pragmatic v1 — no server changes):
   * `@query ` in a submitted prompt searches knowledge and the top
   * hits ride the wire as an invisible `[@knowledge: …]` preamble the
   * brain sees; the local echo shows only what the user typed. Memory
   * facts are MCP-only (worker-gated), so mentions resolve against
   * knowledge alone. One refusal (401/403/404) flags the feature off
   * for the session — mentions then send as plain text, with a notice.
   */
  const knowledgeDisabledRef = useRef(false)
  const docTitlesRef = useRef<Map<string, string> | null>(null)

  /** Document titles for labels/notices — one best-effort page, cached. */
  const ensureDocTitles = useCallback(async () => {
    if (docTitlesRef.current === null) {
      docTitlesRef.current = new Map()
      try {
        const page = await clientRef.current?.knowledgeDocuments(1, 100)
        for (const document of page?.items ?? []) {
          docTitlesRef.current.set(document.id, document.title)
        }
      } catch {
        // Labels fall back to the snippets' first lines.
      }
    }
    const titles = docTitlesRef.current
    return (documentId: string) => titles.get(documentId)
  }, [])

  /** Raw search seam — throws so `expandMentions` can report refusal. */
  const mentionSearchRaw = useCallback((query: string) => {
    const client = clientRef.current
    return client
      ? client.knowledgeSearch(query, 5)
      : Promise.resolve([])
  }, [])

  /** Menu path — a failure flags the feature off silently. */
  const mentionMenuSearch = useCallback(
    async (query: string) => {
      if (knowledgeDisabledRef.current) {
        return []
      }
      try {
        return await mentionSearchRaw(query)
      } catch {
        knowledgeDisabledRef.current = true
        return []
      }
    },
    [mentionSearchRaw]
  )

  const promptBusy = activeSession?.status === "thinking"
  const mentionMenu = useMentionMenu({
    search: mentionMenuSearch,
    titleFor: (documentId) => docTitlesRef.current?.get(documentId),
    enabled: () =>
      !knowledgeDisabledRef.current && !overviewVisible && !promptBusy,
    width: Math.max(24, columns - 4),
  })

  // Warm the title index alongside the hub connect — best effort.
  useEffect(() => {
    void ensureDocTitles()
  }, [ensureDocTitles])

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
              runsFeed: activeSession.runsFeed ?? null,
              thinking,
              liveText: activeSession.liveText,
              expanded: activeSession.blocksExpanded,
            }
          : undefined,
        columns,
        typingFrame,
        noticeLines
      ),
    [activeSession, columns, typingFrame, noticeLines, thinking]
  )

  // -- ctrl+f transcript search ---------------------------------------------------

  const [searchOpen, setSearchOpen] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [searchIndex, setSearchIndex] = useState(0)

  const searchMatches = useMemo(
    () => (searchOpen ? findMatches(transcriptLines, searchQuery) : []),
    [searchOpen, transcriptLines, searchQuery]
  )
  // The index can outlive the match list it was picked from (the query
  // shrank); the clamped value is what the counter and the jumps use.
  const searchCursor =
    searchMatches.length > 0
      ? Math.min(searchIndex, searchMatches.length - 1)
      : 0
  const activeMatchLine = searchMatches[searchCursor] ?? null

  // The ctrl+o hint rides the top row of the viewport — only while the
  // active tab actually hides thinking behind ⏺ event lines.
  const expandHint =
    activeSession &&
    hasCollapsedThinking(activeSession.blocks, activeSession.blocksExpanded)
      ? expandHintLine(columns)
      : null

  // Pinned chrome rows: status line + tab strip + footer + the prompt
  // block. The prompt block = the (possibly multiline, possibly
  // menu-carrying) prompt + the transient ctrl+y hint row + the queue
  // hint row. Everything left belongs to the scrolling viewport.
  const showFooter = tabs.sessions.length > 0 && !overviewVisible
  const queuedCount = activeSession?.queued?.length ?? 0
  const promptBlockRows =
    promptRows + (copyHint ? 1 : 0) + (queuedCount > 0 ? 1 : 0)
  const viewportHeight = Math.max(
    1,
    rows -
      1 - // StatusLine
      (tabs.sessions.length > 0 ? 1 : 0) - // TabBar
      (showFooter ? 1 : 0) - // SessionFooter
      (searchOpen ? 1 : 0) - // TranscriptSearch row
      promptBlockRows -
      (mentionMenu.menuOpen ? mentionMenu.rowCount : 0) // mention popup
  )

  const scroll = useTranscriptScroll(
    transcriptLines.length,
    viewportHeight,
    activeSessionId
  )

  // Home/End are invisible to ink 5's key flags — matched as raw
  // escape sequences on the same input channel useInput listens on.
  useHomeEndKeys(scroll.toTop, scroll.toBottom, !overviewVisible)

  // Search jumps drive the same offset the scroll keys use: enter
  // cycles the match cursor (wrapping), and a fresh query snaps to its
  // first match so find-as-you-type always lands somewhere visible.
  // Plain closures (re-created each render) keep the transcript lines
  // fresh — a streamed chunk between keystrokes must not yank the view.
  const handleSearchChange = (value: string) => {
    setSearchQuery(value)
    setSearchIndex(0)
    const first = findMatches(transcriptLines, value)[0]
    if (first !== undefined) {
      scroll.scrollToLine(first)
    }
  }

  const cycleSearch = (step: number) => {
    if (searchMatches.length === 0) {
      return
    }
    const next = nextMatchIndex(searchCursor, searchMatches.length, step)
    setSearchIndex(next)
    const line = searchMatches[next]
    if (line !== undefined) {
      scroll.scrollToLine(line)
    }
  }

  const handleSearchClose = () => {
    setSearchOpen(false)
    setSearchQuery("")
    setSearchIndex(0)
  }

  useEffect(() => {
    activeIdRef.current = activeSessionId
  }, [activeSessionId])

  useEffect(() => {
    sessionsRef.current = tabs.sessions
  }, [tabs.sessions])

  useEffect(() => {
    overviewRef.current = overviewVisible
  }, [overviewVisible])

  useEffect(() => {
    bellRef.current = bellEnabled
  }, [bellEnabled])

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
              // A stopped turn's leftovers never reach the tab.
              if (mutedSessionsRef.current.has(chunk.sessionId)) {
                return
              }
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
              // Server content carries the mention preamble — `/retry`
              // re-expands from the typed words, not the stored blocks.
              ...(lastUser
                ? { lastUserMessage: stripMentionPreamble(lastUser.content) }
                : {}),
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
      // The terminal may be unfocused — BEL (gated by /bell) plus the
      // OSC 9 toast (always); terminals without OSC 9 ignore it.
      writeTerminal(turnDoneSequences(bellRef.current))
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
      const controller = new AbortController()
      turnControllersRef.current.set(sessionId, controller)
      // A new turn unmutes the chunk stream (a previous /stop muted it).
      mutedSessionsRef.current.delete(sessionId)
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
            ? await client.postMessage(
                sessionId,
                payload.message ?? "",
                controller.signal
              )
            : await client.approve(
                sessionId,
                payload.approved ?? true,
                payload.reason,
                controller.signal
              )
        describeTurn(sessionId, result)
      } catch (error) {
        if (isAbortError(error)) {
          // /stop already flipped the tab out of thinking — stamp the
          // mark; the queue (if any) survives and drains next.
          setTabs((current) => ({
            ...current,
            sessions: patchSession(
              appendBlocks(current.sessions, sessionId, [
                { kind: "lines", lines: ["", stoppedNoticeLine()] },
              ]),
              sessionId,
              { status: "done", liveText: "" }
            ),
          }))
          return
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(
            appendBlocks(current.sessions, sessionId, [
              {
                kind: "lines",
                lines: [
                  "",
                  `${colors.error}${symbols.cross} ${describeError(error)}${colors.reset}`,
                ],
              },
            ]),
            sessionId,
            { status: "idle" }
          ),
        }))
      } finally {
        turnControllersRef.current.delete(sessionId)
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

  /** `/stop` — abort the in-flight turn from the client side. */
  const stopTurn = useCallback((sessionId: string) => {
    turnControllersRef.current.get(sessionId)?.abort()
    mutedSessionsRef.current.add(sessionId)
    setTabs((current) => ({
      ...current,
      sessions: patchSession(current.sessions, sessionId, {
        status: "done",
        liveText: "",
      }),
    }))
  }, [])

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

  /**
   * Expands the typed text when it carries mentions; a refused search
   * flags the feature off and returns the text unchanged (the send
   * must never block on the knowledge surface).
   */
  const expandTyped = useCallback(
    async (typed: string): Promise<MentionExpansion> => {
      if (knowledgeDisabledRef.current || extractMentions(typed).length === 0) {
        return {
          typed,
          outgoing: typed,
          resolutions: [],
          knowledgeUnavailable: false,
        }
      }
      const expansion = await expandMentions(typed, mentionSearchRaw)
      if (expansion.knowledgeUnavailable) {
        knowledgeDisabledRef.current = true
      }
      return expansion
    },
    [mentionSearchRaw]
  )

  /** Sends `message` (the typed text), creating the server session first when pending. */
  const sendMessage = useCallback(
    async (target: Session | undefined, message: string) => {
      const client = clientRef.current
      if (!client) {
        return
      }
      setWelcomeDismissed(true)
      setNoticeLines([])
      // Mentions expand before the send — the preamble rides the wire
      // while the echo keeps showing only what the user typed.
      const typed = stripMentionPreamble(message)
      const expansion = await expandTyped(typed)
      const notices =
        expansion.knowledgeUnavailable || expansion.resolutions.length > 0
          ? mentionNoticeLines(expansion, await ensureDocTitles())
          : []
      const name = titleForFirstMessage(target, typed)
      // The user's words as a message block: renders byte-identically
      // to the old ANSI echo lines (renderMessage → renderUserEcho)
      // and gives /export a real `## you` turn in live sessions.
      const userEchoBlock = {
        kind: "message" as const,
        message: {
          id: `local-${Date.now()}`,
          role: "user",
          content: typed,
          toolName: null,
          parts: null,
          meta: null,
          createdAt: new Date().toISOString(),
        },
      }

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
                  history: [typed],
                })
            return {
              ...next,
              sessions: patchSession(
                appendBlocks(next.sessions, session.id, [userEchoBlock]),
                session.id,
                { lastUserMessage: typed }
              ),
            }
          })
          if (notices.length > 0) {
            pushLines(session.id, notices)
          }
          await runTurn(session.id, "message", {
            message: expansion.outgoing,
          })
        } catch (error) {
          setNoticeLines([
            "",
            `${colors.error}${symbols.cross} ${describeError(error)}${colors.reset}`,
          ])
        }
        return
      }

      setTabs((current) => ({
        ...current,
        sessions: patchSession(
          appendBlocks(current.sessions, target.id, [userEchoBlock]),
          target.id,
          { lastUserMessage: typed }
        ),
      }))
      if (notices.length > 0) {
        pushLines(target.id, notices)
      }
      await runTurn(target.id, "message", { message: expansion.outgoing })
    },
    [ensureDocTitles, expandTyped, pushLines, runTurn]
  )

  // -- ops pack: /project ------------------------------------------------------

  /**
   * Switches the project context (`projectIdRef` seeds every NEW
   * server session; live sessions keep the project they were created
   * with). Without a query it lists the current context + every
   * visible project; an unresolvable query lists what is available.
   */
  const switchProject = useCallback(
    async (query: string) => {
      const client = clientRef.current
      if (!client) {
        return
      }
      const emit = (lines: readonly string[]) => {
        const target = tabs.sessions[tabs.activeIndex]
        if (target) {
          pushLines(target.id, lines)
        } else {
          setNoticeLines(lines)
        }
      }
      try {
        const projects = await client.projects()
        if (query.length === 0) {
          emit(projectListingLines(projectLabel ?? null, projects))
          return
        }
        const match = resolveProject(query, projects)
        if (!match) {
          emit([
            `${colors.faint}  unknown project '${query}'${colors.reset}`,
            ...projectListingLines(projectLabel ?? null, projects),
          ])
          return
        }
        projectIdRef.current = match.id
        setProjectLabel(match.slug)
        emit([projectSwitchedLine(match.slug)])
      } catch (error) {
        emit([
          `${colors.faint}  projects not available — ${describeError(error)}${colors.reset}`,
        ])
      }
    },
    [projectLabel, pushLines, tabs]
  )

  // -- ops pack: /runs auto-refresh ---------------------------------------------

  /**
   * The pinned `/runs` panel is the one live transcript block: while
   * its session is the ACTIVE one, re-fetch on the
   * `RUNS_FEED_REFRESH_MS` cadence and patch the panel in place
   * (`mergeRunsFeedRefresh` keeps the last rows across a failed poll).
   * Cleanup drops the interval when the panel clears, the tab switches
   * away, or the app unmounts; a background tab's panel keeps its last
   * snapshot until it is focused again or `/runs` re-pins it fresh.
   */
  const runsFeedSessionId =
    activeSession?.runsFeed != null ? activeSession.id : null

  useEffect(() => {
    const client = clientRef.current
    if (!runsFeedSessionId || !client) {
      return
    }
    const sessionId = runsFeedSessionId
    const timer = setInterval(() => {
      void fetchRunsFeedPanel(client).then((panel) => {
        setTabs((current) => ({
          ...current,
          sessions: current.sessions.map((session) =>
            session.id === sessionId
              ? {
                  ...session,
                  runsFeed: mergeRunsFeedRefresh(session.runsFeed, panel),
                }
              : session
          ),
        }))
      })
    }, RUNS_FEED_REFRESH_MS)
    return () => clearInterval(timer)
  }, [runsFeedSessionId])

  // -- input ----------------------------------------------------------------------

  const handleSubmit = useCallback(
    (raw: string) => {
      const value = raw.trim()
      const target =
        tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
      if (value.length === 0) {
        return
      }
      // Per-session recall history — rides the session record, so it
      // persists with the tab and survives the pending → live adoption.
      // Queued messages land here too: they were submitted by the user.
      if (target) {
        setTabs((current) => ({
          ...current,
          sessions: appendHistory(current.sessions, target.id, value),
        }))
      }

      const action = resolveSlashAction(value)

      // A thinking turn owns the wire: chat messages (and /retry's
      // resend) queue instead of firing a second concurrent POST.
      if (
        target?.status === "thinking" &&
        (action.kind === "message" || action.kind === "retry")
      ) {
        const queued =
          action.kind === "retry" ? retryMessage(target) : value
        if (queued === null) {
          pushLines(target.id, [NOTHING_TO_RETRY])
          return
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(current.sessions, target.id, {
            queued: enqueueMessage(target.queued ?? [], queued),
          }),
        }))
        pushLines(target.id, [queuedNoticeLine()])
        return
      }

      switch (action.kind) {
        case "exit": {
          exit()
          return
        }
        case "stop": {
          if (!target || target.status !== "thinking") {
            const line = NOTHING_TO_STOP
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          stopTurn(target.id)
          return
        }
        case "clear": {
          if (target) {
            setTabs((current) => ({
              ...current,
              sessions: patchSession(
                setBlocks(current.sessions, target.id, []),
                target.id,
                { runsFeed: null }
              ),
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
              `${colors.faint}  no active session to rename${colors.reset}`,
            ])
            return
          }
          if (action.title.length === 0) {
            pushLines(target.id, [
              `${colors.faint}  usage: /rename <title>${colors.reset}`,
            ])
            return
          }
          setTabs((current) => ({
            ...current,
            sessions: renameSession(current.sessions, target.id, action.title),
          }))
          pushLines(target.id, [
            `${colors.ok}${symbols.checkmark} renamed to ${action.title}${colors.reset}`,
          ])
          return
        }
        case "export": {
          if (!target) {
            setNoticeLines([
              `${colors.faint}  no active session to export${colors.reset}`,
            ])
            return
          }
          const markdown = exportMarkdown(target.blocks)
          if (markdown.length === 0) {
            pushLines(target.id, [
              `${colors.faint}  nothing to export — the transcript is empty${colors.reset}`,
            ])
            return
          }
          const path = action.path ?? exportFileName(target.name)
          const lineCount = markdown.split("\n").length
          void (async () => {
            try {
              await Bun.write(path, markdown)
              pushLines(target.id, [
                `${colors.faint}  ${symbols.checkmark} exported ${path} · ${lineCount} lines${colors.reset}`,
              ])
            } catch (error) {
              pushLines(target.id, [
                `${colors.error}${symbols.cross} export failed: ${describeError(error)}${colors.reset}`,
              ])
            }
          })()
          return
        }
        case "bell": {
          if (action.enabled === undefined) {
            const state = bellEnabled ? "on" : "off"
            const statusLine = `${colors.faint}  bell is ${state} — /bell on|off${colors.reset}`
            if (target) {
              pushLines(target.id, [statusLine])
            } else {
              setNoticeLines([statusLine])
            }
            return
          }
          const next = action.enabled
          setBellEnabled(next)
          bellRef.current = next
          void (async () => {
            try {
              // Read-modify-write: the cookie and login state must survive.
              const contents = await readConfigFile()
              await writeConfigFile({ ...contents, bell: next })
            } catch {
              // Best-effort persistence; this session's toggle already applies.
            }
          })()
          const confirmLine = next
            ? `${colors.ok}${symbols.checkmark} bell on${colors.reset}`
            : `${colors.ok}${symbols.checkmark} bell off — osc 9 toasts stay on${colors.reset}`
          if (target) {
            pushLines(target.id, [confirmLine])
          } else {
            setNoticeLines([confirmLine])
          }
          return
        }
        case "approve":
        case "reject": {
          if (!target) {
            return
          }
          if (!target.awaitingApproval) {
            pushLines(target.id, [
              `${colors.faint}  nothing to approve — the brain did not interrupt${colors.reset}`,
            ])
            return
          }
          const approved = action.kind === "approve"
          const reason = action.kind === "reject" ? action.reason : undefined
          pushLines(target.id, [
            approved
              ? `${colors.ok}${symbols.checkmark} approving…${colors.reset}`
              : `${colors.waiting}${symbols.bullet} rejecting…${colors.reset}`,
          ])
          void runTurn(target.id, "approve", { approved, reason })
          return
        }
        case "runs": {
          const client = clientRef.current
          if (!client) {
            return
          }
          const sessionId = target?.id
          void fetchRunsFeedPanel(client).then((panel) => {
            if (sessionId) {
              // Re-invoking /runs re-renders the panel fresh: the merge
              // only rescues rows when the new fetch itself failed.
              setTabs((current) => ({
                ...current,
                sessions: current.sessions.map((session) =>
                  session.id === sessionId
                    ? {
                        ...session,
                        runsFeed: mergeRunsFeedRefresh(session.runsFeed, panel),
                      }
                    : session
                ),
              }))
            } else {
              // No transcript to pin into — one static snapshot on the
              // welcome screen (no auto-refresh loop without a session).
              setNoticeLines(renderRunsFeedPanel(panel))
            }
          })
          return
        }
        case "plan": {
          const lines = planPanelLines(
            target?.blocks ?? [],
            target?.pendingPlan ?? null
          )
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
          return
        }
        case "project": {
          void switchProject(action.query)
          return
        }
        case "message": {
          void sendMessage(target, value)
          return
        }
      }
    },
    [bellEnabled, exit, openPendingTab, pushLines, runTurn, sendMessage, stopTurn, switchProject, tabs]
  )

  // -- queued-message drain -----------------------------------------------------
  // The moment a session stops thinking (turn done, stopped or failed),
  // its queue sends in order: one dequeue per commit, head first — the
  // dequeue happens before the send so a re-render cannot resend it.

  useEffect(() => {
    for (const session of tabs.sessions) {
      if (session.status !== "thinking" && (session.queued?.length ?? 0) > 0) {
        const { message, rest } = dequeueMessage(session.queued ?? [])
        if (message === undefined) {
          continue
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(current.sessions, session.id, {
            queued: rest,
          }),
        }))
        void sendMessage(session, message)
        return
      }
    }
  }, [tabs.sessions, sendMessage])

  // Global hotkeys — active in every state; the editor ignores these keys.
  useInput((input, key) => {
    if (overviewRef.current) {
      return // the overview's own handler owns the keys
    }
    // While the ctrl+f search row is open it owns the keyboard: its
    // editor eats the query keystrokes and enter/esc drive the search.
    if (searchOpen) {
      return
    }
    // The slash menu owns tab (complete), esc (dismiss) and ↑/↓
    // (selection) while it is open; the mention popup owns the same
    // keys while IT is on screen — either open, the shell stays quiet.
    if (
      (slashMenuOpenRef.current || mentionMenu.menuOpen) &&
      (key.tab || key.escape || key.upArrow || key.downArrow)
    ) {
      return
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
    if (key.ctrl && input === "f") {
      // Transcript search — the inline row above the footer owns the
      // keyboard from here until esc closes it.
      setSearchOpen(true)
      return
    }
    if (key.ctrl && input === "o") {
      // Verbose toggle — flips the active tab's expand flag; the dynamic
      // viewport re-renders the whole transcript under the new mode.
      const target =
        tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
      if (target) {
        setTabs((current) => ({
          ...current,
          sessions: toggleBlocksExpanded(current.sessions, target.id),
        }))
        pushLines(target.id, [
          target.blocksExpanded
            ? `${colors.faint}  ${symbols.bullet} verbose off — thinking and tool blocks render collapsed${colors.reset}`
            : `${colors.faint}  ${symbols.bullet} verbose on — thinking and tool blocks render expanded${colors.reset}`,
        ])
      }
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
            <Text color={palette.error}>
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
  // The prompt stays live while a turn thinks: typing a message queues
  // it, `/stop` needs to be submittable mid-turn.
  const promptEnabled = !overviewVisible

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
            hint={expandHint}
            highlight={
              searchOpen && searchQuery.trim().length > 0
                ? { query: searchQuery, activeLine: activeMatchLine }
                : null
            }
          />
        ) : (
          <Text>{EMPTY_TAB_HINT}</Text>

        )}
      </Box>
      {searchOpen ? (
        <TranscriptSearch
          value={searchQuery}
          matchCount={searchMatches.length}
          matchIndex={searchCursor}
          onChange={handleSearchChange}
          onNext={() => cycleSearch(1)}
          onPrevious={() => cycleSearch(-1)}
          onClose={handleSearchClose}
        />
      ) : null}
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
          {queuedCount > 0 ? <Text>{queueHintLine(queuedCount)}</Text> : null}
          {mentionMenu.element}
          <PromptInput
            onSubmit={handleSubmit}
            history={activeSession?.history ?? []}
            active={promptEnabled && !searchOpen}
            historyRecallEnabled={!scroll.scrolledUp}
            onMenuOpenChange={handleMenuOpenChange}
            onRowsChange={handlePromptRows}
            {...mentionMenu.promptBindings}
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

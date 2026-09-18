/**
 * `comuki` (default) — the multi-session REPL.
 *
 * N parallel brain sessions switched like browser tabs: the tab strip
 * on top, the active transcript in the middle, session badges + expandable
 * action bar at the bottom. Turns are synchronous REST calls per session,
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
 * fresh output below, End resumes. The composer is pinned outside the
 * viewport and never scrolls away. `ctrl+p` opens the action palette.
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
import {
  describeAuthFailure,
  formatWhoamiLines,
  loginAndStore,
  whoAmI,
  whoFromError,
  whoFromMe,
} from "../lib/auth"
import {
  alertFromError,
  alertLines,
  isUnrecoverableError,
  type AlertCardModel,
} from "../lib/alerts"
import {
  expandHintLine,
  findMatches,
  flattenTranscript,
  hasCollapsedThinking,
  nextMatchIndex,
} from "../lib/transcript"
import type { ResolvedConfig } from "../lib/config"
import { readConfigFile, writeConfigFile } from "../lib/config"
import { archiveFilePath } from "../lib/archive"
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
import { useMouse } from "../hooks/useMouse"
import { useSpinnerFrame } from "../hooks/useSpinnerFrame"
import { useTerminalTitle } from "../hooks/useTerminalTitle"
import { useTranscriptScroll } from "../hooks/useTranscriptScroll"
import { lastCodeFence } from "../lib/format"
import { lastAssistantText } from "../lib/history"
import {
  isSgrMouseChunk,
  resolveMouseClick,
  type MouseLayout,
} from "../lib/mouse"
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
  branchOpener,
  forkTitle,
  fromPersisted,
  markUnread,
  newPendingSession,
  patchSession,
  readSessionsFile,
  removeSession,
  renameSession,
  retryMessage,
  sessionNameFromMessage,
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
import { viewportSlice } from "../lib/viewport"
import {
  aliasListingLines,
  expandAlias,
  getAlias,
  isValidAliasName,
  readAliasesFile,
  removeAlias,
  setAlias,
  writeAliasesFile,
} from "../lib/aliases"
import {
  hasContextualWorkbench,
  resolveHarnessLayout,
} from "../lib/harness-layout"
import {
  profileListingLines,
  profileStoredLine,
  resolveProfile,
} from "../lib/profiles"
import {
  getSnippet,
  isValidSnippetName,
  readSnippetsFile,
  removeSnippet,
  saveSnippet,
  snippetListingLines,
  writeSnippetsFile,
} from "../lib/snippets"
import {
  collectFiles,
  ingestRequestFor,
  kbAddErrorLine,
  kbAddResultLine,
  kbListLines,
  kbUsageLines,
  kbWriteUnavailableLines,
  KB_MAX_FILES,
  KB_PAGE_SIZE,
  validateIngestFile,
} from "../lib/kb"
import {
  mergeRunsFeedRefresh,
  planPanelLines,
  projectListingLines,
  projectSwitchedLine,
  renderRunsFeedPanel,
  renderWorkersPanel,
  resolveProject,
  RUNS_FEED_REFRESH_MS,
} from "../lib/runsfeed"
import {
  DASHBOARD_CHAT_PATH,
  dashboardOpenUrl,
  noteUnavailableLines,
  noteUsageLines,
  openDashboardUrl,
  openPanelLines,
  toolsUnavailableLines,
} from "../lib/ops"
import { fetchStatusSnapshot, renderStatusPanel } from "../lib/status"
import {
  bindChatEvents,
  joinChatGroup,
  leaveChatGroup,
  rejoinChatGroups,
  startChatHubConnection,
  type HubConnectionState,
} from "../lib/signalr"
import {
  DEFAULT_KEYBINDINGS,
  keybindingsListingLines,
  matchesBinding,
  readKeybindingsFile,
  type Keybindings,
} from "../lib/keybindings"
import {
  themeListingLines,
  themeSwitchedLine,
  themeUnknownLines,
} from "../lib/theme-command"
import {
  colors,
  currentThemeId,
  isThemeChoice,
  palette,
  resolveTheme,
  symbols,
} from "../theme"
import { AlertCard } from "../components/AlertCard"
import { Fill } from "../components/Fill"
import { PromptInput } from "../components/PromptInput"
import {
  SessionOverview,
  sessionTokenTotals,
} from "../components/SessionOverview"
import { LineInspector } from "../components/OverlaySheet"
import { hostFromUrl } from "../components/StatusLine"
import { TopBar } from "../components/TopBar"
import { TranscriptSearch } from "../components/TranscriptSearch"
import { TranscriptViewport } from "../components/TranscriptViewport"
import { Welcome } from "../components/Welcome"
import { CommandPalette } from "../components/CommandPalette"
import { ContextWorkbench } from "../components/ContextWorkbench"
import { fetchRunsFeedPanel } from "./runsfeed"

const EMPTY_TAB_HINT = `${colors.faint}  no open sessions — ctrl+n to start one${colors.reset}`

const NOTHING_TO_RETRY = `${colors.faint}  nothing to retry — no message sent yet${colors.reset}`

const NOTHING_TO_EDIT = `${colors.faint}  nothing to edit — no message sent yet${colors.reset}`

const NOTHING_TO_STOP = `${colors.faint}  nothing to stop — no turn is running${colors.reset}`

const NOTHING_TO_BRANCH = `${colors.faint}  nothing to branch from — no message sent yet${colors.reset}`

const NOTHING_TO_SAVE = `${colors.faint}  nothing to save — no message sent yet${colors.reset}`

const SNIP_USAGE = `${colors.faint}  usage: /snip [name|save <name>|rm <name>] — names are [a-z0-9-]+${colors.reset}`

const ALIAS_USAGE = `${colors.faint}  usage: /alias [set <name> <text>|rm <name>] — names are [a-z][a-z0-9-]*${colors.reset}`

const PROFILE_USAGE = `${colors.faint}  usage: /profile [name] — stored locally; createSession has no profile field${colors.reset}`

/**
 * The user's words as a transcript message block — the local echo,
 * byte-identical to what the server round-trip would render.
 */
function userEchoBlock(message: string): {
  kind: "message"
  message: ChatMessageView
} {
  return {
    kind: "message",
    message: {
      id: `local-${Date.now()}`,
      role: "user",
      content: message,
      toolName: null,
      parts: null,
      meta: null,
      createdAt: new Date().toISOString(),
    },
  }
}

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
  const [connectError, setConnectError] = useState<unknown>(null)
  const [noticeLines, setNoticeLines] = useState<readonly string[]>([])
  const [overviewVisible, setOverviewVisible] = useState(false)
  const [paletteVisible, setPaletteVisible] = useState(false)
  const [inspector, setInspector] = useState<{
    readonly title: string
    readonly lines: readonly string[]
  } | null>(null)
  const [promptSeed, setPromptSeed] = useState<
    { readonly value: string; readonly seq: number } | undefined
  >(undefined)
  /** The welcome screen never returns once the first message is sent. */
  const [welcomeDismissed, setWelcomeDismissed] = useState(false)
  const [bootstrapped, setBootstrapped] = useState(false)
  /** Hub chip for the status line; driven by the factory's state feed. */
  const [hubState, setHubState] = useState<HubConnectionState>("connecting")
  /** Chat-send EMA from the client — the status line latency badge. */
  const [latencyMs, setLatencyMs] = useState<number | null>(null)
  /**
   * BEL on turn completion (config `bell`, default on). OSC 9 toasts
   * are always emitted — `/bell off` only silences the audible byte.
   */
  const [bellEnabled, setBellEnabled] = useState(config.bell)
  /**
   * Preferred worker-profile key from config.json. Local only —
   * createSession has no profile field, so this never rides a turn.
   */
  const [preferredProfile, setPreferredProfile] = useState<string | undefined>(
    config.preferredProfile
  )
  /** Mirror for callbacks — `describeTurn` reads the live value. */
  const bellRef = useRef(config.bell)
  /** Overlay over the default chords — `/keys` and the shell useInput. */
  const [keybindings, setKeybindings] = useState<Keybindings>(
    DEFAULT_KEYBINDINGS
  )
  const keybindingsRef = useRef<Keybindings>(DEFAULT_KEYBINDINGS)
  /** Rendered height of the prompt block (menu rows + wrapped lines). */
  const [promptRows, setPromptRows] = useState(1)

  const clientRef = useRef<ComukiClient | null>(null)
  const configRef = useRef(config)
  const usingApiKeyRef = useRef(Boolean(config.apiKey))
  /**
   * Inline `/login` wizard: two prompts (email, then password) that
   * replace the chat input until the cookie is stored. Null = the
   * regular prompt.
   */
  const [loginStep, setLoginStep] = useState<"email" | "password" | null>(
    null
  )
  const loginEmailRef = useRef("")
  const pendingRetryRef = useRef<string | null>(null)
  const hubRef = useRef<HubConnection | null>(null)
  const projectIdRef = useRef<string | undefined>(undefined)
  const activeIdRef = useRef<string | undefined>(undefined)
  const overviewRef = useRef(false)
  const inspectorRef = useRef(false)
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
  /** Explicit hub stop (unmount) — the retry loop must not resurrect it. */
  const hubStopRef = useRef<(() => Promise<void>) | null>(null)

  const activeSession =
    tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
  const activeSessionId = activeSession?.id
  const activeHydrated = activeSession?.hydrated
  const showContextWorkbench = hasContextualWorkbench(activeSession)
  const harness = resolveHarnessLayout(columns, showContextWorkbench)
  const contentWidth = harness.workspaceWidth

  // The window title follows the active tab: ⏳ while its turn is in
  // flight, ✓ once it settles; a bare "comuki" when no tab is open.
  useTerminalTitle(
    terminalTitle(activeSession?.name, activeSession?.status === "thinking")
  )

  // ctrl+y (or overlay copy chord) copies the last assistant answer;
  // ctrl+shift+y / `/copycode` copies the last fenced code block.
  const { hint: copyHint, copyLastCode } = useCopyLastAnswer(
    () => lastAssistantText(activeSession?.blocks ?? []),
    {
      chord: keybindings.copy,
      getLastCodeFence: () => lastCodeFence(activeSession?.blocks ?? []),
    }
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
      !knowledgeDisabledRef.current &&
      !overviewVisible &&
      inspector === null &&
      !promptBusy,
    width: Math.max(24, contentWidth - 4),
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
              awaitingApproval:
                harness.workbenchWidth === 0 && activeSession.awaitingApproval,
              pendingPlan:
                harness.workbenchWidth === 0 ? activeSession.pendingPlan : null,
              runsFeed:
                harness.workbenchWidth === 0
                  ? activeSession.runsFeed ?? null
                  : null,
              thinking,
              liveText: activeSession.liveText,
              expanded: activeSession.blocksExpanded,
            }
          : undefined,
        contentWidth,
        typingFrame,
        noticeLines
      ),
    [
      activeSession,
      contentWidth,
      harness.workbenchWidth,
      typingFrame,
      noticeLines,
      thinking,
    ]
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
  // active tab actually hides thinking behind * event lines.
  const expandHint =
    activeSession &&
    hasCollapsedThinking(activeSession.blocks, activeSession.blocksExpanded)
      ? expandHintLine(columns)
      : null

  // One header row and the composer are the only permanent chrome.
  // Everything left belongs to the scrolling transcript.
  const queuedCount = activeSession?.queued?.length ?? 0
  const promptBlockRows =
    promptRows + (copyHint ? 1 : 0) + (queuedCount > 0 ? 1 : 0)
  const viewportHeight = Math.max(
    1,
    rows -
      harness.topBarRows -
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
    inspectorRef.current = inspector !== null
  }, [inspector])

  useEffect(() => {
    bellRef.current = bellEnabled
  }, [bellEnabled])

  useEffect(() => {
    keybindingsRef.current = keybindings
  }, [keybindings])

  useEffect(() => {
    void readKeybindingsFile().then((resolved) => {
      setKeybindings(resolved)
      keybindingsRef.current = resolved
    })
  }, [])

  useEffect(() => {
    configRef.current = config
    usingApiKeyRef.current = Boolean(config.apiKey)
  }, [config])

  const persist = useCallback((state: SessionsState) => {
    void writeSessionsFile(state).catch(() => {
      // Restore is best-effort; a failed write never breaks the chat.
    })
  }, [])

  const persistCookie = useCallback((cookie: string) => {
    void (async () => {
      try {
        const existing = await readConfigFile()
        await writeConfigFile({ ...existing, cookie })
      } catch {
        // Persistence is best-effort; the in-memory client already
        // carries the refreshed cookie for the rest of this session.
      }
    })()
  }, [])

  const applySession = useCallback(
    async (cookie: string) => {
      const nextConfig = { ...configRef.current, cookie, apiKey: undefined }
      configRef.current = nextConfig
      usingApiKeyRef.current = false
      persistCookie(cookie)

      const previous = hubRef.current
      if (previous) {
        void previous.stop()
        hubRef.current = null
      }

      const client = new ComukiClient(nextConfig, {
        onLatencySample: setLatencyMs,
        onSessionCookie: persistCookie,
      })
      clientRef.current = client
      client.setSessionCookie(cookie)

      const who = await whoAmI(client)
      setIdentity(who.label)
      setConnectError(null)

      try {
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
        if (connection) {
          hubRef.current = connection
          bindChatEvents(
            connection,
            (chunk) => {
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
          const sessionIds = sessionsRef.current
            .map((session) => session.id)
            .filter((id) => !id.startsWith(PENDING_PREFIX))
          void rejoinChatGroups(connection, sessionIds)
        }
      } catch {
        // REST stays authoritative; a dead hub is the existing fallback.
      }
    },
    [persistCookie]
  )

  // -- connect + restore -------------------------------------------------------

  useEffect(() => {
    let disposed = false
    let hub: HubConnection | null = null

    void (async () => {
      const client = new ComukiClient(config, {
        onLatencySample: setLatencyMs,
        onSessionCookie: persistCookie,
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
        // A failed start no longer gives up: the factory keeps retrying
        // on the same 0/2/5/10/30s ramp until start() succeeds (or 401).
        const bindHub = (connection: HubConnection) => {
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
        const rejoinOpenGroups = () => {
          const live = hubRef.current
          if (live) {
            const sessionIds = sessionsRef.current
              .map((session) => session.id)
              .filter((id) => !id.startsWith(PENDING_PREFIX))
            void rejoinChatGroups(live, sessionIds)
          }
        }
        const connection = await startChatHubConnection({
          hubUrl: client.hubUrl(),
          headers: client.hubHeaders(),
          onStateChange: setHubState,
          onReady: bindHub,
          onReconnected: rejoinOpenGroups,
          onAuthLost: () => {
            hubRef.current = null
            setHubState("offline")
          },
          onStopHandle: (stop) => {
            hubStopRef.current = stop
          },
        })
        if (connection && !disposed && hubRef.current !== connection) {
          bindHub(connection)
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
          setConnectError(error)
        }
      }

    })()

    return () => {
      disposed = true
      const stop = hubStopRef.current
      if (stop) {
        void stop()
        return
      }
      const connection = hub ?? hubRef.current
      if (connection) {
        void connection.stop()
      }
    }
  }, [config, persistCookie, project])

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
        const authNotice = describeAuthFailure(
          error,
          usingApiKeyRef.current
        )
        if (error instanceof ComukiApiError && error.status === 401) {
          setIdentity("signed out")
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(
            authNotice
              ? appendBlocks(current.sessions, sessionId, [
                  {
                    kind: "lines",
                    lines: [
                      "",
                      `${colors.error}${symbols.cross} ${authNotice}${colors.reset}`,
                    ],
                  },
                ])
              : current.sessions,
            sessionId,
            { hydrated: true }
          ),
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
        const authNotice = describeAuthFailure(
          error,
          usingApiKeyRef.current
        )
        if (authNotice) {
          if (
            error instanceof ComukiApiError &&
            error.status === 401
          ) {
            setIdentity("signed out")
            if (payload.message) {
              pendingRetryRef.current = payload.message
            }
          }
          setTabs((current) => ({
            ...current,
            sessions: patchSession(
              appendBlocks(current.sessions, sessionId, [
                {
                  kind: "lines",
                  lines: [
                    "",
                    `${colors.error}${symbols.cross} ${authNotice}${colors.reset}`,
                  ],
                },
              ]),
              sessionId,
              { status: "idle" }
            ),
          }))
          return
        }
        setTabs((current) => ({
          ...current,
          sessions: patchSession(
            appendBlocks(current.sessions, sessionId, [
              { kind: "lines", lines: ["", ...alertLines(error, columns)] },
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
    [columns, describeTurn]
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
      const echoBlock = userEchoBlock(typed)

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
                appendBlocks(next.sessions, session.id, [echoBlock]),
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
          if (
            error instanceof ComukiApiError &&
            error.status === 401
          ) {
            setIdentity("signed out")
            pendingRetryRef.current = expansion.outgoing
          }
          setNoticeLines(["", ...alertLines(error, columns)])
        }
        return
      }

      setTabs((current) => ({
        ...current,
        sessions: patchSession(
          appendBlocks(current.sessions, target.id, [echoBlock]),
          target.id,
          { lastUserMessage: typed }
        ),
      }))
      if (notices.length > 0) {
        pushLines(target.id, notices)
      }
      await runTurn(target.id, "message", { message: expansion.outgoing })
    },
    [columns, ensureDocTitles, expandTyped, pushLines, runTurn]
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

  // -- /profile ---------------------------------------------------------------

  /**
   * Lists the host catalog (`GET /profiles`) or well-known stems, and
   * stores a local preference in config.json. Honest: createSession
   * has no profile field, so the preference never rides a turn.
   */
  const switchProfile = useCallback(
    async (query: string) => {
      const client = clientRef.current
      const emit = (lines: readonly string[]) => {
        const target = tabs.sessions[tabs.activeIndex]
        if (target) {
          pushLines(target.id, lines)
        } else {
          setNoticeLines(lines)
        }
      }
      let fromHost = false
      let catalog: Awaited<ReturnType<ComukiClient["profiles"]>> = []
      if (client) {
        try {
          catalog = await client.profiles()
          fromHost = true
        } catch {
          fromHost = false
        }
      }
      if (query.length === 0) {
        emit(
          profileListingLines(preferredProfile ?? null, catalog, { fromHost })
        )
        return
      }
      const match = resolveProfile(query, catalog)
      if (!match) {
        emit([
          `${colors.faint}  unknown profile '${query}'${colors.reset}`,
          PROFILE_USAGE,
          ...profileListingLines(preferredProfile ?? null, catalog, {
            fromHost,
          }),
        ])
        return
      }
      setPreferredProfile(match)
      try {
        const contents = await readConfigFile()
        await writeConfigFile({ ...contents, preferredProfile: match })
      } catch {
        // Best-effort persistence; this session's preference already applies.
      }
      emit([profileStoredLine(match)])
    },
    [preferredProfile, pushLines, tabs]
  )

  // -- session power pack: /branch -------------------------------------------

  /**
   * Forks the active session: creates a NEW server session titled
   * `fork of <source>` in the same project and immediately sends the
   * opener (the explicit `/branch <message>` text or the source's last
   * user message — retry-in-new-tab). The server transcript is NOT
   * copied: server sessions start empty and full fork semantics need
   * server support; the opener is the pragmatic v1.
   */
  const forkSession = useCallback(
    async (source: Session | undefined, opener: string) => {
      const client = clientRef.current
      if (!client) {
        return
      }
      setWelcomeDismissed(true)
      const title = source
        ? forkTitle(source.name)
        : sessionNameFromMessage(opener)
      try {
        const session = await client.createSession({
          projectId: projectIdRef.current,
          title,
        })
        const hub = hubRef.current
        if (hub) {
          await joinChatGroup(hub, session.id)
        }
        setTabs((current) => {
          const next = addSession(current, {
            ...newPendingSession(),
            id: session.id,
            name: title,
            hydrated: true,
            // The fork's first message seeds its recall history.
            history: [opener],
          })
          return {
            ...next,
            sessions: patchSession(
              appendBlocks(next.sessions, session.id, [userEchoBlock(opener)]),
              session.id,
              { lastUserMessage: opener }
            ),
          }
        })
        await runTurn(session.id, "message", { message: opener })
      } catch (error) {
        setNoticeLines(["", ...alertLines(error, columns)])
      }
    },
    [columns, runTurn]
  )

  // -- ops pack: /kb -------------------------------------------------------------

  /**
   * The knowledge library. `/kb list` renders one page of the document
   * index; `/kb add <file|glob>` collects local text files, validates
   * them (extension + size) and POSTs one ingest per file — the server
   * chunks + embeds synchronously, so each file resolves to its own
   * `* name → id` line. A 401/403 mid-run stops the remaining files
   * with the honest notice: this subject lacks `knowledge:write`.
   */
  const runKb = useCallback(
    (subcommand: string, rest: string) => {
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

      if (subcommand === "list") {
        client
          .knowledgeDocuments(1, KB_PAGE_SIZE)
          .then((page) => {
            emit(kbListLines(page))
          })
          .catch((error: unknown) => {
            emit([
              kbAddErrorLine("kb list", describeError(error)),
            ])
          })
        return
      }

      if (subcommand !== "add") {
        emit(kbUsageLines())
        return
      }

      if (rest.length === 0) {
        emit(kbUsageLines())
        return
      }

      const files = collectFiles(rest.split(/\s+/))
      if (files.length === 0) {
        emit([
          `${colors.faint}  no files match '${rest}'${colors.reset}`,
        ])
        return
      }
      const capped = files.slice(0, KB_MAX_FILES)
      if (files.length > capped.length) {
        emit([
          `${colors.faint}  pattern matched ${files.length} files — /kb add takes the first ${KB_MAX_FILES}${colors.reset}`,
        ])
      }

      void (async () => {
        for (const path of capped) {
          const file = Bun.file(path)
          const check = validateIngestFile(path, file.size)
          if (!check.ok) {
            emit([kbAddErrorLine(path, check.reason)])
            continue
          }
          try {
            const result = await client.knowledgeIngest(
              ingestRequestFor(path, await file.text(), projectIdRef.current)
            )
            emit([kbAddResultLine(path, result)])
          } catch (error) {
            if (
              error instanceof ComukiApiError &&
              (error.status === 401 || error.status === 403)
            ) {
              // The subject cannot write knowledge at all — the
              // remaining files would fail identically.
              emit(kbWriteUnavailableLines(error.status))
              return
            }
            emit([kbAddErrorLine(path, describeError(error))])
          }
        }
      })()
    },
    [pushLines, tabs]
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
      void fetchRunsFeedPanel(client, config.url).then((panel) => {
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

  const announce = useCallback(
    (line: string) => {
      const target =
        tabs.activeIndex >= 0 ? tabs.sessions[tabs.activeIndex] : undefined
      if (target) {
        pushLines(target.id, [line])
      } else {
        setNoticeLines([line])
      }
    },
    [pushLines, tabs]
  )

  const submitLogin = useCallback(
    (raw: string) => {
      const value = raw.trim()
      if (loginStep === "email") {
        if (value.length === 0) {
          return
        }
        loginEmailRef.current = value
        setLoginStep("password")
        return
      }
      if (loginStep !== "password") {
        return
      }
      if (value.length === 0) {
        return
      }
      const email = loginEmailRef.current
      setLoginStep(null)
      void (async () => {
        try {
          const success = await loginAndStore(
            configRef.current.url,
            email,
            value
          )
          await applySession(success.cookie)
          announce(
            `${colors.ok}${symbols.checkmark} signed in as ${success.displayName} (${success.email})${colors.reset}`
          )
          const retry = pendingRetryRef.current
          pendingRetryRef.current = null
          if (retry) {
            const target =
              tabs.activeIndex >= 0
                ? tabs.sessions[tabs.activeIndex]
                : undefined
            await sendMessage(target, retry)
          }
        } catch (error) {
          announce(
            `${colors.error}${symbols.cross} ${describeError(error)}${colors.reset}`
          )
        }
      })()
    },
    [announce, applySession, loginStep, sendMessage, tabs]
  )

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
          setInspector({ title: "help / commands", lines })
          return
        }
        case "login": {
          loginEmailRef.current = ""
          pendingRetryRef.current = null
          setLoginStep("email")
          const line = `${colors.faint}  sign in — email, then password${colors.reset}`
          if (target) {
            pushLines(target.id, [line])
          } else {
            setNoticeLines([line])
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
        case "login": {
          const lines = [
            `${colors.faint}  leave the repl and run:  comuki login${colors.reset}`,
          ]
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
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
        case "edit": {
          const last = retryMessage(target)
          if (!last) {
            if (target) {
              pushLines(target.id, [NOTHING_TO_EDIT])
            } else {
              setNoticeLines([NOTHING_TO_EDIT])
            }
            return
          }
          setPromptSeed((current) => ({
            value: last,
            seq: (current?.seq ?? 0) + 1,
          }))
          return
        }
        case "copycode": {
          copyLastCode()
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
        case "archive": {
          if (!target) {
            setNoticeLines([
              `${colors.faint}  no active session to archive${colors.reset}`,
            ])
            return
          }
          const markdown = exportMarkdown(target.blocks)
          if (markdown.length === 0) {
            pushLines(target.id, [
              `${colors.faint}  nothing to archive — the transcript is empty${colors.reset}`,
            ])
            return
          }
          const path = archiveFilePath(target.id, target.name)
          const closingId = target.id
          void (async () => {
            try {
              await Bun.write(path, markdown, { createPath: true })
              const index = sessionsRef.current.findIndex(
                (session) => session.id === closingId
              )
              if (index >= 0) {
                closeSession(index)
              }
              setNoticeLines([
                `${colors.faint}  archived ${path}${colors.reset}`,
              ])
            } catch (error) {
              pushLines(closingId, [
                `${colors.error}${symbols.cross} archive failed: ${describeError(error)}${colors.reset}`,
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
          void fetchRunsFeedPanel(client, config.url).then((panel) => {
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
            }
            setInspector({ title: "runs", lines: renderRunsFeedPanel(panel) })
          })
          return
        }
        case "workers": {
          const client = clientRef.current
          if (!client) {
            return
          }
          // One shot per invocation — no polling loop, no pinned panel.
          void client
            .backgroundWorkers()
            .then((workers) => {
              const lines = renderWorkersPanel(workers, config.url)
              setInspector({ title: "workers", lines })
            })
            .catch((error: unknown) => {
              const line = `${colors.error}${symbols.cross} workers not available — ${describeError(error)}${colors.reset}`
              setInspector({ title: "workers", lines: [line] })
            })
          return
        }
        case "plan": {
          const lines = planPanelLines(
            target?.blocks ?? [],
            target?.pendingPlan ?? null
          )
          setInspector({ title: "plan", lines })
          return
        }
        case "project": {
          void switchProject(action.query)
          return
        }
        case "snip": {
          void (async () => {
            const lines = snippetListingLines(await readSnippetsFile())
            if (target) {
              pushLines(target.id, lines)
            } else {
              setNoticeLines(lines)
            }
          })()
          return
        }
        case "snip-send": {
          void (async () => {
            const store = await readSnippetsFile()
            const text = getSnippet(store, action.name)
            if (text === undefined) {
              const line = `${colors.faint}  no snippet '${action.name}' — /snip lists what exists${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
              return
            }
            // A thinking turn owns the wire — the snippet queues like a
            // typed message (the pre-switch gate only sees raw text).
            if (target?.status === "thinking") {
              setTabs((current) => ({
                ...current,
                sessions: patchSession(current.sessions, target.id, {
                  queued: enqueueMessage(target.queued ?? [], text),
                }),
              }))
              pushLines(target.id, [queuedNoticeLine()])
              return
            }
            // The editor has no prefill seam, so a snippet goes out as
            // the next message — one keystroke, same echo as typing it.
            await sendMessage(target, text)
          })()
          return
        }
        case "snip-save": {
          if (!isValidSnippetName(action.name)) {
            const line = SNIP_USAGE
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          // The LAST sent message is the save source — same getter
          // `/retry` uses, mention preamble already stripped.
          const last = retryMessage(target)
          if (!last) {
            const line = NOTHING_TO_SAVE
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          const name = action.name
          void (async () => {
            try {
              const store = await readSnippetsFile()
              await writeSnippetsFile(saveSnippet(store, name, last))
              const line = `${colors.ok}${symbols.checkmark} saved snippet ${name}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            } catch (error) {
              const line = `${colors.error}${symbols.cross} snippet save failed: ${describeError(error)}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            }
          })()
          return
        }
        case "snip-rm": {
          if (!isValidSnippetName(action.name)) {
            const line = SNIP_USAGE
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          const name = action.name
          void (async () => {
            try {
              const store = await readSnippetsFile()
              if (getSnippet(store, name) === undefined) {
                const line = `${colors.faint}  no snippet '${name}'${colors.reset}`
                if (target) {
                  pushLines(target.id, [line])
                } else {
                  setNoticeLines([line])
                }
                return
              }
              await writeSnippetsFile(removeSnippet(store, name))
              const line = `${colors.ok}${symbols.checkmark} removed snippet ${name}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            } catch (error) {
              const line = `${colors.error}${symbols.cross} snippet remove failed: ${describeError(error)}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            }
          })()
          return
        }
        case "branch": {
          const opener = action.message ?? branchOpener(target)
          if (opener === null || opener.length === 0) {
            const line = NOTHING_TO_BRANCH
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          void forkSession(target, opener)
          return
        }
        case "kb": {
          runKb(action.subcommand, action.rest)
          return
        }
        case "theme": {
          const current = currentThemeId()
          if (action.name.length === 0) {
            const lines = themeListingLines(current)
            if (target) {
              pushLines(target.id, lines)
            } else {
              setNoticeLines(lines)
            }
            return
          }
          if (!isThemeChoice(action.name)) {
            const lines = themeUnknownLines(action.name, current)
            if (target) {
              pushLines(target.id, lines)
            } else {
              setNoticeLines(lines)
            }
            return
          }
          resolveTheme(action.name)
          void (async () => {
            try {
              const contents = await readConfigFile()
              await writeConfigFile({ ...contents, theme: action.name })
            } catch {
              // Best-effort persistence; the live palette already switched.
            }
          })()
          const line = themeSwitchedLine(action.name)
          if (target) {
            pushLines(target.id, [line])
          } else {
            setNoticeLines([line])
          }
          return
        }
        case "whoami": {
          const client = clientRef.current
          if (!client) {
            return
          }
          void (async () => {
            try {
              const me = await client.me()
              const lines = formatWhoamiLines(whoFromMe(me), me)
              if (target) {
                pushLines(target.id, lines)
              } else {
                setNoticeLines(lines)
              }
            } catch (error) {
              const lines = formatWhoamiLines(whoFromError(error))
              if (target) {
                pushLines(target.id, lines)
              } else {
                setNoticeLines(lines)
              }
            }
          })()
          return
        }
        case "keys": {
          const lines = keybindingsListingLines(keybindingsRef.current)
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
          return
        }
        case "status": {
          const client = clientRef.current
          if (!client) {
            return
          }
          const totals = sessionTokenTotals(target?.blocks ?? [])
          const localLines = [
            `  identity: ${identity}`,
            `  transport: ${hubState}`,
            `  host: ${hostFromUrl(config.url) ?? "unavailable"}`,
            `  latency: ${latencyMs === null ? "unknown" : `${latencyMs}ms`}`,
            `  project: ${projectLabel ?? "none"}`,
            `  profile: ${preferredProfile ?? "default"}`,
            `  context: ${
              totals === null
                ? "unknown"
                : `${totals.tokensIn + totals.tokensOut} tokens`
            }`,
          ]
          setInspector({ title: "platform status", lines: localLines })
          void fetchStatusSnapshot(client).then((snapshot) => {
            setInspector({
              title: "platform status",
              lines: [...localLines, "", ...renderStatusPanel(snapshot)],
            })
          })
          return
        }
        case "open": {
          const url = dashboardOpenUrl(config.url, target?.id)
          const opened = openDashboardUrl(url)
          const reason = url.endsWith(DASHBOARD_CHAT_PATH) ? "chat" : "runs"
          const lines = openPanelLines(url, opened, reason)
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
          return
        }
        case "tools": {
          const lines = toolsUnavailableLines()
          setInspector({ title: "tools", lines })
          return
        }
        case "note": {
          const lines =
            action.text.length === 0
              ? noteUsageLines()
              : noteUnavailableLines()
          if (target) {
            pushLines(target.id, lines)
          } else {
            setNoticeLines(lines)
          }
          return
        }
        case "profile": {
          void switchProfile(action.name)
          return
        }
        case "alias": {
          void (async () => {
            const lines = aliasListingLines(await readAliasesFile())
            if (target) {
              pushLines(target.id, lines)
            } else {
              setNoticeLines(lines)
            }
          })()
          return
        }
        case "alias-set": {
          if (!isValidAliasName(action.name) || action.text.length === 0) {
            const line = ALIAS_USAGE
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          const name = action.name
          const text = action.text
          void (async () => {
            try {
              const store = await readAliasesFile()
              await writeAliasesFile(setAlias(store, name, text))
              const line = `${colors.ok}${symbols.checkmark} alias ${name}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            } catch (error) {
              const line = `${colors.error}${symbols.cross} alias save failed: ${describeError(error)}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            }
          })()
          return
        }
        case "alias-rm": {
          if (!isValidAliasName(action.name)) {
            const line = ALIAS_USAGE
            if (target) {
              pushLines(target.id, [line])
            } else {
              setNoticeLines([line])
            }
            return
          }
          const name = action.name
          void (async () => {
            try {
              const store = await readAliasesFile()
              if (getAlias(store, name) === undefined) {
                const line = `${colors.faint}  no alias '${name}'${colors.reset}`
                if (target) {
                  pushLines(target.id, [line])
                } else {
                  setNoticeLines([line])
                }
                return
              }
              await writeAliasesFile(removeAlias(store, name))
              const line = `${colors.ok}${symbols.checkmark} removed alias ${name}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            } catch (error) {
              const line = `${colors.error}${symbols.cross} alias remove failed: ${describeError(error)}${colors.reset}`
              if (target) {
                pushLines(target.id, [line])
              } else {
                setNoticeLines([line])
              }
            }
          })()
          return
        }
        case "message": {
          void (async () => {
            const expanded = expandAlias(await readAliasesFile(), value)
            await sendMessage(target, expanded ?? value)
          })()
          return
        }
      }
    },
    [
      bellEnabled,
      closeSession,
      config.url,
      copyLastCode,
      exit,
      forkSession,
      hubState,
      identity,
      latencyMs,
      openPendingTab,
      preferredProfile,
      projectLabel,
      pushLines,
      runKb,
      runTurn,
      sendMessage,
      stopTurn,
      switchProfile,
      switchProject,
      tabs,
    ]
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
        void (async () => {
          const expanded = expandAlias(await readAliasesFile(), message)
          await sendMessage(session, expanded ?? message)
        })()
        return
      }
    }
  }, [tabs.sessions, sendMessage])

  // SGR mouse: a click on the plan card's `approve` / `reject` line
  // submits the matching slash. Terminals without mouse tracking ignore
  // the DECSET bytes and never fire — the keyboard path is untouched.
  const mouseLayoutRef = useRef<MouseLayout>({
    tabRow: null,
    sessions: [],
    transcriptTop: 2,
    hasHint: false,
    visibleLines: [],
    awaitingApproval: false,
  })
  const indicatorRow = scroll.offset > 0 && scroll.newBelow
  mouseLayoutRef.current = {
    tabRow: null,
    sessions: tabs.sessions,
    transcriptTop: harness.topBarRows + 1,
    hasHint: expandHint !== null,
    visibleLines: viewportSlice(
      transcriptLines,
      viewportHeight -
        (expandHint !== null ? 1 : 0) -
        (indicatorRow ? 1 : 0),
      scroll.offset
    ),
    awaitingApproval: activeSession?.awaitingApproval === true,
  }
  const handleMouseClick = useCallback(
    (click: { readonly x: number; readonly y: number }) => {
      if (
        overviewRef.current ||
        inspectorRef.current ||
        paletteVisible ||
        searchOpen
      ) {
        return
      }
      const localClick = {
        x: click.x,
        y: click.y,
      }
      const target = resolveMouseClick(localClick, mouseLayoutRef.current)
      if (target.kind === "tab") {
        focusSession(target.index)
        return
      }
      if (target.kind === "approve") {
        handleSubmit("/approve")
        return
      }
      if (target.kind === "reject") {
        handleSubmit("/reject")
      }
    },
    [
      paletteVisible,
      searchOpen,
      focusSession,
      handleSubmit,
    ]
  )
  useMouse(handleMouseClick)

  // Global hotkeys — active in every state; the editor ignores these keys.
  useInput((input, key) => {
    if (isSgrMouseChunk(input)) {
      return
    }
    if (overviewRef.current) {
      return // the overview's own handler owns the keys
    }
    if (paletteVisible) {
      return // the palette owns filtering, selection and dismissal
    }
    if (inspectorRef.current) {
      if (key.escape) {
        setInspector(null)
      }
      return
    }
    // While the ctrl+f search row is open it owns the keyboard: its
    // editor eats the query keystrokes and enter/esc drive the search.
    if (searchOpen || loginStep !== null) {
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
    // j/k/g/G are the vim twins — same gate, so they never steal keys
    // while the prompt is at the bottom (typing).
    if (key.upArrow && scroll.scrolledUp) {
      scroll.lineUp()
      return
    }
    if (key.downArrow && scroll.scrolledUp) {
      scroll.lineDown()
      return
    }
    if (scroll.scrolledUp && !key.ctrl && !key.meta) {
      if (input === "j") {
        scroll.lineDown()
        return
      }
      if (input === "k") {
        scroll.lineUp()
        return
      }
      if (input === "g") {
        scroll.toTop()
        return
      }
      if (input === "G") {
        scroll.toBottom()
        return
      }
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
    const bindings = keybindingsRef.current
    if (key.ctrl && input.toLowerCase() === "p") {
      setPaletteVisible(true)
      return
    }
    if (matchesBinding(bindings.overview, input, key)) {
      setOverviewVisible((current) => !current)
      return
    }
    if (matchesBinding(bindings.new, input, key)) {
      openPendingTab()
      return
    }
    if (matchesBinding(bindings.search, input, key)) {
      // Transcript search — the inline row above the composer owns the
      // keyboard from here until esc closes it.
      setSearchOpen(true)
      return
    }
    if (matchesBinding(bindings.verbose, input, key)) {
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
    if (matchesBinding(bindings.close, input, key)) {
      closeSession(tabs.activeIndex)
    }
  })

  useEffect(() => {
    if (connectError !== null && isUnrecoverableError(connectError)) {
      process.exitCode = 1
    }
  }, [connectError])

  const connectAlert: AlertCardModel | null =
    connectError === null ? null : alertFromError(connectError)

  const showWelcome = !welcomeDismissed && tabs.sessions.length === 0
  // The prompt stays live while a turn thinks: typing a message queues
  // it, `/stop` needs to be submittable mid-turn.
  const promptEnabled =
    !overviewVisible && !paletteVisible && inspector === null
  const attentionCount = tabs.sessions.filter(
    (session) => session.unread || session.awaitingApproval
  ).length
  const activity = activeSession?.awaitingApproval
    ? "approval needed"
    : activeSession?.status === "thinking"
      ? "thinking"
      : activeSession?.status === "running"
        ? "workers active"
        : undefined
  const workbenchLines = activeSession?.awaitingApproval
    ? planPanelLines(activeSession.blocks, activeSession.pendingPlan)
    : activeSession?.runsFeed != null
      ? renderRunsFeedPanel(activeSession.runsFeed)
      : activeSession?.pendingPlan != null
        ? planPanelLines(activeSession.blocks, activeSession.pendingPlan)
        : activeSession?.status === "running"
          ? ["active workers are processing this session"]
          : []
  const composerMetadata = [
    projectLabel ? `project ${projectLabel}` : undefined,
    preferredProfile ? `profile ${preferredProfile}` : undefined,
    promptBusy ? "enter queues / /stop interrupts" : undefined,
  ]
    .filter((value): value is string => value !== undefined)
    .join(" / ")

  // Conversation and composer are permanent; all navigation is transient.
  return (
    <Fill width={columns} height={rows} color={palette.floor}>
      <Box flexDirection="column" width={columns} height={rows}>
        <TopBar
          width={columns}
          mode={harness.mode}
          session={activeSession?.name}
          activity={activity}
          attentionCount={attentionCount}
        />
        <Box
          flexDirection="row"
          width={columns}
          height={rows - harness.topBarRows}
        >
          <Box
            flexDirection="column"
            width={contentWidth}
            height={rows - harness.topBarRows}
          >
            <Fill width={contentWidth} height={viewportHeight} color={palette.floor}>
              <Box
                flexDirection="column"
                width={contentWidth}
                height={viewportHeight}
                overflow="hidden"
              >
        {paletteVisible ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <CommandPalette
              width={Math.max(32, contentWidth - 4)}
              onSelect={(command) => {
                setPaletteVisible(false)
                handleSubmit(command)
              }}
              onClose={() => setPaletteVisible(false)}
            />
          </Box>
        ) : overviewVisible ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <SessionOverview
              sessions={tabs.sessions}
              activeIndex={tabs.activeIndex}
              width={Math.min(68, Math.max(24, contentWidth - 2))}
              viewportHeight={viewportHeight}
              terminalTop={harness.topBarRows + 1}
              onSelect={selectSession}
              onNewSession={() => {
                openPendingTab()
                setOverviewVisible(false)
              }}
              onClose={() => setOverviewVisible(false)}
            />
          </Box>
        ) : inspector !== null ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <LineInspector
              title={inspector.title}
              lines={inspector.lines}
              width={Math.min(76, Math.max(24, contentWidth - 2))}
              height={Math.min(viewportHeight, 22)}
            />
          </Box>
        ) : showWelcome ? (
          <Box
            flexDirection="column"
            alignItems="center"
            justifyContent="center"
            flexGrow={1}
          >
            <Welcome />
            {connectAlert ? (
              <Box marginTop={1} flexDirection="column">
                <AlertCard {...connectAlert} width={contentWidth} />
              </Box>
            ) : null}
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
            width={contentWidth}
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
            </Fill>
      {searchOpen ? (
        <Fill width={contentWidth} height={1} color={palette.rail}>
          <TranscriptSearch
            value={searchQuery}
            matchCount={searchMatches.length}
            matchIndex={searchCursor}
            onChange={handleSearchChange}
            onNext={() => cycleSearch(1)}
            onPrevious={() => cycleSearch(-1)}
            onClose={handleSearchClose}
          />
        </Fill>
      ) : null}
      {!overviewVisible && !paletteVisible && inspector === null ? (
        <>
          {connectAlert && !showWelcome ? (
            <AlertCard {...connectAlert} width={contentWidth} />
          ) : null}
          {copyHint ? (
            <Text dimColor>{`  ${copyHint}`}</Text>
          ) : null}
          {queuedCount > 0 ? <Text>{queueHintLine(queuedCount)}</Text> : null}
          {mentionMenu.element}
          <Fill width={contentWidth} height={promptRows} color={palette.floor}>
            {loginStep === null ? (
              <PromptInput
                onSubmit={handleSubmit}
                history={activeSession?.history ?? []}
                active={promptEnabled && !searchOpen}
                historyRecallEnabled={!scroll.scrolledUp}
                label={composerMetadata}
                onMenuOpenChange={handleMenuOpenChange}
                onRowsChange={handlePromptRows}
                seed={promptSeed}
                {...mentionMenu.promptBindings}
              />
            ) : (
              <PromptInput
                key={loginStep}
                onSubmit={submitLogin}
                history={[]}
                active={promptEnabled && !searchOpen}
                historyRecallEnabled={false}
                slashMenuEnabled={false}
                mask={loginStep === "password" ? "*" : undefined}
                placeholder={
                  loginStep === "email" ? "email..." : "password..."
                }
                onMenuOpenChange={handleMenuOpenChange}
                onRowsChange={handlePromptRows}
              />
            )}
          </Fill>
        </>
      ) : null}
          </Box>
          {harness.dividerWidth > 0 ? (
            <Fill
              width={harness.dividerWidth}
              height={rows - harness.topBarRows}
              color={palette.ruleStrong}
            />
          ) : null}
          {harness.workbenchWidth > 0 ? (
            <ContextWorkbench
              title={activeSession?.awaitingApproval ? "approval" : "workbench"}
              lines={workbenchLines}
              width={harness.workbenchWidth}
              height={rows - harness.topBarRows}
            />
          ) : null}
        </Box>
      </Box>
    </Fill>
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

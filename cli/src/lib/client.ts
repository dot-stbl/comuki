/**
 * Typed REST client for the Comuki host API.
 *
 * Wire shapes mirror the C# read models byte-for-byte (camelCase JSON):
 * `ChatSessionView`, `ChatMessageView` + `MessagePart` polymorphism, run /
 * knowledge / compute / project views. The C# records in
 * `platform/src/host/Comuki.Host` are the source of truth — renaming either
 * side is a wire break.
 *
 * Auth: API key rides as `Authorization: Bearer ck_…` (plus
 * `X-Comuki-Tenant` for tenant-scoped keys); a `comuki login` cookie rides
 * as a manual `Cookie` header. `fetchImpl` is injectable so tests mock the
 * transport, never the endpoints.
 */
import type { ResolvedConfig } from "./config"

export class ComukiApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    readonly detail: string | undefined
  ) {
    super(
      `HTTP ${status}${code ? ` (${code})` : ""}: ${detail ?? "request failed"}`
    )
    this.name = "ComukiApiError"
  }
}

/**
 * True for the DOMException fetch rejects with when an AbortController
 * fires — how /stop recognises its own abort and skips the error line.
 */
export function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === "AbortError"
}

// ---------------------------------------------------------------------------
// Wire view types (camelCase, as the host serialises them)
// ---------------------------------------------------------------------------

export interface MeView {
  readonly userId: string | null
  readonly subjectType: string
  readonly subjectId: string
  readonly email: string | null
  readonly displayName: string | null
  readonly roles: readonly string[]
  readonly permissions: readonly string[]
}

/** Wire of `GET /api/v1/health` — anonymous liveness `{ status: "ok" }`. */
export interface HealthView {
  readonly status: string
}

export interface ChatSessionView {
  readonly id: string
  readonly projectId: string | null
  readonly title: string
  readonly status: string
  readonly createdAt: string
  readonly updatedAt: string
}

export type MessagePart =
  | { readonly kind: "text"; readonly markdown: string }
  | {
      readonly kind: "code"
      readonly language: string
      readonly source: string
      readonly path?: string | null
      readonly startLine?: number | null
    }
  | {
      readonly kind: "diagram"
      readonly dialect: string
      readonly source: string
    }
  | {
      readonly kind: "thinking"
      readonly text: string
      readonly tokens?: number | null
      /** Optional on the wire today — the collapsed line shows it when present. */
      readonly durationMs?: number | null
    }
  | {
      readonly kind: "tool"
      readonly name: string
      readonly inputJson: string
      readonly status: string
      readonly outputJson?: string | null
      readonly durationMs?: number | null
    }
  | { readonly kind: "handoff"; readonly query: string }
  | {
      readonly kind: "plan"
      readonly nodes: readonly PlanItemView[]
      readonly edges: readonly PlanEdgeView[]
    }

export interface PlanItemView {
  readonly key: string
  readonly profileKey: string
  readonly brief: string
  readonly dependsOn: readonly string[]
}

export interface PlanEdgeView {
  readonly from: string
  readonly to: string
}

export interface ChatMessageMetaView {
  readonly model?: string | null
  readonly tokensIn?: number | null
  readonly tokensOut?: number | null
  readonly costMicros?: number | null
  readonly latencyMs?: number | null
  readonly stopReason?: string | null
}

export interface ChatMessageView {
  readonly id: string
  readonly role: string
  readonly content: string
  readonly toolName: string | null
  readonly parts: readonly MessagePart[] | null
  readonly meta: ChatMessageMetaView | null
  readonly createdAt: string
}

export interface ChatTurnResultView {
  readonly messages: readonly ChatMessageView[]
  readonly awaitingApproval: boolean
  readonly pendingPlan: unknown
}

export interface ChatMessagesPageView {
  readonly items: readonly ChatMessageView[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

export interface RunView {
  readonly id: string
  readonly projectId: string
  readonly status: string
  readonly createdAt: string
  readonly updatedAt: string
}

export interface RunsPageView {
  readonly items: readonly RunView[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

/** Outcome of one background-worker cycle (`WorkerResult` on the wire). */
export interface WorkerResultView {
  readonly success: boolean
  readonly detail: string | null
  readonly data: unknown
}

/**
 * Wire row of `GET /api/v1/workers/background` — one host background
 * loop (memory-sweep, lease-reaper, …). Null timestamps mean "has not
 * happened yet"; `nextRunAt` is null while a cycle is in flight or
 * the worker has finished (startup workers run exactly once).
 */
export interface BackgroundWorkerView {
  readonly name: string
  readonly lastRunAt: string | null
  readonly nextRunAt: string | null
  readonly lastResult: WorkerResultView | null
  readonly consecutiveFailures: number
  readonly isHealthy: boolean
}

export interface KnowledgeDocumentSummaryView {
  readonly id: string
  readonly projectId: string | null
  readonly title: string
  readonly source: string
  readonly sourceRef: string
  readonly mimeType: string
  readonly chunkCount: number
  readonly tokenCount: number
  readonly createdAt: string
}

export interface KnowledgeDocumentsPageView {
  readonly items: readonly KnowledgeDocumentSummaryView[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

/** Wire row of `POST /api/v1/knowledge/ingest` — the new source id + chunk count. */
export interface KnowledgeIngestResultView {
  readonly sourceDocumentId: string
  readonly chunksWritten: number
}

/** Wire row of `GET /api/v1/knowledge/search` — chunk hit, best first. */
export interface KnowledgeSearchHitView {
  readonly documentId: string
  readonly chunkId: string
  readonly snippet: string
  readonly score: number
}

export interface ComputePoolView {
  readonly projectId: string
  readonly profileKey: string
  readonly queued: number
  readonly running: number
  readonly minIdle: number
  readonly maxConcurrent: number
}

export interface ComputeSnapshotView {
  readonly provider: string
  readonly defaults: {
    readonly workerImage: string
    readonly minIdle: number
    readonly maxConcurrent: number
  }
  readonly pools: readonly ComputePoolView[]
}

export interface ProjectView {
  readonly id: string
  readonly name: string
  readonly slug: string
  readonly description: string | null
  readonly archived: boolean
}

/**
 * Catalog-facing worker profile (`GET /profiles`). Matches
 * `ProfileDefinition` on the wire — key/name/description; tools and
 * model are optional extras the listing does not need.
 */
export interface ProfileView {
  readonly key: string
  readonly name: string
  readonly description: string
  readonly allowedTools?: readonly string[]
  readonly model?: string | null
}

export interface LoginSuccess {
  readonly cookie: string
  readonly userId: string
  readonly email: string
  readonly displayName: string
}

type FetchLike = (
  input: string,
  init?: RequestInit & { headers?: Record<string, string> }
) => Promise<Response>

export interface ClientOptions {
  /** Test seam — defaults to the global fetch. */
  fetchImpl?: FetchLike
  signal?: AbortSignal
  /**
   * Receives the chat-latency EMA after every successful `postMessage`
   * round-trip — the status bar's latency badge.
   */
  readonly onLatencySample?: (latencyMs: number) => void
  /**
   * Sliding cookie refresh: every successful response that carries a
   * session `Set-Cookie` (`comuki.auth` / `.Comuki.Session`) fires this
   * so the caller can persist it. Login itself still returns the cookie
   * on the `LoginSuccess` — this is for mid-session refresh.
   */
  readonly onSessionCookie?: (cookie: string) => void
}

/** Smoothing factor for the chat-latency EMA (≈ the last 3 sends dominate). */
const LATENCY_EMA_ALPHA = 0.3

/** Host session cookie names — ASP.NET cookie auth + the older Session names. */
const SESSION_COOKIE_NAMES = new Set([
  "comuki.auth",
  ".Comuki.Session",
  "Comuki.Session",
])

/**
 * One EMA step over chat-send round-trips. The first sample seeds the
 * EMA; negative samples clamp to zero (clock jitter must not travel
 * backwards). Pure — unit-tested directly.
 */
export function nextLatencyEma(
  previousMs: number | null,
  sampleMs: number
): number {
  const sample = Math.max(0, Math.round(sampleMs))
  if (previousMs === null) {
    return sample
  }
  return Math.round(previousMs + LATENCY_EMA_ALPHA * (sample - previousMs))
}

export class ComukiClient {
  private readonly fetchImpl: FetchLike
  private readonly signal?: AbortSignal
  private readonly baseUrl: string
  private readonly headers: Record<string, string>
  /**
   * Same object SignalR keeps after connect. Mutated in place by
   * `setSessionCookie` so a sliding refresh is visible on reconnect
   * without rebuilding the bag.
   */
  private readonly hubHeaderBag: Record<string, string> = {}
  private readonly onLatencySample?: (latencyMs: number) => void
  private readonly onSessionCookie?: (cookie: string) => void
  private chatLatencyEmaMs: number | null = null

  constructor(config: ResolvedConfig, options: ClientOptions = {}) {
    this.fetchImpl = options.fetchImpl ?? (fetch as FetchLike)
    this.signal = options.signal
    this.onLatencySample = options.onLatencySample
    this.onSessionCookie = options.onSessionCookie
    this.baseUrl = config.url.replace(/\/+$/, "")
    this.headers = {
      Accept: "application/json",
      ...(config.apiKey ? { Authorization: `Bearer ${config.apiKey}` } : {}),
      ...(config.tenant ? { "X-Comuki-Tenant": config.tenant } : {}),
      ...(config.cookie ? { Cookie: config.cookie } : {}),
    }
    this.syncHubHeaders()
  }

  /** EMA of successful chat POST round-trips; null before the first send. */
  chatLatencyMs(): number | null {
    return this.chatLatencyEmaMs
  }

  /** Same-origin SignalR hub URL (`<url>/ws/runs`). */
  readonly hubUrl = () => this.baseUrl + "/ws/runs"

  /** Headers the hub connection must send (API key / cookie auth). */
  readonly hubHeaders = (): Record<string, string> => this.hubHeaderBag

  /**
   * Replace the session cookie on this instance. The next REST call and
   * the live hub-header bag both see the new value.
   */
  setSessionCookie(value: string): void {
    this.headers.Cookie = value
    this.hubHeaderBag.Cookie = value
  }

  private syncHubHeaders(): void {
    if (this.headers.Authorization) {
      this.hubHeaderBag.Authorization = this.headers.Authorization
    }
    if (this.headers.Cookie) {
      this.hubHeaderBag.Cookie = this.headers.Cookie
    }
    if (this.headers["X-Comuki-Tenant"]) {
      this.hubHeaderBag["X-Comuki-Tenant"] = this.headers["X-Comuki-Tenant"]
    }
  }

  private async request<T>(
    method: string,
    path: string,
    body?: unknown,
    signal?: AbortSignal
  ): Promise<T> {
    const response = await this.fetchImpl(this.baseUrl + path, {
      method,
      signal: signal ?? this.signal,
      headers: {
        ...this.headers,
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    })

    if (response.ok) {
      this.captureSessionCookie(response)
    }

    if (!response.ok) {
      throw await ComukiClient.toApiError(response)
    }
    if (response.status === 204) {
      return undefined as T
    }
    return (await response.json()) as T
  }

  private captureSessionCookie(response: Response): void {
    const setCookie = response.headers.getSetCookie?.() ?? []
    const cookie = ComukiClient.pickSessionCookie(setCookie)
    if (!cookie) {
      return
    }
    this.setSessionCookie(cookie)
    this.onSessionCookie?.(cookie)
  }

  private static async toApiError(response: Response): Promise<ComukiApiError> {
    let code: string | undefined
    let detail: string | undefined
    try {
      const problem = (await response.json()) as {
        code?: string
        detail?: string
        title?: string
      }
      code = problem.code
      detail = problem.detail ?? problem.title
    } catch {
      // Non-JSON error body (proxy html, empty 502) — status is all we have.
    }
    return new ComukiApiError(response.status, code, detail)
  }

  // -- auth ----------------------------------------------------------------

  /**
   * Email+password login. Returns the session cookie the caller persists
   * via `writeConfigFile`; this client stays cookie-less by design (API
   * key or a new cookie-passing instance is the caller's choice).
   */
  async login(email: string, password: string): Promise<LoginSuccess> {
    const response = await this.fetchImpl(this.baseUrl + "/api/v1/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    })
    if (!response.ok) {
      throw await ComukiClient.toApiError(response)
    }
    const body = (await response.json()) as {
      userId: string
      email: string
      displayName: string
    }
    const setCookie = response.headers.getSetCookie?.() ?? []
    const cookie = ComukiClient.pickSessionCookie(setCookie)
    if (!cookie) {
      throw new ComukiApiError(
        200,
        undefined,
        "login returned no session cookie"
      )
    }
    this.setSessionCookie(cookie)
    this.onSessionCookie?.(cookie)
    return { cookie, ...body }
  }

  /**
   * First non-antiforgery session cookie in a `Set-Cookie` list.
   * Prefers `comuki.auth` / `.Comuki.Session` / `Comuki.Session`; any
   * other named cookie with a value is the fallback (host cookie names
   * have drifted once already).
   */
  static pickSessionCookie(setCookie: readonly string[]): string | null {
    let fallback: string | null = null
    for (const raw of setCookie) {
      const pair = raw.split(";", 1)[0] ?? ""
      const eq = pair.indexOf("=")
      if (eq <= 0) {
        continue
      }
      const name = pair.slice(0, eq).trim()
      const value = pair.slice(eq + 1)
      if (/antiforgery|csrf/i.test(name) || value.length === 0) {
        continue
      }
      const candidate = pair.trim()
      if (SESSION_COOKIE_NAMES.has(name)) {
        return candidate
      }
      fallback ??= candidate
    }
    return fallback
  }

  me(): Promise<MeView> {
    return this.request("GET", "/api/v1/auth/me")
  }

  // -- chat ----------------------------------------------------------------

  createSession(request: {
    projectId?: string
    title?: string
  }): Promise<ChatSessionView> {
    return this.request("POST", "/api/v1/chat/sessions", request)
  }

  async postMessage(
    sessionId: string,
    message: string,
    signal?: AbortSignal
  ): Promise<ChatTurnResultView> {
    const startedAtMs = Date.now()
    const result = await this.request<ChatTurnResultView>(
      "POST",
      `/api/v1/chat/sessions/${sessionId}/messages`,
      { message },
      signal
    )
    // Only successful round-trips count — a refused connection is a
    // "server unreachable" signal, not a fast send.
    this.chatLatencyEmaMs = nextLatencyEma(
      this.chatLatencyEmaMs,
      Date.now() - startedAtMs
    )
    this.onLatencySample?.(this.chatLatencyEmaMs)
    return result
  }

  listMessages(
    sessionId: string,
    page = 1,
    pageSize = 50
  ): Promise<ChatMessagesPageView> {
    return this.request(
      "GET",
      `/api/v1/chat/sessions/${sessionId}/messages?page=${page}&pageSize=${pageSize}`
    )
  }

  approve(
    sessionId: string,
    approved: boolean,
    reason?: string,
    signal?: AbortSignal
  ): Promise<ChatTurnResultView> {
    return this.request(
      "POST",
      `/api/v1/chat/sessions/${sessionId}/approve`,
      { approved, reason },
      signal
    )
  }

  // -- platform ------------------------------------------------------------

  runs(page = 1, pageSize = 20, filter?: string): Promise<RunsPageView> {
    const params = new URLSearchParams({
      page: String(page),
      pageSize: String(pageSize),
    })
    if (filter) {
      params.set("filter", filter)
    }
    return this.request("GET", `/api/v1/runs?${params.toString()}`)
  }

  /**
   * Point-in-time status of the host's background worker registry,
   * ordered by name — the `/workers` panel's single one-shot fetch.
   */
  backgroundWorkers(): Promise<readonly BackgroundWorkerView[]> {
    return this.request("GET", "/api/v1/workers/background")
  }

  knowledgeDocuments(
    page = 1,
    pageSize = 1
  ): Promise<KnowledgeDocumentsPageView> {
    return this.request(
      "GET",
      `/api/v1/knowledge/documents?page=${page}&pageSize=${pageSize}`
    )
  }

  /**
   * Synchronous ingest — one `POST /api/v1/knowledge/ingest` call per
   * document: the server chunks + embeds the text inside the request.
   * Requires the `knowledge:write` permission (403 otherwise); a
   * project-scoped key must pass its own `projectId` — the global
   * corpus is reserved for unrestricted subjects.
   */
  knowledgeIngest(request: {
    projectId?: string
    title: string
    source: string
    sourceRef: string
    mimeType: string
    text: string
  }): Promise<KnowledgeIngestResultView> {
    return this.request("POST", "/api/v1/knowledge/ingest", request)
  }

  /**
   * pgvector cosine search — the same path the MCP `search_knowledge`
   * tool takes. Empty when pgvector is absent; 401/403 when the key
   * lacks `knowledge:read` (the mention layer flags itself off then).
   */
  async knowledgeSearch(
    query: string,
    topK = 8
  ): Promise<readonly KnowledgeSearchHitView[]> {
    const params = new URLSearchParams({ q: query, topK: String(topK) })
    const response = await this.request<{ items: readonly KnowledgeSearchHitView[] }>(
      "GET",
      `/api/v1/knowledge/search?${params.toString()}`
    )
    return response.items
  }

  compute(): Promise<ComputeSnapshotView> {
    return this.request("GET", "/api/v1/compute")
  }

  /**
   * Anonymous liveness probe — `{ status: "ok" }` on a live host.
   * Same path `comuki setup` pings and `/status` surfaces first.
   */
  health(): Promise<HealthView> {
    return this.request("GET", "/api/v1/health")
  }

  projects(): Promise<readonly ProjectView[]> {
    return this.request("GET", "/api/v1/projects?includeArchived=false")
  }

  /**
   * Worker-profile catalog (`GET /profiles`, `plan:read`). Listing
   * only — `createSession` has no profile field, so a selected name
   * cannot ride the next turn.
   */
  profiles(): Promise<readonly ProfileView[]> {
    return this.request("GET", "/profiles")
  }
}

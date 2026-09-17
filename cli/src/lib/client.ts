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
}

/** Smoothing factor for the chat-latency EMA (≈ the last 3 sends dominate). */
const LATENCY_EMA_ALPHA = 0.3

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
  private readonly onLatencySample?: (latencyMs: number) => void
  private chatLatencyEmaMs: number | null = null

  constructor(config: ResolvedConfig, options: ClientOptions = {}) {
    this.fetchImpl = options.fetchImpl ?? (fetch as FetchLike)
    this.signal = options.signal
    this.onLatencySample = options.onLatencySample
    this.baseUrl = config.url.replace(/\/+$/, "")
    this.headers = {
      Accept: "application/json",
      ...(config.apiKey ? { Authorization: `Bearer ${config.apiKey}` } : {}),
      ...(config.tenant ? { "X-Comuki-Tenant": config.tenant } : {}),
      ...(config.cookie ? { Cookie: config.cookie } : {}),
    }
  }

  /** EMA of successful chat POST round-trips; null before the first send. */
  chatLatencyMs(): number | null {
    return this.chatLatencyEmaMs
  }

  /** Same-origin SignalR hub URL (`<url>/ws/runs`). */
  readonly hubUrl = () => this.baseUrl + "/ws/runs"

  /** Headers the hub connection must send (API key / cookie auth). */
  readonly hubHeaders = (): Record<string, string> => {
    const headers: Record<string, string> = {}
    if (this.headers.Authorization) {
      headers.Authorization = this.headers.Authorization
    }
    if (this.headers.Cookie) {
      headers.Cookie = this.headers.Cookie
    }
    if (this.headers["X-Comuki-Tenant"]) {
      headers["X-Comuki-Tenant"] = this.headers["X-Comuki-Tenant"]
    }
    return headers
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

    if (!response.ok) {
      throw await ComukiClient.toApiError(response)
    }
    if (response.status === 204) {
      return undefined as T
    }
    return (await response.json()) as T
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
    return { cookie, ...body }
  }

  private static pickSessionCookie(
    setCookie: readonly string[]
  ): string | null {
    for (const raw of setCookie) {
      const pair = raw.split(";", 1)[0] ?? ""
      const eq = pair.indexOf("=")
      if (eq > 0) {
        const name = pair.slice(0, eq).trim()
        // Host session cookie is `.Comuki.Session` / `Comuki.Session`;
        // any non-antiforgery cookie with a value wins as a fallback.
        if (!/antiforgery|csrf/i.test(name) && pair.slice(eq + 1).length > 0) {
          return pair.trim()
        }
      }
    }
    return null
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

  knowledgeDocuments(
    page = 1,
    pageSize = 1
  ): Promise<KnowledgeDocumentsPageView> {
    return this.request(
      "GET",
      `/api/v1/knowledge/documents?page=${page}&pageSize=${pageSize}`
    )
  }

  compute(): Promise<ComputeSnapshotView> {
    return this.request("GET", "/api/v1/compute")
  }

  projects(): Promise<readonly ProjectView[]> {
    return this.request("GET", "/api/v1/projects?includeArchived=false")
  }
}

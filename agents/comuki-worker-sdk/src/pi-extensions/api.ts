/**
 * Minimal structural types for the parts of pi-coding-agent's
 * `ExtensionAPI` that the worker-SDK extensions use. We keep this
 * dependency-free so the worker-sdk typechecks without importing
 * `@earendil-works/pi-coding-agent` directly — the runtime is free to
 * pass its real `ExtensionAPI` in, which is structurally compatible.
 *
 * Adding a new field here must follow two rules:
 *   1. The shape must be assignable to the corresponding pi type
 *      (pi's actual `ExtensionAPI` is the assignment source).
 *   2. The handler must tolerate `unknown` for the event payload so
 *      untyped pi events still pass through.
 */

export interface PiExtensionContext {
  readonly hasUI: boolean
}

/** Subset of `ToolCallEvent` we actually discriminate on. */
export interface PiToolCallEvent {
  readonly toolName: string
  readonly input: Readonly<Record<string, unknown>>
}

export interface PiToolCallResult {
  readonly block?: boolean
  readonly reason?: string
}

/** Subset of `ResourcesDiscoverEvent` we read. */
export interface PiResourcesDiscoverEvent {
  readonly cwd: string
  readonly reason: string
}

export interface PiResourcesDiscoverResult {
  readonly skillPaths?: readonly string[]
}

/**
 * Subset of `McpServerConfig` the worker SDK needs. We accept any extra
 * fields because the real config has more keys (timeout, exposure, etc.)
 * and the worker SDK does not own the policy for them.
 */
export interface PiMcpServerConfig {
  readonly url?: string
  readonly command?: string
  readonly args?: readonly string[]
  readonly env?: Readonly<Record<string, string>>
  readonly headers?: Readonly<Record<string, string>>
  readonly description?: string
  readonly [key: string]: unknown
}

export interface PiExtensionApi {
  on(
    event: "tool_call",
    handler: (
      event: PiToolCallEvent,
      ctx: PiExtensionContext
    ) => PiToolCallResult | undefined | Promise<PiToolCallResult | undefined>
  ): () => void
  on(
    event: "resources_discover",
    handler: (
      event: PiResourcesDiscoverEvent,
      ctx: PiExtensionContext
    ) =>
      | PiResourcesDiscoverResult
      | undefined
      | Promise<PiResourcesDiscoverResult | undefined>
  ): () => void
  registerMcpServer(name: string, config: PiMcpServerConfig): void
}

export type ExtensionFactory = (api: PiExtensionApi) => void | Promise<void>

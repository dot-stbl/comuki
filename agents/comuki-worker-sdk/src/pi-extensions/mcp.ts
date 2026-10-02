/**
 * Pi-extension exposing the Comuki MCP server to pi via the worker SDK.
 *
 * Spec (openspec/changes/harden-pi-worker-sandbox §6.3): when
 * `COMUKI_MCP_URL` is set on the pi process, the worker-SDK SHALL expose
 * MCP tools to pi. Unset URL → no MCP tools. The dev-sdk's MCP client is
 * NOT the worker path — it sits in the developer's IDE, not the worker
 * container. Egress to the MCP host is gated by the platform's allowlist
 * (the worker image already enforces it; this extension does not).
 *
 * The extension is opt-in: `fromEnv` returns `null` for `createMcpExtension`
 * when the URL is missing, so the loader can decide to skip wiring the
 * server entirely. Calling `createMcpExtension` without an explicit URL
 * always reads `COMUKI_MCP_URL` (and `COMUKI_MCP_TOKEN`) at construction
 * time — exactly once, before pi registers the server.
 */
import type {
  ExtensionFactory,
  PiExtensionApi,
  PiMcpServerConfig,
} from "./api"

export interface McpExtensionEnv {
  readonly COMUKI_MCP_URL?: string | undefined
  readonly COMUKI_MCP_TOKEN?: string | undefined
}

export interface McpExtensionOptions {
  readonly env?: McpExtensionEnv
  /**
   * Optional override for the registered server name. Defaults to
   * `comuki`. The MCP tool prefix becomes `mcp__<name>__<tool>` so the
   * surface is stable across renames of the platform's MCP service.
   */
  readonly serverName?: string
}

export const DEFAULT_MCP_SERVER_NAME = "comuki"

/**
 * Snapshot the env into a minimal env shape — only `COMUKI_MCP_URL` and
 * `COMUKI_MCP_TOKEN` are read. Returns `null` when the URL is missing or
 * blank, so callers can branch on "no MCP tools" without a sentinel value.
 */
export function readMcpConfig(env: McpExtensionEnv): {
  readonly url: string
  readonly token: string | undefined
} | null {
  const rawUrl = env.COMUKI_MCP_URL?.trim()
  if (rawUrl === undefined || rawUrl.length === 0) {
    return null
  }
  const rawToken = env.COMUKI_MCP_TOKEN?.trim()
  return {
    url: rawUrl,
    token: rawToken === undefined || rawToken.length === 0 ? undefined : rawToken,
  }
}

/**
 * Default name for the MCP server name → tool prefix mapping
 * (`mcp__comuki__<tool>`).
 */
export function createMcpExtension(
  options: McpExtensionOptions = {}
): ExtensionFactory {
  const config = readMcpConfig(options.env ?? defaultMcpEnv())
  const serverName = options.serverName ?? DEFAULT_MCP_SERVER_NAME

  return function mcpExtension(api: PiExtensionApi): void {
    if (config === null) {
      // Spec: unset URL → no MCP tools. Do nothing; pi registers nothing.
      return
    }

    const headers: Record<string, string> = {}
    if (config.token !== undefined) {
      headers.authorization = `Bearer ${config.token}`
    }

    const serverConfig: PiMcpServerConfig = {
      url: config.url,
      headers,
      description: "Comuki knowledge MCP — exposed via the worker SDK",
    }

    api.registerMcpServer(serverName, serverConfig)
  }
}

/**
 * Narrow `process.env` to the two Comuki MCP keys the extension reads.
 * Node's `ProcessEnv` is indexable but not structurally assignable to
 * a named-key shape, so the pick happens here.
 */
function defaultMcpEnv(): McpExtensionEnv {
  return {
    COMUKI_MCP_URL: process.env.COMUKI_MCP_URL,
    COMUKI_MCP_TOKEN: process.env.COMUKI_MCP_TOKEN,
  }
}
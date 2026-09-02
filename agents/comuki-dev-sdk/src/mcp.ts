/**
 * Thin MCP client to the Comuki Knowledge server (or any MCP endpoint).
 *
 * - URL from `COMUKI_MCP_URL`; optional bearer token from `COMUKI_MCP_TOKEN`
 *   (sent as `Authorization` on every request).
 * - Prefers the streamable-HTTP transport, falls back to legacy SSE.
 * - Fail-soft: a missing URL yields `null` from `fromEnv`; a failed connect
 *   yields `false` / empty results rather than throwing — a dev session must
 *   survive the platform being offline.
 * - `fetchImpl` exists for tests; it is handed to the transports directly.
 */
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { SSEClientTransport } from '@modelcontextprotocol/sdk/client/sse.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';

export type FetchFn = (url: string | URL, init?: RequestInit) => Promise<Response>;

export interface ComukiMcpOptions {
  readonly url: string;
  readonly token?: string;
  readonly fetchImpl?: FetchFn;
  readonly clientName?: string;
  readonly clientVersion?: string;
}

export interface ComukiTool {
  readonly name: string;
  readonly description?: string;
}

export interface ComukiToolCallResult {
  readonly content: readonly unknown[];
  readonly isError: boolean;
}

const DEFAULT_CLIENT_NAME = 'comuki-dev-sdk';
const DEFAULT_CLIENT_VERSION = '0.1.0';
const CONNECT_TIMEOUT_MS = 5_000;
const REQUEST_TIMEOUT_MS = 10_000;

export class ComukiMcpClient {
  private client: Client | null = null;

  private constructor(private readonly options: ComukiMcpOptions) {}

  /** `null` when `COMUKI_MCP_URL` is not set — MCP is opt-in. */
  static fromEnv(env: Record<string, string | undefined> = process.env): ComukiMcpClient | null {
    const url = env.COMUKI_MCP_URL?.trim();
    if (url === undefined || url.length === 0) {
      return null;
    }
    const token = env.COMUKI_MCP_TOKEN?.trim();
    return new ComukiMcpClient({ url, token: token === undefined || token.length === 0 ? undefined : token });
  }

  static create(options: ComukiMcpOptions): ComukiMcpClient {
    return new ComukiMcpClient(options);
  }

  /** Attempts streamable-HTTP, then legacy SSE. Resolves `false` on failure. */
  async connect(): Promise<boolean> {
    const headers: Record<string, string> = { accept: 'application/json, text/event-stream' };
    if (this.options.token !== undefined) {
      headers.authorization = `Bearer ${this.options.token}`;
    }

    const transports = [
      (): StreamableHTTPClientTransport =>
        new StreamableHTTPClientTransport(new URL(this.options.url), {
          requestInit: { headers },
          fetch: this.options.fetchImpl,
        }),
      (): SSEClientTransport =>
        new SSEClientTransport(new URL(this.options.url), {
          requestInit: { headers },
          fetch: this.options.fetchImpl,
        }),
    ];

    for (const createTransport of transports) {
      const client = new Client(
        { name: this.options.clientName ?? DEFAULT_CLIENT_NAME, version: this.options.clientVersion ?? DEFAULT_CLIENT_VERSION },
      );
      try {
        await withTimeout(client.connect(createTransport()), CONNECT_TIMEOUT_MS, 'connect');
        this.client = client;
        return true;
      } catch {
        await client.close().catch(() => undefined);
      }
    }

    return false;
  }

  async listTools(): Promise<ComukiTool[]> {
    const client = await this.ensureConnected();
    if (client === null) {
      return [];
    }
    try {
      const result = await withTimeout(client.listTools(), REQUEST_TIMEOUT_MS, 'listTools');
      return result.tools.map((tool) => ({ name: tool.name, description: tool.description }));
    } catch {
      return [];
    }
  }

  async callTool(name: string, args: Readonly<Record<string, unknown>> = {}): Promise<ComukiToolCallResult | null> {
    const client = await this.ensureConnected();
    if (client === null) {
      return null;
    }
    try {
      const result = await withTimeout(
        client.callTool({ name, arguments: { ...args } }),
        REQUEST_TIMEOUT_MS,
        'callTool',
      );
      return { content: result.content as readonly unknown[], isError: result.isError === true };
    } catch {
      return null;
    }
  }

  async close(): Promise<void> {
    const client = this.client;
    this.client = null;
    if (client !== null) {
      await client.close().catch(() => undefined);
    }
  }

  private async ensureConnected(): Promise<Client | null> {
    if (this.client !== null) {
      return this.client;
    }
    return (await this.connect()) ? this.client : null;
  }
}

async function withTimeout<T>(promise: Promise<T>, timeoutMs: number, label: string): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([
      promise,
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(`${label} timed out after ${timeoutMs}ms`)), timeoutMs);
      }),
    ]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

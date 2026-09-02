import { afterEach, describe, expect, test } from 'bun:test';
import { ComukiMcpClient } from './mcp';
import type { FetchFn } from './mcp';

interface RecordedRequest {
  readonly method: string;
  readonly url: string;
  readonly authorization: string | null;
  readonly accept: string | null;
  readonly body: unknown;
}

/**
 * Fake fetch speaking just enough streamable-HTTP MCP for the wrapper:
 * initialize handshake, initialized notification, tools/list, tools/call,
 * a 405 standalone GET (server does not offer the SSE stream) and session
 * DELETE on close.
 */
function fakeStreamableServer(options: { readonly failWith?: number } = {}): { fetchImpl: FetchFn; requests: RecordedRequest[] } {
  const requests: RecordedRequest[] = [];

  const fetchImpl: FetchFn = async (url, init) => {
    const requestUrl = String(url);
    const bodyText = typeof init?.body === 'string' ? init.body : '';
    const body = bodyText.length > 0 ? (JSON.parse(bodyText) as { method?: string; id?: number | string }) : undefined;
    requests.push({
      method: init?.method ?? 'GET',
      url: requestUrl,
      authorization: init?.headers instanceof Headers ? init.headers.get('authorization') : null,
      accept: init?.headers instanceof Headers ? init.headers.get('accept') : null,
      body,
    });

    if (options.failWith !== undefined) {
      return new Response(null, { status: options.failWith });
    }

    if (body?.method === 'initialize') {
      return jsonResponse({
        jsonrpc: '2.0',
        id: body.id,
        result: { protocolVersion: '2025-06-18', capabilities: { tools: {} }, serverInfo: { name: 'fake', version: '0' } },
      });
    }
    if (body?.method === 'notifications/initialized') {
      return new Response(null, { status: 202 });
    }
    if (body?.method === 'tools/list') {
      return jsonResponse({
        jsonrpc: '2.0',
        id: body.id,
        result: { tools: [{ name: 'search', description: 'Search the knowledge base', inputSchema: { type: 'object' } }] },
      });
    }
    if (body?.method === 'tools/call') {
      return jsonResponse({
        jsonrpc: '2.0',
        id: body.id,
        result: { content: [{ type: 'text', text: 'hit' }], isError: false },
      });
    }

    // Standalone GET stream and session DELETE: 405 is a valid "not offered".
    return new Response(null, { status: 405 });
  };

  return { fetchImpl, requests };
}

function jsonResponse(payload: unknown): Response {
  return new Response(JSON.stringify(payload), { status: 200, headers: { 'content-type': 'application/json' } });
}

const created: ComukiMcpClient[] = [];

function client(options: { readonly token?: string; readonly fetchImpl: FetchFn }): ComukiMcpClient {
  const instance = ComukiMcpClient.create({ url: 'http://fake/mcp', token: options.token, fetchImpl: options.fetchImpl });
  created.push(instance);
  return instance;
}

afterEach(async () => {
  for (const instance of created.splice(0)) {
    await instance.close();
  }
});

describe('fromEnv', () => {
  test('returns null without COMUKI_MCP_URL', () => {
    expect(ComukiMcpClient.fromEnv({})).toBeNull();
    expect(ComukiMcpClient.fromEnv({ COMUKI_MCP_URL: '   ' })).toBeNull();
  });

  test('builds a client from env with optional token', async () => {
    const instance = ComukiMcpClient.fromEnv({ COMUKI_MCP_URL: 'http://host/mcp', COMUKI_MCP_TOKEN: 'secret' });

    expect(instance).not.toBeNull();
    await instance?.close();
  });
});

describe('connect over streamable http', () => {
  test('connects, sends bearer auth and negotiates initialize', async () => {
    const server = fakeStreamableServer();
    const mcp = client({ token: 'secret-token', fetchImpl: server.fetchImpl });

    await expect(mcp.connect()).resolves.toBe(true);

    const initialize = server.requests.find((request) => (request.body as { method?: string } | undefined)?.method === 'initialize');
    expect(initialize?.authorization).toBe('Bearer secret-token');
    expect(initialize?.url).toBe('http://fake/mcp');
  });

  test('listTools maps tool names and descriptions', async () => {
    const server = fakeStreamableServer();
    const mcp = client({ fetchImpl: server.fetchImpl });

    const tools = await mcp.listTools();

    expect(tools).toEqual([{ name: 'search', description: 'Search the knowledge base' }]);
  });

  test('listTools auto-connects when connect was not called first', async () => {
    const server = fakeStreamableServer();
    const mcp = client({ fetchImpl: server.fetchImpl });

    await expect(mcp.listTools()).resolves.toBeArrayOfSize(1);
  });

  test('callTool passes arguments and maps the result', async () => {
    const server = fakeStreamableServer();
    const mcp = client({ fetchImpl: server.fetchImpl });
    await mcp.connect();

    const result = await mcp.callTool('search', { query: 'locks' });

    expect(result?.isError).toBe(false);
    expect(result?.content).toEqual([{ type: 'text', text: 'hit' }]);
    const call = server.requests.find((request) => (request.body as { method?: string } | undefined)?.method === 'tools/call');
    expect((call?.body as { params?: { arguments?: { query: string } } })?.params?.arguments).toEqual({ query: 'locks' });
  });
});

describe('fail-soft behaviour', () => {
  test('network failure connects false and yields empty results', async () => {
    const failing: FetchFn = () => Promise.reject(new Error('ECONNREFUSED'));
    const mcp = client({ fetchImpl: failing });

    await expect(mcp.connect()).resolves.toBe(false);
    await expect(mcp.listTools()).resolves.toEqual([]);
    await expect(mcp.callTool('search')).resolves.toBeNull();
  });

  test('server rejecting streamable falls back to the SSE transport before giving up', async () => {
    // 404 on initialize POST: streamable fails; the SSE fallback then issues a
    // GET with `Accept: text/event-stream`, which also 404s → connect false.
    const server = fakeStreamableServer({ failWith: 404 });
    const mcp = client({ fetchImpl: server.fetchImpl });

    await expect(mcp.connect()).resolves.toBe(false);

    const sseAttempt = server.requests.find(
      (request) => request.method === 'GET' && request.accept?.includes('text/event-stream') === true,
    );
    expect(sseAttempt).toBeDefined();
  });
});

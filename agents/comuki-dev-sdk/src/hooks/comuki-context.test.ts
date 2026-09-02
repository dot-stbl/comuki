import { describe, expect, test } from 'bun:test';
import { buildSessionContext, memoryDigestUrl, profilesCatalogUrl } from './comuki-context';
import type { FetchFn } from '../mcp';

function okResponse(body: string): Response {
  return new Response(body, { status: 200, headers: { 'content-type': 'application/json' } });
}

function recordingFetch(handler: (url: string) => Response | null): { fetchImpl: FetchFn; urls: string[] } {
  const urls: string[] = [];
  const fetchImpl: FetchFn = (url) => {
    const urlText = String(url);
    urls.push(urlText);
    const response = handler(urlText);
    if (response === null) {
      return Promise.reject(new Error('network down'));
    }
    return Promise.resolve(response);
  };
  return { fetchImpl, urls };
}

describe('url builders', () => {
  test('profiles catalog url strips trailing slashes', () => {
    expect(profilesCatalogUrl('http://localhost:17173')).toBe('http://localhost:17173/profiles');
    expect(profilesCatalogUrl('http://localhost:17173///')).toBe('http://localhost:17173/profiles');
  });

  test('memory digest url encodes the project name', () => {
    expect(memoryDigestUrl('http://host', 'my project')).toBe('http://host/api/v1/memory/digest?project=my%20project');
    expect(memoryDigestUrl('http://host')).toBe('http://host/api/v1/memory/digest');
  });
});

describe('buildSessionContext', () => {
  test('empty env produces no context', async () => {
    expect(await buildSessionContext({})).toBe('');
  });

  test('project only — header without digest, no network', async () => {
    const { fetchImpl, urls } = recordingFetch(() => okResponse('{}'));

    const context = await buildSessionContext({ COMUKI_PROJECT: 'anlytra' }, fetchImpl);

    expect(context).toContain('## Comuki context');
    expect(context).toContain('- Project: anlytra');
    expect(context).not.toContain('Memory digest');
    expect(urls).toEqual([]);
  });

  test('host adds catalog pointer and fetches the digest', async () => {
    const { fetchImpl, urls } = recordingFetch(() => okResponse('{"summary":"recent decisions"}'));

    const context = await buildSessionContext({ COMUKI_PROJECT: 'anlytra', COMUKI_HOST: 'http://localhost:17173' }, fetchImpl);

    expect(context).toContain('- Profiles catalog: http://localhost:17173/profiles');
    expect(context).toContain('### Memory digest');
    expect(context).toContain('{"summary":"recent decisions"}');
    expect(urls).toEqual(['http://localhost:17173/api/v1/memory/digest?project=anlytra']);
  });

  test('mcp url is listed when set', async () => {
    const context = await buildSessionContext({ COMUKI_MCP_URL: 'http://host/mcp' });

    expect(context).toContain('- Comuki MCP: http://host/mcp');
  });

  test('blank env values are treated as unset', async () => {
    const context = await buildSessionContext({ COMUKI_PROJECT: '  ', COMUKI_HOST: '' });

    expect(context).toBe('');
  });

  test('digest fetch failure is fail-soft offline', async () => {
    const { fetchImpl } = recordingFetch(() => null);

    const context = await buildSessionContext({ COMUKI_PROJECT: 'anlytra', COMUKI_HOST: 'http://localhost:17173' }, fetchImpl);

    expect(context).toContain('## Comuki context');
    expect(context).not.toContain('Memory digest');
  });

  test('digest non-200 is skipped', async () => {
    const { fetchImpl } = recordingFetch(() => new Response('nope', { status: 503 }));

    const context = await buildSessionContext({ COMUKI_PROJECT: 'anlytra', COMUKI_HOST: 'http://localhost:17173' }, fetchImpl);

    expect(context).not.toContain('Memory digest');
  });

  test('digest is capped at MAX_DIGEST_LENGTH', async () => {
    const { fetchImpl } = recordingFetch(() => okResponse('x'.repeat(10_000)));

    const context = await buildSessionContext({ COMUKI_HOST: 'http://localhost:17173' }, fetchImpl);

    const digestStart = context.indexOf('### Memory digest\n');
    expect(digestStart).toBeGreaterThanOrEqual(0);
    expect(context.length - (digestStart + '### Memory digest\n'.length)).toBe(4_000);
  });
});

/**
 * Claude Code SessionStart hook — injects the Comuki context header into the
 * session: current project, profiles catalog pointer, MCP endpoint and (when
 * `COMUKI_HOST` is set) the project memory digest.
 *
 * Output is plain stdout: for SessionStart, Claude Code adds stdout to the
 * session context on every hooks version — no `hookSpecificOutput` needed.
 * Everything network-shaped is fail-soft: offline or a slow host leaves the
 * header intact without the digest.
 */
import type { FetchFn } from '../mcp';

export interface ContextEnv {
  readonly COMUKI_PROJECT?: string;
  readonly COMUKI_HOST?: string;
  readonly COMUKI_MCP_URL?: string;
}

export const MEMORY_DIGEST_TIMEOUT_MS = 2_000;
export const MAX_DIGEST_LENGTH = 4_000;

export function profilesCatalogUrl(host: string): string {
  return `${trimTrailingSlash(host)}/profiles`;
}

/**
 * Provisional contract — the platform memory endpoints land with Slice 2;
 * the digest call stays fail-soft so the URL shape can settle later.
 */
export function memoryDigestUrl(host: string, project?: string): string {
  const base = `${trimTrailingSlash(host)}/api/v1/memory/digest`;
  return project === undefined || project.length === 0 ? base : `${base}?project=${encodeURIComponent(project)}`;
}

export async function fetchMemoryDigest(url: string, fetchImpl: FetchFn = fetch): Promise<string | null> {
  try {
    const response = await fetchImpl(url, {
      headers: { accept: 'application/json' },
      signal: AbortSignal.timeout(MEMORY_DIGEST_TIMEOUT_MS),
    });
    if (!response.ok) {
      return null;
    }
    const text = (await response.text()).trim();
    return text.length === 0 ? null : text.slice(0, MAX_DIGEST_LENGTH);
  } catch {
    return null;
  }
}

export async function buildSessionContext(env: ContextEnv, fetchImpl: FetchFn = fetch): Promise<string> {
  const project = nonEmpty(env.COMUKI_PROJECT);
  const host = nonEmpty(env.COMUKI_HOST);
  const mcpUrl = nonEmpty(env.COMUKI_MCP_URL);

  const lines: string[] = [];
  if (project !== undefined) {
    lines.push(`- Project: ${project}`);
  }
  if (host !== undefined) {
    lines.push(`- Profiles catalog: ${profilesCatalogUrl(host)}`);
  }
  if (mcpUrl !== undefined) {
    lines.push(`- Comuki MCP: ${mcpUrl}`);
  }
  if (lines.length === 0) {
    return '';
  }

  let context = `## Comuki context\n${lines.join('\n')}`;

  if (host !== undefined) {
    const digest = await fetchMemoryDigest(memoryDigestUrl(host, project), fetchImpl);
    if (digest !== null) {
      context += `\n\n### Memory digest\n${digest}`;
    }
  }

  return context;
}

function nonEmpty(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed === undefined || trimmed.length === 0 ? undefined : trimmed;
}

function trimTrailingSlash(host: string): string {
  return host.trim().replace(/\/+$/, '');
}

if (import.meta.main) {
  const context = await buildSessionContext({
    COMUKI_PROJECT: process.env.COMUKI_PROJECT,
    COMUKI_HOST: process.env.COMUKI_HOST,
    COMUKI_MCP_URL: process.env.COMUKI_MCP_URL,
  });
  if (context.length > 0) {
    process.stdout.write(`${context}\n`);
  }
}

import { z } from 'zod';

/**
 * TS mirror of the C# `Comuki.Host.Translator.Parsing.PiEvent` discriminated
 * union. The shapes follow Claude Code's stream-json convention — the same
 * convention `pi-coding-agent` uses. Field names are the camelCase forms the
 * C# parser emits, so both sides of the Translator seam agree on one shape.
 *
 * Variants are discriminated by `kind` (not the wire `type` field, which the
 * `unknown` variant still carries verbatim).
 */

export const systemEventSchema = z.object({
  kind: z.literal('system'),
  subtype: z.string(),
  cwd: z.string(),
  tools: z.array(z.string()),
});
export type SystemEvent = z.infer<typeof systemEventSchema>;

export const userEventSchema = z.object({
  kind: z.literal('user'),
  text: z.string(),
});
export type UserEvent = z.infer<typeof userEventSchema>;

export const assistantTextEventSchema = z.object({
  kind: z.literal('assistant-text'),
  text: z.string(),
});
export type AssistantTextEvent = z.infer<typeof assistantTextEventSchema>;

export const assistantToolUseEventSchema = z.object({
  kind: z.literal('assistant-tool-use'),
  tool: z.string(),
  inputJson: z.string(),
});
export type AssistantToolUseEvent = z.infer<typeof assistantToolUseEventSchema>;

export const resultEventSchema = z.object({
  kind: z.literal('result'),
  subtype: z.string(),
  durationMs: z.number(),
  costUsd: z.number(),
  result: z.string(),
});
export type ResultEvent = z.infer<typeof resultEventSchema>;

export const unknownEventSchema = z.object({
  kind: z.literal('unknown'),
  type: z.string(),
  raw: z.unknown(),
});
export type UnknownEvent = z.infer<typeof unknownEventSchema>;

export const unparseableEventSchema = z.object({
  kind: z.literal('unparseable'),
  line: z.string(),
  error: z.string(),
});
export type UnparseableEvent = z.infer<typeof unparseableEventSchema>;

export const piEventSchema = z.discriminatedUnion('kind', [
  systemEventSchema,
  userEventSchema,
  assistantTextEventSchema,
  assistantToolUseEventSchema,
  resultEventSchema,
  unknownEventSchema,
  unparseableEventSchema,
]);
export type PiEvent = z.infer<typeof piEventSchema>;

/**
 * Parses a single line of `pi --output-format stream-json` output. Mirrors the
 * C# `StreamJsonParser.ParseLine` contract: blank lines yield `null`, malformed
 * JSON yields an `unparseable` event, unmodelled event types yield an `unknown`
 * event — a single bad line never throws.
 */
export function parsePiLine(line: string): PiEvent | null {
  if (line.trim().length === 0) {
    return null;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(line);
  } catch (error) {
    return { kind: 'unparseable', line, error: errorMessage(error) };
  }

  if (!isRecord(parsed) || typeof parsed.type !== 'string') {
    return { kind: 'unparseable', line, error: "Missing or non-string 'type' field" };
  }

  switch (parsed.type) {
    case 'system':
      return mapSystem(parsed);
    case 'user':
      return mapUser(parsed);
    case 'assistant':
      return mapAssistant(parsed);
    case 'result':
      return mapResult(parsed);
    default:
      return { kind: 'unknown', type: parsed.type, raw: parsed };
  }
}

/**
 * Parses a full stream-json dump (one JSON object per line). Mirrors the C#
 * `StreamJsonParser.Parse` overload for text input.
 */
export function parsePiStream(text: string): PiEvent[] {
  const events: PiEvent[] = [];
  for (const line of text.split('\n')) {
    const piEvent = parsePiLine(line);
    if (piEvent !== null) {
      events.push(piEvent);
    }
  }
  return events;
}

function mapSystem(root: Record<string, unknown>): SystemEvent {
  return {
    kind: 'system',
    subtype: asString(root.subtype) ?? 'init',
    cwd: asString(root.cwd) ?? '',
    tools: Array.isArray(root.tools)
      ? root.tools.filter((tool): tool is string => typeof tool === 'string')
      : [],
  };
}

function mapUser(root: Record<string, unknown>): UserEvent {
  const message = isRecord(root.message) ? root.message : null;
  const content = message !== null ? message.content : undefined;

  if (typeof content === 'string') {
    return { kind: 'user', text: content };
  }

  if (Array.isArray(content)) {
    const text = content
      .filter(isRecord)
      .map((block) => asString(block.text))
      .filter((part): part is string => part !== null)
      .join('');
    return { kind: 'user', text };
  }

  return { kind: 'user', text: '' };
}

function mapAssistant(root: Record<string, unknown>): PiEvent {
  const message = isRecord(root.message) ? root.message : null;
  const content = message !== null ? message.content : undefined;
  if (!Array.isArray(content)) {
    return { kind: 'unknown', type: 'assistant', raw: root };
  }

  for (const block of content) {
    if (!isRecord(block)) {
      continue;
    }
    if (asString(block.type) === 'text') {
      const text = asString(block.text);
      if (text !== null) {
        return { kind: 'assistant-text', text };
      }
    }
    if (asString(block.type) === 'tool_use') {
      const tool = asString(block.name);
      if (tool !== null) {
        return { kind: 'assistant-tool-use', tool, inputJson: JSON.stringify(block.input ?? {}) };
      }
    }
  }

  return { kind: 'unknown', type: 'assistant', raw: root };
}

function mapResult(root: Record<string, unknown>): ResultEvent {
  return {
    kind: 'result',
    subtype: asString(root.subtype) ?? 'success',
    durationMs: asFiniteNumber(root.duration_ms) ?? 0,
    costUsd: asFiniteNumber(root.cost_usd) ?? 0,
    result: asString(root.result) ?? '',
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function asString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function asFiniteNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

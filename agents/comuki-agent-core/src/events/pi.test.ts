import { describe, expect, test } from 'bun:test';
import {
  parsePiLine,
  parsePiStream,
  piEventSchema,
  type PiEvent,
  type UnknownEvent,
  type UnparseableEvent,
} from './pi';

// Fixture lines mirror tests/unit/Comuki.Host.Translator.Unit.StreamJson/Fixtures.
const SYSTEM_LINE =
  '{"type":"system","subtype":"init","cwd":"/work","tools":["Read","Write","Edit","Bash"]}';
const USER_LINE = '{"type":"user","message":{"role":"user","content":"Say hello in exactly one word"}}';
const USER_BLOCK_LINE =
  '{"type":"user","message":{"role":"user","content":[{"type":"text","text":"Hello "},{"type":"text","text":"world"}]}}';
const ASSISTANT_TEXT_LINE =
  '{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Hello"}]}}';
const ASSISTANT_TOOL_USE_LINE =
  '{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","name":"Bash","input":{"command":"ls /work"}}]}}';
const RESULT_LINE =
  '{"type":"result","subtype":"success","duration_ms":1234,"cost_usd":0.0012,"result":"Hello"}';
const UNKNOWN_LINE = '{"type":"some_future_event_type","data":{"hello":"world"}}';
const MALFORMED_LINE = 'this line is not valid json';

describe('parsePiLine', () => {
  test('recognizes system event', () => {
    const piEvent = parsePiLine(SYSTEM_LINE);

    expect(piEvent).toEqual({
      kind: 'system',
      subtype: 'init',
      cwd: '/work',
      tools: ['Read', 'Write', 'Edit', 'Bash'],
    });
  });

  test('recognizes user event with string content', () => {
    const piEvent = parsePiLine(USER_LINE);

    expect(piEvent).toEqual({ kind: 'user', text: 'Say hello in exactly one word' });
  });

  test('joins user block-array content', () => {
    const piEvent = parsePiLine(USER_BLOCK_LINE);

    expect(piEvent).toEqual({ kind: 'user', text: 'Hello world' });
  });

  test('recognizes assistant text event', () => {
    const piEvent = parsePiLine(ASSISTANT_TEXT_LINE);

    expect(piEvent).toEqual({ kind: 'assistant-text', text: 'Hello' });
  });

  test('recognizes assistant tool-use event', () => {
    const piEvent = parsePiLine(ASSISTANT_TOOL_USE_LINE);

    expect(piEvent).toEqual({ kind: 'assistant-tool-use', tool: 'Bash', inputJson: '{"command":"ls /work"}' });
  });

  test('recognizes result event', () => {
    const piEvent = parsePiLine(RESULT_LINE);

    expect(piEvent).toEqual({
      kind: 'result',
      subtype: 'success',
      durationMs: 1234,
      costUsd: 0.0012,
      result: 'Hello',
    });
  });

  test('yields unknown event for unmodelled type', () => {
    const piEvent = parsePiLine(UNKNOWN_LINE) as UnknownEvent;

    expect(piEvent.kind).toBe('unknown');
    expect(piEvent.type).toBe('some_future_event_type');
    expect(piEvent.raw).toEqual({ type: 'some_future_event_type', data: { hello: 'world' } });
  });

  test('yields unparseable event for malformed json', () => {
    const piEvent = parsePiLine(MALFORMED_LINE) as UnparseableEvent;

    expect(piEvent.kind).toBe('unparseable');
    expect(piEvent.line).toContain('not valid json');
    expect(piEvent.error.length).toBeGreaterThan(0);
  });

  test('yields unparseable event for missing type field', () => {
    const piEvent = parsePiLine('{"data":1}') as UnparseableEvent;

    expect(piEvent.kind).toBe('unparseable');
    expect(piEvent.error).toBe("Missing or non-string 'type' field");
  });

  test('returns null for blank lines', () => {
    expect(parsePiLine('')).toBeNull();
    expect(parsePiLine('   ')).toBeNull();
    expect(parsePiLine('\t')).toBeNull();
  });
});

describe('parsePiStream', () => {
  test('parses every non-empty line in order', () => {
    const events = parsePiStream(
      [SYSTEM_LINE, USER_LINE, MALFORMED_LINE, RESULT_LINE].join('\n'),
    );

    expect(events.map((piEvent) => piEvent.kind)).toEqual([
      'system',
      'user',
      'unparseable',
      'result',
    ]);
  });

  test('skips empty and whitespace-only lines', () => {
    expect(parsePiStream('\n  \n\t\n')).toEqual([]);
  });
});

describe('piEventSchema round-trips', () => {
  test.each([
    ['system', SYSTEM_LINE],
    ['user', USER_LINE],
    ['user blocks', USER_BLOCK_LINE],
    ['assistant-text', ASSISTANT_TEXT_LINE],
    ['assistant-tool-use', ASSISTANT_TOOL_USE_LINE],
    ['result', RESULT_LINE],
    ['unknown', UNKNOWN_LINE],
  ] as const)('validate %s after JSON round-trip', (_label, line) => {
    const piEvent = parsePiLine(line) as PiEvent;

    const roundTripped = piEventSchema.parse(JSON.parse(JSON.stringify(piEvent)));

    expect(roundTripped).toEqual(piEvent);
  });

  test('unparseable events survive schema validation', () => {
    const piEvent = parsePiLine(MALFORMED_LINE) as PiEvent;

    expect(piEventSchema.parse(piEvent)).toEqual(piEvent);
  });
});

import { describe, expect, test } from 'bun:test';
import { briefSchema, parseBrief } from './brief';

describe('parseBrief', () => {
  test('parses a full brief with optional fields', () => {
    const brief = parseBrief({
      taskId: '0192f0c1-0000-7000-8000-000000000001',
      profileKey: 'implement',
      prompt: 'Add a login page',
      contextFiles: ['docs/spec.md', 'src/auth.ts'],
      rulesDigest: 'no-console-log; tests are platform-owned',
    });

    expect(brief).toEqual({
      taskId: '0192f0c1-0000-7000-8000-000000000001',
      profileKey: 'implement',
      prompt: 'Add a login page',
      contextFiles: ['docs/spec.md', 'src/auth.ts'],
      rulesDigest: 'no-console-log; tests are platform-owned',
    });
  });

  test('parses a minimal brief', () => {
    const brief = parseBrief({
      taskId: 't-1',
      profileKey: 'explore-readonly',
      prompt: 'Read the code and summarize',
    });

    expect(brief.contextFiles).toBeUndefined();
    expect(brief.rulesDigest).toBeUndefined();
  });

  test('strips unknown keys', () => {
    const brief = parseBrief({
      taskId: 't-1',
      profileKey: 'implement',
      prompt: 'Do it',
      extra: 'dropped',
    });

    expect(Object.keys(brief).sort()).toEqual(['profileKey', 'prompt', 'taskId']);
  });

  test('rejects empty taskId', () => {
    expect(() =>
      parseBrief({ taskId: '', profileKey: 'implement', prompt: 'Do it' }),
    ).toThrow();
  });

  test('rejects missing prompt', () => {
    expect(() => parseBrief({ taskId: 't-1', profileKey: 'implement' })).toThrow();
  });

  test('schema round-trips a valid brief', () => {
    const brief = parseBrief({ taskId: 't-1', profileKey: 'docs-writer', prompt: 'Write docs' });

    expect(briefSchema.parse(JSON.parse(JSON.stringify(brief)))).toEqual(brief);
  });
});

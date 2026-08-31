import { describe, expect, test } from 'bun:test';
import { parseRuleDoc } from './reader';

describe('parseRuleDoc', () => {
  test('parses frontmatter with name, description and scalar scope', () => {
    const rule = parseRuleDoc(
      ['---', 'name: no-any', 'description: Forbids the any type', 'scope: src/**/*.ts', '---', '', 'Use unknown instead.'].join(
        '\n',
      ),
    );

    expect(rule).toEqual({
      name: 'no-any',
      description: 'Forbids the any type',
      scope: 'src/**/*.ts',
      body: '\nUse unknown instead.',
    });
  });

  test('parses scope as a flow list', () => {
    const rule = parseRuleDoc(
      ['---', 'name: multi', 'description: desc', 'scope: [src, tests]', '---', 'body'].join('\n'),
    );

    expect(rule?.scope).toEqual(['src', 'tests']);
  });

  test('parses scope as a block list', () => {
    const rule = parseRuleDoc(
      ['---', 'name: multi', 'description: desc', 'scope:', '  - src', '  - tests', '---', 'body'].join('\n'),
    );

    expect(rule?.scope).toEqual(['src', 'tests']);
  });

  test('unquotes quoted values and keeps colons inside them', () => {
    const rule = parseRuleDoc(
      ['---', 'name: "quoted name"', "description: 'note: with colon'", '---', ''].join('\n'),
    );

    expect(rule?.name).toBe('quoted name');
    expect(rule?.description).toBe('note: with colon');
  });

  test('tolerates CRLF line endings', () => {
    const rule = parseRuleDoc(
      ['---', 'name: crlf', 'description: windows rule', '---', '', 'Body text.'].join('\r\n'),
    );

    expect(rule?.name).toBe('crlf');
    expect(rule?.body).toContain('Body text.');
  });

  test('ignores comments and unknown keys', () => {
    const rule = parseRuleDoc(
      ['---', '# a comment inside frontmatter', 'name: n', 'description: d', 'priority: high', '---', ''].join('\n'),
    );

    expect(rule?.name).toBe('n');
    expect('priority' in (rule ?? {})).toBe(false);
  });

  test('returns null when frontmatter is missing', () => {
    expect(parseRuleDoc('# Just markdown\n\nNo frontmatter here.')).toBeNull();
    expect(parseRuleDoc('')).toBeNull();
  });

  test('returns null when frontmatter is unterminated', () => {
    expect(parseRuleDoc('---\nname: broken\ndescription: no closing fence')).toBeNull();
  });

  test('returns null when frontmatter lacks a name', () => {
    expect(parseRuleDoc('---\ndescription: no name field\n---\nbody')).toBeNull();
  });

  test('returns null when name is empty', () => {
    expect(parseRuleDoc('---\nname: ""\ndescription: d\n---\nbody')).toBeNull();
  });
});

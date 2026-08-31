import { z } from 'zod';

/**
 * Reader for declarative rule documents — markdown with a YAML-ish frontmatter
 * block (`---` fences) carrying `name` / `description` / optional `scope`,
 * followed by the rule body. Used for control-plane worker rules and skills;
 * hard locks live in the worker/dev SDKs, not here.
 *
 * The frontmatter parser is a minimal scalar/list subset of YAML — enough for
 * the documented rule format, without a YAML dependency. Anything it cannot
 * interpret is left out and the zod schema decides whether the result is a
 * valid rule.
 */

const frontmatterSchema = z.object({
  name: z.string().min(1),
  description: z.string(),
  scope: z.union([z.string(), z.array(z.string())]).optional(),
});

export interface RuleDoc {
  readonly name: string;
  readonly description: string;
  readonly scope?: string | string[];
  readonly body: string;
}

/**
 * Parses a rule document. Returns `null` when the text has no frontmatter
 * block, or when the frontmatter does not carry a valid `name` / `description`
 * — listing many documents must not throw on one malformed entry.
 */
export function parseRuleDoc(text: string): RuleDoc | null {
  const extracted = extractFrontmatter(text);
  if (extracted === null) {
    return null;
  }

  const parsed = frontmatterSchema.safeParse(parseYamlish(extracted.yaml));
  if (!parsed.success) {
    return null;
  }

  return { ...parsed.data, body: extracted.body };
}

interface Frontmatter {
  readonly yaml: string;
  readonly body: string;
}

function extractFrontmatter(text: string): Frontmatter | null {
  const lines = text.split(/\r?\n/);
  if (lines[0]?.trim() !== '---') {
    return null;
  }

  const endIndex = lines.findIndex((line, index) => index > 0 && line.trim() === '---');
  if (endIndex === -1) {
    return null;
  }

  return {
    yaml: lines.slice(1, endIndex).join('\n'),
    body: lines.slice(endIndex + 1).join('\n'),
  };
}

/**
 * Parses the supported YAML subset: `key: value` scalars, flow lists
 * (`[a, b]`), block lists (`- item` under an empty value), and `#` comments.
 * Nested structures and tags are ignored.
 */
function parseYamlish(yaml: string): Record<string, string | string[]> {
  const result: Record<string, string | string[]> = {};
  const lines = yaml.split('\n');
  let index = 0;

  while (index < lines.length) {
    const line = lines[index] ?? '';
    index++;

    const trimmed = line.trim();
    if (trimmed.length === 0 || trimmed.startsWith('#')) {
      continue;
    }

    const match = /^([A-Za-z][\w.-]*)\s*:\s*(.*)$/.exec(trimmed);
    if (match === null) {
      continue;
    }

    const key = match[1] ?? '';
    const value = (match[2] ?? '').trim();

    if (value.length === 0) {
      const blockList = takeBlockListItems(lines, index);
      if (blockList.values.length > 0) {
        result[key] = blockList.values;
        index = blockList.nextIndex;
      }
      continue;
    }

    const flow = /^\[(.*)\]$/.exec(value);
    if (flow !== null) {
      result[key] = splitFlowList(flow[1] ?? '');
    } else {
      result[key] = stripQuotes(value);
    }
  }

  return result;
}

function takeBlockListItems(lines: string[], startIndex: number): { values: string[]; nextIndex: number } {
  const values: string[] = [];
  let index = startIndex;

  while (index < lines.length) {
    const itemMatch = /^\s+-\s+(.*)$/.exec(lines[index] ?? '');
    if (itemMatch === null) {
      break;
    }
    values.push(stripQuotes((itemMatch[1] ?? '').trim()));
    index++;
  }

  return { values, nextIndex: index };
}

function splitFlowList(content: string): string[] {
  return content
    .split(',')
    .map((part) => stripQuotes(part.trim()))
    .filter((part) => part.length > 0);
}

function stripQuotes(value: string): string {
  const first = value.charAt(0);
  const last = value.charAt(value.length - 1);
  if (value.length >= 2 && ((first === '"' && last === '"') || (first === "'" && last === "'"))) {
    return value.slice(1, -1);
  }
  return value;
}

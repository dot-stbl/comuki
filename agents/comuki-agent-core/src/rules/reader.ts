import { z } from "zod"

/**
 * Reader for declarative rule documents — markdown with a YAML-ish frontmatter
 * block (`---` fences) carrying `name` / `description` / optional `scope`,
 * followed by the rule body. Used for control-plane worker rules and skills;
 * hard locks live in the worker/dev SDKs, not here.
 *
 * The frontmatter parser is a minimal scalar/list/object subset of YAML — enough
 * for the documented rule format, without a YAML dependency. Anything it cannot
 * interpret is left out and the zod schema decides whether the result is a
 * valid rule.
 *
 * Task 25.1 (openspec/changes/add-mission-cowork §25.1): the parser is a
 * strict superset of the previous one — the only new shapes it accepts are
 * flow-mapped objects (e.g. `[{ kind: knowledge, id: "doc@v3" }]`) inside a
 * flow or block list. Documents without the new keys parse exactly as before.
 */

const flowObjectSchema = z.record(z.string(), z.string())

const flowListItemSchema = z.union([z.string(), flowObjectSchema])

const validateAgainstSchema = z.union([
  z.string(),
  flowListItemSchema,
  z.array(flowListItemSchema),
])

const frontmatterSchema = z.object({
  name: z.string().min(1),
  description: z.string(),
  scope: z.union([z.string(), z.array(z.string())]).optional(),
  trigger_when: z.union([z.string(), z.array(z.string())]).optional(),
  validate_against: validateAgainstSchema.optional(),
  version: z.string().default("0.1.0"),
})

export interface RuleDoc {
  readonly name: string
  readonly description: string
  readonly scope?: string | string[]
  readonly triggerWhen?: string | string[]
  readonly validateAgainst?: YamlishValidateAgainst
  readonly version: string
  readonly body: string
}

/**
 * The parsed shape of a `validate_against` frontmatter entry. Derived from
 * `validateAgainstSchema` so the explicit union and the zod inference stay
 * in lockstep — no `as` cast is needed at the assignment site because the
 * schema accepts the same shapes a RuleDoc reader (a string, a single
 * SourceRef-shaped object, or a list of either).
 */
export type YamlishValidateAgainst = z.infer<typeof validateAgainstSchema>

/**
 * Parses a rule document. Returns `null` when the text has no frontmatter
 * block, or when the frontmatter does not carry a valid `name` / `description`
 * — listing many documents must not throw on one malformed entry.
 */
export function parseRuleDoc(text: string): RuleDoc | null {
  const extracted = extractFrontmatter(text)
  if (extracted === null) {
    return null
  }

  const parsed = frontmatterSchema.safeParse(parseYamlish(extracted.yaml))
  if (!parsed.success) {
    return null
  }

  return {
    name: parsed.data.name,
    description: parsed.data.description,
    scope: parsed.data.scope,
    triggerWhen: parsed.data.trigger_when,
    validateAgainst: parsed.data.validate_against,
    version: parsed.data.version,
    body: extracted.body,
  }
}

interface Frontmatter {
  readonly yaml: string
  readonly body: string
}

function extractFrontmatter(text: string): Frontmatter | null {
  const lines = text.split(/\r?\n/)
  if (lines[0]?.trim() !== "---") {
    return null
  }

  const endIndex = lines.findIndex(
    (line, index) => index > 0 && line.trim() === "---"
  )
  if (endIndex === -1) {
    return null
  }

  return {
    yaml: lines.slice(1, endIndex).join("\n"),
    body: lines.slice(endIndex + 1).join("\n"),
  }
}

/**
 * A primitive scalar value parsed out of the frontmatter subset.
 */
type YamlishPrimitive = string

/**
 * A flow-mapped inline object — a single-level `{ key: value, key: value }`
 * inside a flow list. Deeper nesting is not part of the documented subset.
 */
type YamlishFlowObject = { readonly [key: string]: YamlishPrimitive }

/**
 * A single item of a flow or block list — either a scalar string or a
 * flow-mapped object. Block-mapped objects (lines after a `-` entry) are not
 * supported; the documented form for `validate_against` always wraps the
 * object in a flow-list (`[{ kind: ..., id: ... }]`).
 */
type YamlishListItem = YamlishPrimitive | YamlishFlowObject

/**
 * The shape of one frontmatter field: a scalar, a list of items, or a single
 * flow-mapped object. A scalar degrades to a single-item list when a list is
 * expected (plain YAML semantics), so the public type keeps the original
 * shape and the degradation happens in `List()`.
 */
type YamlishValue = YamlishPrimitive | YamlishListItem[] | YamlishFlowObject

type Yamlish = Record<string, YamlishValue>

/**
 * Parses the supported YAML subset: `key: value` scalars, flow lists
 * (`[a, b]`), flow-mapped objects in flow lists (`[{ k: v }]`), block lists
 * (`- item` under an empty value), and `#` comments. Nested structures and
 * tags are ignored.
 */
function parseYamlish(yaml: string): Yamlish {
  const result: Yamlish = {}
  const lines = yaml.split("\n")
  let index = 0

  while (index < lines.length) {
    const line = lines[index] ?? ""
    index++

    const trimmed = line.trim()
    if (trimmed.length === 0 || trimmed.startsWith("#")) {
      continue
    }

    const match = /^([A-Za-z][\w.-]*)\s*:\s*(.*)$/.exec(trimmed)
    if (match === null) {
      continue
    }

    const key = match[1] ?? ""
    const value = (match[2] ?? "").trim()

    if (value.length === 0) {
      const blockList = takeBlockListItems(lines, index)
      if (blockList.values.length > 0) {
        result[key] = blockList.values
        index = blockList.nextIndex
      }
      continue
    }

    const flow = /^\[(.*)\]$/.exec(value)
    if (flow !== null) {
      result[key] = splitFlowList(flow[1] ?? "")
    } else if (value.startsWith("{") && value.endsWith("}")) {
      const parsed = parseFlowObject(value.slice(1, -1))
      if (parsed !== null) {
        result[key] = parsed
      }
    } else {
      result[key] = stripQuotes(value)
    }
  }

  return result
}

function takeBlockListItems(
  lines: string[],
  startIndex: number
): { values: YamlishListItem[]; nextIndex: number } {
  const values: YamlishListItem[] = []
  let index = startIndex

  while (index < lines.length) {
    const raw = lines[index] ?? ""
    const itemMatch = /^\s+-\s+(.*)$/.exec(raw)
    if (itemMatch === null) {
      break
    }

    const body = (itemMatch[1] ?? "").trim()
    if (body.startsWith("{") && body.endsWith("}")) {
      const parsed = parseFlowObject(body.slice(1, -1))
      if (parsed !== null) {
        values.push(parsed)
        index++
        continue
      }
    }

    values.push(stripQuotes(body))
    index++
  }

  return { values, nextIndex: index }
}

/**
 * Parses a flow list body — `a, b, "c", { kind: knowledge }`. The comma
 * count tracks quote state, brace depth, and bracket depth, so commas
 * inside any of those do not split the list.
 */
function splitFlowList(content: string): YamlishListItem[] {
  const items: YamlishListItem[] = []
  let buffer = ""
  let inQuotes: '"' | "'" | null = null
  let braceDepth = 0
  let bracketDepth = 0

  for (let position = 0; position < content.length; position++) {
    const character = content.charAt(position)

    if (inQuotes !== null) {
      buffer += character
      if (character === inQuotes) {
        inQuotes = null
      }
      continue
    }

    if (character === '"' || character === "'") {
      inQuotes = character
      buffer += character
      continue
    }

    if (character === "{") {
      braceDepth++
    } else if (character === "}") {
      braceDepth = Math.max(0, braceDepth - 1)
    } else if (character === "[") {
      bracketDepth++
    } else if (character === "]") {
      bracketDepth = Math.max(0, bracketDepth - 1)
    }

    if (character === "," && braceDepth === 0 && bracketDepth === 0) {
      const parsed = parseFlowListItem(buffer.trim())
      if (parsed !== null) {
        items.push(parsed)
      }
      buffer = ""
      continue
    }

    buffer += character
  }

  const tail = buffer.trim()
  if (tail.length > 0) {
    const parsed = parseFlowListItem(tail)
    if (parsed !== null) {
      items.push(parsed)
    }
  }

  return items
}

function parseFlowListItem(raw: string): YamlishListItem | null {
  if (raw.length === 0) {
    return null
  }

  if (raw.startsWith("{") && raw.endsWith("}")) {
    return parseFlowObject(raw.slice(1, -1))
  }

  return stripQuotes(raw)
}

/**
 * Parses a single-level inline object body — `kind: knowledge, id: "x@v1"`.
 * Quoted values are unquoted. Missing colons or empty values yield null; the
 * caller skips the entry instead of poisoning the surrounding list.
 */
function parseFlowObject(body: string): YamlishFlowObject | null {
  const result: { [key: string]: YamlishPrimitive } = {}
  const entries = splitTopLevelCommas(body)

  for (const entry of entries) {
    const colonIndex = entry.indexOf(":")
    if (colonIndex < 0) {
      return null
    }

    const key = entry.slice(0, colonIndex).trim()
    const value = entry.slice(colonIndex + 1).trim()
    if (key.length === 0 || value.length === 0) {
      return null
    }

    result[key] = stripQuotes(value)
  }

  return Object.keys(result).length > 0 ? result : null
}

function splitTopLevelCommas(content: string): string[] {
  const parts: string[] = []
  let buffer = ""
  let inQuotes: '"' | "'" | null = null

  for (let position = 0; position < content.length; position++) {
    const character = content.charAt(position)

    if (inQuotes !== null) {
      buffer += character
      if (character === inQuotes) {
        inQuotes = null
      }
      continue
    }

    if (character === '"' || character === "'") {
      inQuotes = character
      buffer += character
      continue
    }

    if (character === ",") {
      parts.push(buffer.trim())
      buffer = ""
      continue
    }

    buffer += character
  }

  const tail = buffer.trim()
  if (tail.length > 0) {
    parts.push(tail)
  }

  return parts
}

function stripQuotes(value: string): string {
  const first = value.charAt(0)
  const last = value.charAt(value.length - 1)
  if (
    value.length >= 2 &&
    ((first === '"' && last === '"') || (first === "'" && last === "'"))
  ) {
    return value.slice(1, -1)
  }
  return value
}

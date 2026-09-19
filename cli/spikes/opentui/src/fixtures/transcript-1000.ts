/**
 * 1,000-entry transcript fixture for the focus-mode renderer.
 *
 * Mirrors the kind of mixed entries the production chat stream actually
 * produces: user text, assistant answers, tool call announcements, tool
 * output previews, status lines, code-block fences. Tool-call rows can be
 * collapsed to a one-line summary by `focusMode: true`.
 */

export type Entry =
  | { readonly kind: "user"; readonly id: number; readonly text: string }
  | { readonly kind: "assistant"; readonly id: number; readonly text: string }
  | {
      readonly kind: "tool";
      readonly id: number;
      readonly toolName: string;
      readonly summary: string;
      readonly collapsed: boolean;
      readonly outputPreview?: string;
    }
  | { readonly kind: "status"; readonly id: number; readonly text: string }
  | {
      readonly kind: "code";
      readonly id: number;
      readonly language: string;
      readonly body: string;
    }

const PHRASES = [
  "Проверь, пожалуйста, что merge-queue эскалирует на blocked run",
  "show me last 3 runs for project v2",
  "explain why claim-lease lost the row after SIGKILL",
  "/profile",
  "/kb add docs/adr-0002.md",
  "re-run integration suite but only Identity.Oidc.* tests",
  "what's the diff between c0cdacd and 5953655",
  "restart host with --detach and tail journal",
] as const

const TOOLS = [
  "Bash",
  "Read",
  "Write",
  "Grep",
  "WebFetch",
  "RunKnowledgeSearch",
  "ComputeStart",
] as const

const STATUSES = [
  "✓ done in 4.2s",
  "✗ failed — 404 from upstream",
  "→ escalated to human",
  "… thinking",
  "queue: 2 ahead",
] as const

const LANGUAGES = ["ts", "sh", "toml", "json", "md"] as const

function pick<T>(arr: readonly T[], i: number): T {
  return arr[i % arr.length] as T
}

export function buildTranscript(count = 1_000): readonly Entry[] {
  // The fixture produces **exactly** `count` final entries: each of
  // the `count` outer iterations emits exactly one entry. Every
  // 11th iteration emits a `code` entry INSTEAD of the regular user
  // / assistant / tool / status entry — net cardinality stays at
  // `count` and code blocks are still represented in the fixture.
  const out: Entry[] = []
  for (let i = 0; i < count; i++) {
    if (i % 11 === 0) {
      out.push({
        kind: "code",
        id: 1000 + i,
        language: pick(LANGUAGES, i),
        body: `// code #${i}\nconst x = ${i};\nexport { x };`,
      })
      continue
    }
    const kindRoll = i % 7
    if (kindRoll === 0 || kindRoll === 3) {
      out.push({ kind: "user", id: i, text: pick(PHRASES, i) })
    } else if (kindRoll === 1 || kindRoll === 4) {
      out.push({
        kind: "assistant",
        id: i,
        text: `Answer #${i}: ${pick(PHRASES, i + 1)} — I'll handle it via the run queue.`,
      })
    } else if (kindRoll === 2 || kindRoll === 5) {
      const collapsed = i % 3 !== 0
      out.push({
        kind: "tool",
        id: i,
        toolName: pick(TOOLS, i),
        summary: collapsed
          ? `${pick(TOOLS, i)} → ${pick(PHRASES, i + 2)}`
          : `expanded call ${i}`,
        collapsed,
        ...(collapsed ? {} : { outputPreview: pick(PHRASES, i + 3).slice(0, 64) }),
      })
    } else {
      out.push({ kind: "status", id: i, text: pick(STATUSES, i) })
    }
  }
  return out
}

/**
 * Focus-mode flattening — collapses tool calls into a single `+ tool →
 * summary` line unless `expanded === true`. Stable order preserved. Pure:
 * same fixture, same lines.
 */
export interface FlattenOptions {
  readonly focusMode: boolean
  readonly width: number
  readonly expanded: boolean
}

export function flattenTranscript(
  entries: readonly Entry[],
  options: FlattenOptions
): readonly string[] {
  const lines: string[] = []
  for (const entry of entries) {
    if (entry.kind === "tool") {
      if (options.focusMode && entry.collapsed && !options.expanded) {
        lines.push(`  + ${entry.toolName} → ${entry.summary}`.slice(0, options.width))
        continue
      }
      lines.push(`  · ${entry.toolName}  ${entry.summary}`.slice(0, options.width))
      if (!entry.collapsed && entry.outputPreview !== undefined) {
        lines.push(`    ${entry.outputPreview}`.slice(0, options.width))
      }
      continue
    }
    if (entry.kind === "user") {
      lines.push(`› ${entry.text}`.slice(0, options.width))
      continue
    }
    if (entry.kind === "assistant") {
      lines.push(`  ${entry.text}`.slice(0, options.width))
      continue
    }
    if (entry.kind === "status") {
      lines.push(`  ${entry.text}`.slice(0, options.width))
      continue
    }
    lines.push(`  [${entry.language}]`)
    for (const raw of entry.body.split("\n")) {
      lines.push(`    ${raw}`.slice(0, options.width))
    }
  }
  return lines
}
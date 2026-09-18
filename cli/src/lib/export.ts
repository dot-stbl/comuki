/**
 * Markdown export of one session transcript (`/export`). Pure: blocks
 * in, markdown out — no filesystem, no ANSI. User turns become
 * `## you` sections, assistant turns `## comuki` with their parts in
 * wire order (thinking/tool parts collapse to `>` quotes that reuse
 * the on-screen collapsed summary, code keeps its fence, a plan
 * renders as a list). Raw line blocks (notices, errors) keep their
 * place as quotes, ANSI stripped.
 */
import { collapsedSummary } from "./format"
import { stripAnsi } from "../theme"
import type { ChatBlock } from "./sessions"
import type { ChatMessageView, MessagePart, PlanItemView } from "./client"

function firstLine(text: string): string {
  return (text.split("\n", 1)[0] ?? "").trim()
}

function partQuote(segments: readonly (string | null)[]): string {
  const joined = segments
    .filter((segment): segment is string => segment !== null && segment.length > 0)
    .join(" · ")
  return `> ${joined}`
}

function planList(nodes: readonly PlanItemView[]): string {
  if (nodes.length === 0) {
    return "> plan · (empty)"
  }
  return nodes
    .map((node) => {
      const brief = firstLine(node.brief) || "(no brief)"
      const deps =
        node.dependsOn.length > 0 ? ` — depends on ${node.dependsOn.join(", ")}` : ""
      return `- **${node.profileKey || node.key}** → ${brief}${deps}`
    })
    .join("\n")
}

/** One message part → its markdown block (null drops the part). */
function partToMarkdown(part: MessagePart): string | null {
  switch (part.kind) {
    case "text": {
      const markdown = part.markdown.trim()
      return markdown.length > 0 ? markdown : null
    }
    case "thinking": {
      // Defensive only — thinking/tool are always collapsible.
      const summary = collapsedSummary(part)
      return summary === null
        ? null
        : partQuote([summary.label, ...summary.details])
    }
    case "tool": {
      const summary = collapsedSummary(part)
      return summary === null
        ? null
        : partQuote([summary.label, summary.badge, ...summary.details])
    }
    case "code": {
      const lines = [
        ...(part.path !== null && part.path !== undefined
          ? [
              `> code · ${part.path}${
                part.startLine ? `:${part.startLine}` : ""
              }`,
            ]
          : []),
        "```" + part.language,
        part.source,
        "```",
      ]
      return lines.join("\n")
    }
    case "diagram":
      return ["```" + part.dialect, part.source, "```"].join("\n")
    case "handoff":
      return `> handoff → ${part.query}`
    case "plan":
      return planList(part.nodes)
  }
}

function messageToMarkdown(message: ChatMessageView): string {
  if (message.role === "user") {
    return ["## you", "", message.content.trim()].join("\n")
  }
  if (message.role === "assistant") {
    const body =
      message.parts !== null && message.parts.length > 0
        ? message.parts
            .map(partToMarkdown)
            .filter((part): part is string => part !== null)
            .join("\n\n")
        : message.content.trim()
    return ["## comuki", "", body].join("\n")
  }
  // tool / system journal rows — quiet quotes, never sections.
  const label = message.toolName ?? message.role
  const body = message.content.trim()
  return [`> ${label}${body ? `: ${body}` : ""}`].join("\n")
}

/** Blocks → one markdown document (trailing newline; empty transcript → ""). */
export function exportMarkdown(blocks: readonly ChatBlock[]): string {
  const sections: string[] = []
  for (const block of blocks) {
    if (block.kind === "message") {
      sections.push(messageToMarkdown(block.message))
      continue
    }
    const quoted = block.lines
      .map((line) => stripAnsi(line).trim())
      .filter((line) => line.length > 0)
      .map((line) => `> ${line}`)
    if (quoted.length > 0) {
      sections.push(quoted.join("\n"))
    }
  }
  return sections.length > 0 ? sections.join("\n\n") + "\n" : ""
}

/**
 * Path-safe session slug: hostile characters collapse to dashes, empty
 * names fall back to `session`, length capped at 40.
 */
export function sessionSlug(sessionName: string): string {
  return (
    sessionName
      .replace(/[^\p{L}\p{N}_-]+/gu, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 40) || "session"
  )
}

/**
 * Default export path: `comuki-{slug}-{yyyymmdd-hhmm}.md` (local
 * time). Path-hostile characters in the session name collapse to
 * dashes; an empty slug falls back to `session`.
 */
export function exportFileName(
  sessionName: string,
  now: Date = new Date()
): string {
  const slug = sessionSlug(sessionName)
  const pad = (value: number) => String(value).padStart(2, "0")
  const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(
    now.getDate()
  )}-${pad(now.getHours())}${pad(now.getMinutes())}`
  return `comuki-${slug}-${stamp}.md`
}

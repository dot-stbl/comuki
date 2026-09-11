import type {
  ChatMessage,
  MessagePart,
  PartKind,
  ToolCall,
  ToolPart,
} from "./types"

/**
 * What a turn is made of, whichever shape it arrived in.
 *
 * Two paths reach the thread and they will both exist for as long as the
 * console has a mock: a message with an explicit `parts` list (what the wire
 * is being built to send) and today's flat message — a `kind` plus four
 * optional fields. Rather than teach the composition both, everything is
 * flattened to one ordered list here, in the model, where it can be tested
 * without a DOM.
 *
 * The ordering of a derived list is the reading order of a turn: the working
 * out first (it is the *reason*), then the call it made, then the prose, then
 * the hand-off under it. That is the order the flat composition already drew
 * them in, so nothing moves on screen.
 */

/** Every part kind, in the order the union declares them. Tests read this. */
export const PART_KINDS: readonly PartKind[] = [
  "text",
  "code",
  "diagram",
  "thinking",
  "tool",
  "handoff",
  "plan",
]

/**
 * The flat tool record, as a part.
 *
 * The two shapes are the same fact with different field names — the seed has
 * said `args`/`result` since it was written and the wire contract says
 * `inputJson`/`outputJson`. Converting here means `ToolCallCard` renders one
 * type, so the card cannot acquire a branch that only one path reaches.
 */
export function toolCallToPart(tool: ToolCall): ToolPart {
  return {
    kind: "tool",
    name: tool.name,
    inputJson: tool.args,
    status: tool.status,
    outputJson: tool.result,
  }
}

/**
 * The parts of a message.
 *
 * An explicit list wins outright: a turn that said what it is made of is not
 * second-guessed. Everything else is derived, and the derivation closes the
 * latent hole the flat composition had — `kind === "tool" && message.tool`
 * rendered *nothing at all* when a tool message carried no tool record, so the
 * thread showed an empty row and said nothing about why. Here a tool message
 * with no record falls back to whatever prose it has, and a message that turns
 * out to be made of nothing returns an empty list, which the composition
 * renders as a stated blank rather than as a hole.
 */
export function messageParts(message: ChatMessage): MessagePart[] {
  if (message.parts) {
    return message.parts
  }

  const parts: MessagePart[] = []

  if (message.kind === "tool" && message.tool) {
    parts.push(toolCallToPart(message.tool))
  }

  // Prose on any kind that carries it. The flat composition only drew text for
  // `person` and `reply`, which is why a `tool` message with no record and a
  // sentence on it read as empty. `error` keeps its own band in the
  // composition — it is chrome around the turn rather than a part of it — so
  // its text is deliberately not repeated here.
  if (message.kind !== "error" && message.text) {
    parts.push({ kind: "text", markdown: message.text })
  }

  if (message.handoff) {
    parts.push({ kind: "handoff", query: message.handoff })
  }

  return parts
}

/**
 * Whether a message has anything to draw at all.
 *
 * A proposal and an error are drawn by the composition rather than by a part,
 * so "no parts" does not mean "no message" — it means the body is empty, and
 * only then is the blank note the honest thing to render.
 */
export function hasRenderableBody(message: ChatMessage): boolean {
  return (
    messageParts(message).length > 0 ||
    message.kind === "error" ||
    (message.kind === "proposal" && Boolean(message.proposal))
  )
}

import type { ReactNode } from "react"

import type {
  ChatMessage as Message,
  MessagePart,
  PartKind,
} from "@/domains/chat/model/types"
import { CodeBlock } from "@/shared/ui"

import { ChatHandoffs } from "./chat-handoff"
import { MessageProse } from "./message-prose"
import { PlanSketch } from "./plan-sketch"
import { ThinkingBlock } from "./thinking-block"
import { ToolCallCard } from "./tool-call"

import styles from "./chat-message.module.css"

/**
 * The renderer table — one arm per part kind, and no way to forget one.
 *
 * ## What this replaces
 *
 * Four sequential `&&` conditionals inside the message composition, with no
 * exhaustiveness anywhere near them. A new kind added to the model rendered an
 * empty `<li>`: the thread showed a byline, a blank body and no clue that
 * anything was missing, and the type system said nothing at all because
 * nothing had been *removed*. That is the failure mode a chain of conditionals
 * always has — it is only ever wrong by omission, and omission is invisible.
 *
 * A `Record<PartKind, …>` inverts it. The table is indexed by the union
 * itself, so adding `question` or `decision` to `PartKind` makes **this object
 * literal** stop compiling, at the declaration, naming the kind that has no
 * arm. The check costs nothing at runtime and happens before anything ships.
 *
 * ## The one cast, and why it is contained
 *
 * TypeScript cannot narrow `PART_RENDERERS[part.kind]` and `part` together —
 * it reads the lookup as a union of functions and the argument as a union of
 * parts, and refuses to pair them up. The cast that fixes it lives in
 * `renderPart` and nowhere else, applied to a value the table itself just
 * guaranteed the type of. Every arm below is written against its own narrow
 * part type, which is the property worth keeping.
 */
type PartRenderers = {
  [K in PartKind]: (
    part: Extract<MessagePart, { kind: K }>,
    message: Message
  ) => ReactNode
}

const PART_RENDERERS: PartRenderers = {
  text: (part, message) => (
    <MessageProse markdown={part.markdown} streaming={message.streaming} />
  ),

  code: (part) => (
    <CodeBlock
      source={part.source}
      language={part.language}
      path={part.path}
      startLine={part.startLine}
    />
  ),

  /* A stub, and honest about being one — see `DiagramPart`. The source is
     shown as code because that is what it is until something draws it, and a
     dialect nobody can render is still a dialect an operator can copy into a
     tool that can. */
  diagram: (part) => (
    <div className={styles.diagram} data-test="chat-diagram">
      <p className={styles.diagramNote}>
        a {part.dialect} diagram — drawn on the screen that owns it; here it is
        the source the turn wrote
      </p>
      <CodeBlock source={part.source} language={part.dialect} />
    </div>
  ),

  thinking: (part) => <ThinkingBlock text={part.text} tokens={part.tokens} />,

  tool: (part) => <ToolCallCard tool={part} />,

  handoff: (part) => <ChatHandoffs query={part.query} />,

  plan: (part) => <PlanSketch nodes={part.nodes} edges={part.edges} />,
}

/**
 * One part, drawn.
 *
 * The single cast this file allows itself: `part.kind` selected the arm, so
 * the arm's parameter type is the part's type by construction — the compiler
 * simply has no way to say so.
 */
export function renderPart(part: MessagePart, message: Message): ReactNode {
  const render = PART_RENDERERS[part.kind] as (
    part: MessagePart,
    message: Message
  ) => ReactNode
  return render(part, message)
}

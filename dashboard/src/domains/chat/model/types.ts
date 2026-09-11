import type { Permission } from "@/shared/session"

/**
 * The chat console's vocabulary.
 *
 * The console is not an assistant bolted to the side of the product: it is the
 * same control plane the screens drive, reached by typing instead of clicking.
 * Three facts about it are settled and every type here exists to hold one of
 * them:
 *
 * 1. **It proposes; a human confirms.** A state change is a `Proposal`, never
 *    something the assistant did. There is no message kind that means "I have
 *    already stopped it".
 * 2. **Its acts land in the same journal.** A `Proposal` carries the act and
 *    the subject, so confirming it goes through the same store the duty screen
 *    writes to — see `chat.store.ts`.
 * 3. **It renders what the conversation produced and hands off what is a
 *    product surface.** Prose, code, a diagram, a question, a decision came out
 *    of this turn and are rendered here. Runs, the queue, knowledge and cost
 *    each have a screen of their own, so the console links to them: a `Handoff`
 *    is a filter on a real screen. The test is whether the thing has its own
 *    screen — if it does, link to it; if it does not, draw it. There is
 *    deliberately no "results" shape here to tempt anybody into drawing a
 *    second runs table.
 */

/** What a message is. Five kinds, and the composition renders five states. */
export type MessageKind = "person" | "reply" | "tool" | "proposal" | "error"

export type ToolStatus = "running" | "success" | "failed"

/** One call the assistant made against the Orchestration API. */
export interface ToolCall {
  /** The endpoint, in the product's own spelling. A value, set in mono. */
  name: string
  args: string
  status: ToolStatus
  /** What came back, or what went wrong. */
  result?: string
}

/* --------------------------------------------------------------------------
 * Message parts — what a turn is actually made of.
 *
 * A turn used to be a `kind` plus four optional fields, and the composition
 * read them with four sequential `&&`s. That shape says a message is *one*
 * thing, which was true while the console could only say a sentence: it cannot
 * say "here is the prose, here is the patch it is about, and here is the call
 * that produced it", which is the ordinary shape of an answer from a coding
 * agent. So a message carries an ordered list of **parts**, each of which knows
 * what it is, and the composition dispatches through a table keyed by exactly
 * this union — see `ui/message-part.tsx`. A kind with no arm in that table is a
 * compile error at the table's own declaration, which is the whole point:
 * adding a kind here and forgetting to draw it used to render an empty `<li>`
 * and TypeScript said nothing.
 *
 * The list is **frozen** and shared with the backend contract being written
 * against it. Two more are coming and are deliberately absent:
 *
 * - `question` — the console asking the operator something, with the answers
 *   as controls rather than as a sentence to type back.
 * - `decision` — a `Proposal`, moved from the message onto the part list, so a
 *   turn can propose one thing and explain two others.
 *
 * Both are P2. Adding either to `PartKind` breaks the renderer table on
 * purpose; nothing else here has to change to make room for them.
 * ----------------------------------------------------------------------- */

/** The seven things a turn can be made of today. `question` and `decision` are P2. */
export type PartKind =
  | "text"
  | "code"
  | "diagram"
  | "thinking"
  | "tool"
  | "handoff"
  | "plan"

/** Prose. Markdown, as `ChatMessage.cs` has always claimed it was. */
export interface TextPart {
  kind: "text"
  /** GitHub-flavoured markdown. Raw HTML never enters the thread. */
  markdown: string
}

/**
 * A block of code the conversation produced.
 *
 * `language` is the spelling the turn used (`ts`, `typescript`, `c#`) rather
 * than a member of a closed set: the renderer resolves it against the nine
 * grammars the highlighter registers and falls back to plain text for
 * everything else, which is the only behaviour that survives a model naming a
 * language the dashboard has never heard of.
 */
export interface CodePart {
  kind: "code"
  language: string
  source: string
  /** Where it came from, when the turn knew — rendered as `path:line`. */
  path?: string
  startLine?: number
}

/**
 * A drawing the turn produced — a sequence, a state machine, an ERD.
 *
 * `dialect` names the grammar (`mermaid`, `dot`). The renderer is a **stub**
 * this phase: it shows the source as code and says which dialect it is, rather
 * than pretending to a picture it cannot draw. Drawing it is P2, and a stub
 * that is honest about being one is better than a diagram nobody can check.
 */
export interface DiagramPart {
  kind: "diagram"
  dialect: string
  source: string
}

/**
 * The model's own working-out.
 *
 * Collapsed by default and muted, with no animation: it is evidence an
 * operator opens when an answer surprises them, not a performance of
 * thinking. `tokens` is what it cost, when the turn reported it.
 */
export interface ThinkingPart {
  kind: "thinking"
  text: string
  tokens?: number
}

/**
 * One call against the Orchestration API, as the wire will carry it.
 *
 * The flat `ToolCall` below is the same fact in the shape the seed has always
 * had; `model/parts.ts` converts one into the other, so the card renders one
 * type and the two paths cannot drift.
 */
export interface ToolPart {
  kind: "tool"
  name: string
  /** The arguments, already rendered — JSON when the call had any. */
  inputJson: string
  status: ToolStatus
  /** What came back, or what went wrong. */
  outputJson?: string
  durationMs?: number
}

/** The question handed to the screen that already answers it. */
export interface HandoffPart {
  kind: "handoff"
  query: string
}

/** One node of a plan the turn drew. */
export interface PlanNode {
  id: string
  label: string
  /** Which profile would run it, when the plan says. */
  profile?: string
}

/** One dependency between two plan nodes, named by their ids. */
export interface PlanEdge {
  from: string
  to: string
}

/**
 * A graph of work the turn laid out.
 *
 * The renderer is a **stub** this phase — the nodes in order with their
 * dependencies said in words. The run graph on `/runs/$runId` is the drawing,
 * and duplicating it here before the shapes agree is how two pictures of one
 * plan start disagreeing.
 */
export interface PlanPart {
  kind: "plan"
  nodes: PlanNode[]
  edges: PlanEdge[]
}

export type MessagePart =
  | TextPart
  | CodePart
  | DiagramPart
  | ThinkingPart
  | ToolPart
  | HandoffPart
  | PlanPart

/**
 * The act a proposal performs.
 *
 * Named after the act rather than the tool, exactly like `Permission` is, so
 * one entry gates the confirming control here and the button on the screen
 * that does the same thing without either of them agreeing on a screen name.
 */
export type ProposalAct =
  | "run.start"
  | "run.stop"
  | "plan.approve"
  | "settings.debug"

export type ProposalDecision = "confirmed" | "rejected"

/** One node of a proposed plan. */
export interface ProposalStep {
  profile: string
  label: string
}

/** A state change the assistant is offering, and a human has to press. */
export interface Proposal {
  id: string
  act: ProposalAct
  /** One line: what confirming would do. */
  summary: string
  /**
   * Where it lands.
   *
   * Never optional. Permission in this product is resolved per project and
   * there is no current project to fall back on, so a proposal that could not
   * name one could not be checked — and an unchecked proposal is the RBAC
   * bypass this console is not allowed to be. The empty string means the
   * scope was never set, which is itself a refusal to confirm.
   */
  projectId: string
  /** The identifier the act names — a run id, a plan id. */
  subject?: string
  steps?: ProposalStep[]
  /** Absent while it is still a question. */
  decision?: ProposalDecision
}

export interface ChatMessage {
  id: string
  kind: MessageKind
  /**
   * What the turn is made of, in the order it should be read.
   *
   * Absent on every message the flat path produces — the seed's own shape, and
   * every wire row until the host learns to send parts. `model/parts.ts`
   * derives a part list from the flat fields when this is missing, so one
   * renderer serves both and neither has a branch the other lacks.
   */
  parts?: MessagePart[]
  /** A person's words or the assistant's prose. Content, so not English-only. */
  text?: string
  /** Tokens are still arriving. Only ever on a `reply`. */
  streaming?: boolean
  tool?: ToolCall
  proposal?: Proposal
  /**
   * The question this message hands off, as a *query* rather than a link.
   *
   * The hrefs come from the product's own resolver at render time, so the
   * console and the command palette can never disagree about where "search
   * live runs for X" lands. See `model/references.ts`.
   */
  handoff?: string
  at: string
}

export interface ChatSession {
  id: string
  title: string
  age: string
  messages: ChatMessage[]
}

/**
 * How much of a project a command needs before it can run.
 *
 * §7 called this "a project chip", and that requirement was written when a
 * project was a global scope with a switcher in the header. It is not any more
 * — a project is a column and a filter, and `Session` has no current project at
 * all — so the chip is not a mode the conversation is in. It is an explicit
 * scope on the commands that genuinely cannot proceed without one.
 *
 * - `none` — the command acts on the conversation or on the platform. `/help`
 *   is not about a project and `/project` *is* the scope control.
 * - `implied` — the argument already names one. `/stop 2a6f1c33` says where it
 *   lands, because a run id belongs to exactly one project; asking again would
 *   produce a second answer that could disagree with the first.
 * - `required` — the command creates work and nothing in it says where. `/run`
 *   and `/init` are the whole list.
 */
export type CommandScope = "none" | "implied" | "required"

export type CommandOrigin = "built-in" | "client"

export interface SlashCommand {
  /** With the slash — the way it is typed and the way it is shown. */
  name: string
  description: string
  origin: CommandOrigin
  scope: CommandScope
  /**
   * The act the command performs, when it performs one.
   *
   * Two jobs, and they are different questions. It is what the scope chip
   * filters by — a `required` command offers only the projects where this
   * holds — and it is what a resulting proposal is checked against. A command
   * that only reads has none.
   */
  permission?: Permission
  /** Which project's git declared it. Only ever set on a client command. */
  projectId?: string
}

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
 * 3. **It hands off rather than rendering a second product.** A `Handoff` is a
 *    filter on a real screen. There is deliberately no "results" shape here to
 *    tempt anybody into drawing a second runs table.
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

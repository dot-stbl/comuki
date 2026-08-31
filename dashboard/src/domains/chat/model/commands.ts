import {
  can,
  needsLabel,
  type ProjectRef,
  type Session,
} from "@/shared/session"

import type { SlashCommand } from "./types"

/**
 * The command catalogue, and the rule about scope.
 *
 * ## Why the project chip is not what §7 asked for
 *
 * §7 says the composer carries a *project chip*, and it meant a mode: at the
 * time a project was a global scope with a switcher in the header, and the
 * chip was that switcher repeated inside the conversation. That is gone. A
 * project is a column and a filter now, `Session` has no current project, and
 * a conversation that quietly held one would be reintroducing the switcher
 * through the back door — with the extra defect that nothing else on the
 * screen would agree with it.
 *
 * So the chip is not a mode. It is an **explicit scope on the commands that
 * cannot proceed without one**, and the line is drawn by asking what the
 * command's own argument already says:
 *
 * - **`none`** — the command is not about a project. `/help` describes the
 *   console; `/status` reads across the whole swarm, the way the duty board
 *   does; `/project` *is* the scope control and cannot need one to work.
 * - **`implied`** — the argument names the project transitively. `/stop
 *   2a6f1c33` and `/plan 5b1d7e40` both carry a run id, and a run belongs to
 *   exactly one project. Asking for a chip here would produce a second answer
 *   that can disagree with the first, and the product would then have to pick
 *   one — a bug with two plausible behaviours, which is the worst kind.
 * - **`required`** — the command creates something and nothing in it says
 *   where. `/run` starts work and `/init` onboards a repository; there is no
 *   argument either could carry that would name a project, so the chip is the
 *   only place the answer can come from.
 *
 * ## What the chip offers, and what it does not decide
 *
 * A `required` command's chip lists **only the projects where this session
 * holds that command's permission**, so the operator is not walked into a
 * refusal they could have been spared. That is a courtesy, not the gate: the
 * gate is on the confirming control of the proposal that comes back, checked
 * with `can(session, permission, projectId)` exactly as every screen checks
 * it. An `implied` command proves why both are needed — the chip cannot filter
 * a project the operator never chose, so `/plan` against a run in a project
 * they only watch resolves, proposes, and is refused at the press.
 */

/**
 * The built-in set from §7, each named with the act it performs.
 *
 * Keying on acts rather than on roles is what lets one permission gate the
 * chip, the proposal and the button on the screen that does the same thing
 * without any of the three agreeing on a role list first — the same argument
 * `shared/session/permissions.ts` makes about the rail.
 */
export const BUILT_IN_COMMANDS: SlashCommand[] = [
  {
    name: "/init",
    description: "onboard a repository: git access, compute, models, knowledge",
    origin: "built-in",
    scope: "required",
    // The first step of the wizard is repo and git access, which is the
    // sources act — not a live setting and not a run. The wizard's own route
    // gates on the same key, so guessing the URL meets the forbidden state.
    permission: "sources.edit",
  },
  {
    name: "/project",
    description: "list the projects this shift can act in",
    origin: "built-in",
    scope: "none",
    permission: "projects.view",
  },
  {
    name: "/run",
    description: "start a run from a ticket",
    origin: "built-in",
    scope: "required",
    permission: "inbox.take",
  },
  {
    name: "/status",
    description: "what the swarm is doing right now",
    origin: "built-in",
    scope: "none",
    permission: "runs.view",
  },
  {
    name: "/stop",
    description: "stop a run and tear its container down",
    origin: "built-in",
    scope: "implied",
    permission: "runs.stop",
  },
  {
    name: "/plan",
    description: "read a run's plan, and decide on it",
    origin: "built-in",
    scope: "implied",
    permission: "plans.approve",
  },
  {
    name: "/debug",
    description: "turn verbose worker logging on for an hour",
    origin: "built-in",
    scope: "required",
    permission: "settings.live",
  },
  {
    name: "/help",
    description: "what the console can do",
    origin: "built-in",
    scope: "none",
  },
]

/**
 * Every command the composer offers.
 *
 * The built-in set, plus whatever the client's git declared — which arrives as
 * data and is never hardcoded here. Custom commands are narrowed to the
 * projects this session can see at all, the same boundary every list in the
 * product uses, and no further. Whether the shift may actually *run* one is
 * answered by the proposal it produces, not by hiding the command: a menu is
 * what teaches an operator that the command exists, and the person who cannot
 * run it is often exactly the one asked what it does.
 */
export function availableCommands(
  session: Session,
  custom: SlashCommand[]
): SlashCommand[] {
  const visible = new Set(session.projects.map((project) => project.id))
  return [
    ...BUILT_IN_COMMANDS,
    ...custom.filter(
      (entry) => !entry.projectId || visible.has(entry.projectId)
    ),
  ]
}

/** The first token, when it is a command. `/stop 2a6f1c33` → `/stop`. */
export function commandOf(
  text: string,
  commands: SlashCommand[]
): SlashCommand | null {
  const head = text.trim().split(/\s+/)[0]?.toLowerCase() ?? ""
  if (!head.startsWith("/")) {
    return null
  }
  return commands.find((entry) => entry.name === head) ?? null
}

/**
 * Whether the composer's menu is open, and on what.
 *
 * Open while the *first* token is being typed and is a slash word: `/ru` is
 * still choosing a command, `/run PLEX-14` has already chosen one. Anything
 * with whitespace in it is an argument, and a menu over an argument is a menu
 * in the way.
 */
const COMMAND_HEAD = /^\/\S*$/

export function commandMenuQuery(text: string): string | null {
  return COMMAND_HEAD.test(text) ? text.toLowerCase() : null
}

/** Commands whose name starts with what has been typed so far. */
export function matchCommands(
  query: string,
  commands: SlashCommand[]
): SlashCommand[] {
  return commands.filter((entry) => entry.name.startsWith(query))
}

/**
 * The projects a `required` command may be scoped to.
 *
 * Filtered by the command's own permission, asked per project — which is the
 * only way to ask it, because the same person is `approver` on one project and
 * `viewer` on the next. A command with no permission at all is a read, and a
 * read is not scoped.
 */
export function scopeChoices(
  session: Session,
  command: SlashCommand | null
): ProjectRef[] {
  if (!command || command.scope !== "required" || !command.permission) {
    return []
  }
  const permission = command.permission
  return session.projects.filter((project) =>
    can(session, permission, project.id)
  )
}

export interface ScopeState {
  /** Whether the chip is shown at all. */
  needed: boolean
  /** The command the chip is scoping — the chip says so out loud. */
  command: SlashCommand | null
  /** The projects on offer, already filtered by the command's permission. */
  choices: ProjectRef[]
  /**
   * Nothing has been chosen yet. An *incomplete* command, not a refused one —
   * the send control takes `disabled` for this, the way it would for an empty
   * box, because there is nothing here to explain and nothing to ask for.
   */
  incomplete: string | null
  /**
   * There is no project this session could choose. A permission fact, and it
   * takes `denied` rather than `disabled`: the control stays in the tab order
   * and says which role would open it.
   */
  denied: string | null
}

/** What the composer should show beside the textarea, and whether it may send. */
export function scopeState(
  session: Session,
  command: SlashCommand | null,
  projectId: string
): ScopeState {
  if (!command || command.scope !== "required") {
    return {
      needed: false,
      command,
      choices: [],
      incomplete: null,
      denied: null,
    }
  }

  const choices = scopeChoices(session, command)
  if (choices.length === 0) {
    return {
      needed: true,
      command,
      choices,
      incomplete: null,
      // The same sentence every refused control in this product says, from the
      // same function — asked without a project, because the fact is that
      // there is no project where it would hold.
      denied: command.permission ? needsLabel(command.permission) : "not available",
    }
  }

  const chosen = choices.some((project) => project.id === projectId)
  return {
    needed: true,
    command,
    choices,
    incomplete: chosen ? null : `${command.name} needs a project`,
    denied: null,
  }
}

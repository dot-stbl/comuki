import type {
  VerifyCommand,
  VerifyResult,
  VerifySource,
} from "@/domains/verify/model/types"

/**
 * Readings the gate's own arithmetic produces, kept out of the components so
 * the section, its heading and its tests all read the same numbers.
 */

/** The commands declared for one project, in the order the file declares them. */
export function commandsFor(
  commands: VerifyCommand[],
  projectId: string
): VerifyCommand[] {
  return commands.filter((command) => command.projectId === projectId)
}

/** How many of a project's commands last came back failing. */
export function failingCount(commands: VerifyCommand[]): number {
  return commands.filter((command) => command.last?.outcome === "failed").length
}

/**
 * How many have never run at all.
 *
 * Counted separately from failures on purpose: a command nothing has reached is
 * a hole in the gate's coverage, not a broken build, and rolling the two into
 * one "not green" figure would hide the difference the screen exists to show.
 */
export function neverRanCount(commands: VerifyCommand[]): number {
  return commands.filter((command) => command.last === null).length
}

/**
 * Where a project's commands live, as one line an operator can act on.
 *
 * The repo, the ref and the path, in that order, because that is the order they
 * narrow. This is the sentence the screen exists to say — editing a command is
 * a commit over there, so the screen's job is to name "there" precisely enough
 * that nobody has to go looking.
 */
export function sourceLocation(source: VerifySource): string {
  return `${source.repo} @ ${source.ref} · ${source.path}`
}

/** A result's own one-line reading, for a cell that has to say it in one line. */
export function resultLabel(result: VerifyResult | null): string {
  if (!result) {
    return "never ran"
  }
  return result.outcome === "failed" ? "failed" : "passed"
}

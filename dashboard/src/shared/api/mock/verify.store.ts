import { VERIFY_SEED, type SeedVerifySnapshot } from "./verify.seed"

/**
 * Mutable mock store for the verification gate.
 *
 * There is exactly one thing on this screen a person can change — whether the
 * gate is on for a project — and it still needs a store: a query whose
 * `queryFn` maps a module constant restores that constant on the refetch that
 * follows the mutation, and the switch flips back about 200 ms later. Same
 * pattern as `runs.store.ts` and `compute.store.ts`.
 *
 * The commands are **not** mutable here, and that is the product's rule rather
 * than an unfinished store: a command is a line in the client's git, so the
 * only way to change one is a commit in their repository. There is no
 * `updateSeedVerifyCommand` for the same reason there is no editor on the
 * screen.
 *
 * Session-scoped and in-memory by design: a reload is a fresh shift.
 */

function clone(snapshot: SeedVerifySnapshot): SeedVerifySnapshot {
  return {
    projects: snapshot.projects.map((project) => ({
      ...project,
      source: { ...project.source },
    })),
    commands: snapshot.commands.map((command) => ({
      ...command,
      last: command.last ? { ...command.last } : null,
    })),
  }
}

let state: SeedVerifySnapshot = clone(VERIFY_SEED)

export function readSeedVerify(): SeedVerifySnapshot {
  return state
}

/** Turn the gate on or off for one project. The commands do not move. */
export function setSeedVerifyEnabled(projectId: string, enabled: boolean): void {
  state = {
    ...state,
    projects: state.projects.map((project) =>
      project.projectId === projectId ? { ...project, enabled } : project
    ),
  }
}

/** Back to the seeded gate — used by tests and stories. */
export function resetSeedVerify(): void {
  state = clone(VERIFY_SEED)
}

/**
 * The verification gate — the client's own checks, declared in the client's own
 * git.
 *
 * Fictional, like every other seed in this folder: no repo, ref, path, command,
 * duration or run id below came from a real client. The set is chosen so the
 * three states that actually matter are all reachable without a backend — a
 * check that has never run, a check that is failing, and a project whose git
 * declares nothing at all.
 *
 * The shape follows the FE requirements (§10 Verify):
 *
 *   - A **feature flag** per project. Off means the gate does not run; the
 *     commands are still declared, because a file in git does not stop existing
 *     when a switch here is flipped.
 *   - The command list is **read-only, by design and not by omission**. Editing
 *     a command means committing to the client's repository — that is the
 *     product's GitOps rule. So every project carries `source`: the repo, the
 *     ref and the path the commands were read from, and a URL to open it. A
 *     screen that showed a greyed-out editor would be describing a feature the
 *     product deliberately does not have.
 *   - **Last result per command**, carrying the id of the run that produced it
 *     so the row deep-links to it.
 *
 * Project ids are the ones `session.seed.ts` hands the shift.
 */

/** A verification result is pass or fail. It is not a run status. */
export type SeedVerifyOutcome = "success" | "failed"

export interface SeedVerifyResult {
  outcome: SeedVerifyOutcome
  /** The run this result came out of — the row's deep link. */
  runId: string
  /** When it ran, as the operator would say it. Fictional. */
  at: string
  durationSec: number
  /**
   * The first useful line of output. Present on a failure, because a red cell
   * with nothing behind it sends the operator to the run to find out what a
   * sentence could have told them here.
   */
  detail?: string
}

export interface SeedVerifyCommand {
  id: string
  projectId: string
  /** The file in the client's repo this was declared in, repo-relative. */
  path: string
  /** The name the client gave the check. */
  name: string
  /** The command line, verbatim, as it will be run in the container. */
  command: string
  /**
   * `null` when it has never run — a command committed to git that no run has
   * reached yet. Distinct from a failure, and it has to look distinct: "never
   * ran" is a fact about the gate's coverage, "failed" is a fact about the code.
   */
  last: SeedVerifyResult | null
}

export interface SeedVerifySource {
  /** The client's repository, as they would type it. */
  repo: string
  /** The ref the commands were read at. */
  ref: string
  /** Where in that repo they live. */
  path: string
  /** Where the operator goes to change one. The whole point of the screen. */
  url: string
}

export interface SeedVerifyProject {
  projectId: string
  enabled: boolean
  source: SeedVerifySource
  /** When the declarations were last read out of git. */
  readAt: string
}

export interface SeedVerifySnapshot {
  projects: SeedVerifyProject[]
  commands: SeedVerifyCommand[]
}

const PROJECTS: SeedVerifyProject[] = [
  {
    projectId: "p_comuki",
    enabled: true,
    source: {
      repo: "comuki/web-app",
      ref: "main",
      path: ".comuki/verify.yaml",
      url: "https://github.com/comuki/web-app/blob/main/.comuki/verify.yaml",
    },
    readAt: "6 min ago",
  },
  {
    // The gate is off. The commands below are still declared — a switch here
    // does not delete a file over there — so they are listed, and the section
    // says plainly that nothing is running them.
    projectId: "p_plexor",
    enabled: false,
    source: {
      repo: "plexor/identity-svc",
      ref: "trunk",
      path: "ops/comuki-verify.yaml",
      url: "https://git.plexor.internal/plexor/identity-svc/-/blob/trunk/ops/comuki-verify.yaml",
    },
    readAt: "2 days ago",
  },
  {
    // Enabled, and declaring nothing. The empty state has to name the file the
    // client is expected to create, or it is just an empty box.
    projectId: "p_atlas",
    enabled: true,
    source: {
      repo: "atlas/checkout-web",
      ref: "main",
      path: ".comuki/verify.yaml",
      url: "https://github.com/atlas/checkout-web/blob/main/.comuki/verify.yaml",
    },
    readAt: "31 min ago",
  },
]

const COMMANDS: SeedVerifyCommand[] = [
  {
    id: "vc_comuki_types",
    projectId: "p_comuki",
    path: ".comuki/verify.yaml",
    name: "types",
    command: "bun run typecheck",
    last: {
      outcome: "success",
      runId: "b3d8a402",
      at: "11 min ago",
      durationSec: 34,
    },
  },
  {
    id: "vc_comuki_unit",
    projectId: "p_comuki",
    path: ".comuki/verify.yaml",
    name: "unit",
    command: "bun run test -- --reporter=dot",
    last: {
      outcome: "failed",
      runId: "5b1d7e40",
      at: "11 min ago",
      durationSec: 128,
      detail:
        "2 failed in src/domains/runs/model/profile-flow.test.ts — expected 7 columns, received 6",
    },
  },
  {
    id: "vc_comuki_lint",
    projectId: "p_comuki",
    path: ".comuki/verify.yaml",
    name: "lint",
    command: "bun run lint --max-warnings=0",
    last: {
      outcome: "success",
      runId: "5b1d7e40",
      at: "11 min ago",
      durationSec: 19,
    },
  },
  {
    // Committed a fortnight ago; no run has reached it. The gate stops at the
    // first failure, and `unit` above is where every run has been stopping.
    id: "vc_comuki_visual",
    projectId: "p_comuki",
    path: ".comuki/verify.yaml",
    name: "visual baseline",
    command: "bun run test:visual -- --update=false",
    last: null,
  },
  {
    id: "vc_plexor_build",
    projectId: "p_plexor",
    path: "ops/comuki-verify.yaml",
    name: "build",
    command: "./gradlew assemble --no-daemon",
    last: {
      outcome: "success",
      runId: "9c41b7d2",
      at: "12 aug",
      durationSec: 212,
    },
  },
  {
    id: "vc_plexor_contract",
    projectId: "p_plexor",
    path: "ops/comuki-verify.yaml",
    name: "contract",
    command: "./gradlew pactVerify --no-daemon",
    last: {
      outcome: "success",
      runId: "9c41b7d2",
      at: "12 aug",
      durationSec: 96,
    },
  },
]

export const VERIFY_SEED: SeedVerifySnapshot = {
  projects: PROJECTS,
  commands: COMMANDS,
}

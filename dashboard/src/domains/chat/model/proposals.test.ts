import { describe, expect, it } from "vitest"

import {
  PROPOSAL_PERMISSION,
  isPending,
  proposalCheck,
} from "@/domains/chat/model/proposals"
import type { Proposal, ProposalAct } from "@/domains/chat/model/types"
import { PROJECTS_SEED, SESSION_USER_SEED } from "@/shared/api/mock"
import { roleGrants, type Role, type Session } from "@/shared/session"

/**
 * Chat is not an RBAC bypass.
 *
 * The console's proposals answer to the product's own permission matrix, per
 * project, through the same `can()` every screen calls. These cases hold that
 * from both ends: the mapping is complete, and the seeded shift — which
 * approves on one project, administers a second and only watches the third —
 * gets three different answers to the same question.
 */

function proposal(act: ProposalAct, projectId: string): Proposal {
  return {
    id: `cp_${act}`,
    act,
    summary: "do the thing",
    projectId,
    subject: "5b1d7e40",
  }
}

/** The seeded shift, exactly as the mock hands it over. */
const shift: Session = {
  user: SESSION_USER_SEED,
  projects: PROJECTS_SEED,
}

function session(
  platformRoles: Role[],
  projectRoles: Record<string, Role[]> = {}
): Session {
  return {
    user: { ...SESSION_USER_SEED, platformRoles, projectRoles },
    projects: PROJECTS_SEED,
  }
}

describe("every act names a permission", () => {
  it("covers all four, and none of them is a chat-only key", () => {
    const acts: ProposalAct[] = [
      "run.start",
      "run.stop",
      "plan.approve",
      "settings.debug",
    ]
    for (const act of acts) {
      const permission = PROPOSAL_PERMISSION[act]
      expect(permission).toBeDefined()
      // The permission exists in the product's own matrix — asserted by
      // asking a role about it rather than by comparing to a second list.
      expect(typeof roleGrants("platform-admin", permission)).toBe("boolean")
    }
  })
})

describe("the seeded shift, asked the same question three times", () => {
  it("may approve a plan on the project it approves on", () => {
    expect(proposalCheck(shift, proposal("plan.approve", "p_comuki"))).toEqual({
      allowed: true,
      denial: null,
    })
  })

  it("may approve a plan on the project it administers", () => {
    expect(
      proposalCheck(shift, proposal("plan.approve", "p_atlas")).allowed
    ).toBe(true)
  })

  it("may not approve a plan on the project it only watches, and says so", () => {
    // `operator` on the platform carries `runs.stop` everywhere, so a stop
    // here would prove nothing. Approving is a project judgement, and this is
    // the act the shift genuinely does not hold on `p_plexor`.
    expect(proposalCheck(shift, proposal("plan.approve", "p_plexor"))).toEqual({
      allowed: false,
      denial: "needs approver, project-admin or platform-admin on plexor",
    })
  })

  it("may still stop a run there, because platform ops does carry that", () => {
    expect(proposalCheck(shift, proposal("run.stop", "p_plexor")).allowed).toBe(
      true
    )
  })
})

describe("a proposal with nowhere to land", () => {
  it("is refused rather than allowed", () => {
    // The safe direction and the true one: a state change with no project
    // cannot be authorised, and defaulting the other way would make the empty
    // string a bypass.
    const orphan = proposal("run.start", "")
    expect(proposalCheck(session(["platform-admin"]), orphan)).toEqual({
      allowed: false,
      denial: "needs a project to act in",
    })
  })
})

describe("a decided proposal is history", () => {
  it("stops being a question once it has an answer", () => {
    const decided: Proposal = {
      ...proposal("run.stop", "p_comuki"),
      decision: "confirmed",
    }
    expect(isPending(proposal("run.stop", "p_comuki"))).toBe(true)
    expect(isPending(decided)).toBe(false)
  })
})

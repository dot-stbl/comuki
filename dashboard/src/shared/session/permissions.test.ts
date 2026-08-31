import { describe, expect, it } from "vitest"

import {
  ROLES,
  needsLabel,
  permissionScope,
  roleGrants,
  rolesGranting,
  type Permission,
} from "./permissions"

describe("the role matrix", () => {
  it("gives a viewer sight of runs and nothing else", () => {
    expect(roleGrants("viewer", "runs.view")).toBe(true)
    expect(roleGrants("viewer", "runs.stop")).toBe(false)
    expect(roleGrants("viewer", "plans.approve")).toBe(false)
  })

  it("lets a member act on a run without approving a plan", () => {
    expect(roleGrants("member", "runs.stop")).toBe(true)
    expect(roleGrants("member", "plans.approve")).toBe(false)
  })

  // The one branch in a table that otherwise reads as a ladder. Operator is
  // platform ops: it stops runs and turns live settings, and approving a plan
  // stays a project judgement it has to be assigned.
  it("keeps plan approval away from an operator", () => {
    expect(roleGrants("operator", "runs.stop")).toBe(true)
    expect(roleGrants("operator", "settings.live")).toBe(true)
    expect(roleGrants("operator", "projects.create")).toBe(true)
    expect(roleGrants("operator", "plans.approve")).toBe(false)
    expect(roleGrants("operator", "sources.edit")).toBe(false)
  })

  it("keeps identity to the platform admin alone", () => {
    expect(rolesGranting("identity.manage")).toEqual(["platform-admin"])
  })

  it("gives the platform admin every act", () => {
    for (const permission of ROLES.flatMap((role) =>
      (["runs.stop", "plans.approve", "identity.manage"] as Permission[]).filter(
        (entry) => roleGrants(role, entry)
      )
    )) {
      expect(roleGrants("platform-admin", permission)).toBe(true)
    }
  })

  it("scopes identity, projects and observability outside a project", () => {
    expect(permissionScope("identity.manage")).toBe("platform")
    expect(permissionScope("projects.create")).toBe("platform")
    expect(permissionScope("observability.view")).toBe("platform")
    expect(permissionScope("runs.stop")).toBe("project")
  })

  it("names the roles that would open a denied act", () => {
    expect(needsLabel("plans.approve")).toBe(
      "needs approver, project-admin or platform-admin"
    )
    expect(needsLabel("identity.manage")).toBe("needs platform-admin")
  })
})

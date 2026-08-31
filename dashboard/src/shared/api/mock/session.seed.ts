import type { ProjectRef, SessionUser } from "@/shared/session"

/**
 * The signed-in shift, until the backend issues a real session.
 *
 * Fictional, like every other seed in this folder. The roles are arranged so
 * that the product's own access rule is visible while clicking around rather
 * than only provable in a test: the same person approves plans on one project,
 * administers a second and can only watch the third, so the duty list shows
 * rows whose Approve works directly above rows whose Approve explains itself.
 *
 * `operator` on the platform is what opens the rail's lower tier — Projects,
 * Compute, Models, Observability. It deliberately stops short of
 * `platform-admin`: granting that would open Identity too and, in doing so,
 * grant every act on every project, which would flatten the demonstration
 * above into a screen where nothing is ever refused.
 *
 * **To look at Identity, make this `["platform-admin"]`** — one line, and the
 * only cost is that nothing is denied anywhere while it is set.
 */
export const SESSION_USER_SEED: SessionUser = {
  id: "u_duty",
  name: "Duty Engineer",
  email: "duty@comuki.local",
  platformRoles: ["operator"],
  projectRoles: {
    p_comuki: ["approver"],
    p_plexor: ["viewer"],
    p_atlas: ["project-admin"],
  },
}

export const PROJECTS_SEED: ProjectRef[] = [
  { id: "p_comuki", key: "comuki", name: "Comuki platform" },
  { id: "p_plexor", key: "plexor", name: "Plexor" },
  { id: "p_atlas", key: "atlas", name: "Atlas" },
]

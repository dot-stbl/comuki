/**
 * The platform's project registry, until the backend keeps one.
 *
 * Fictional, like every other seed in this folder — the slugs, the repositories
 * and the dates are invented and nothing downstream should mistake them for a
 * tenant's real estate.
 *
 * The three ids `session.seed.ts` hands the shift are reused verbatim, because
 * a product that disagrees with itself about which projects exist is worse than
 * one with no projects at all: the runs list, the role scopes and this registry
 * all name `p_comuki`, `p_plexor` and `p_atlas` and mean the same three things.
 * `p_vega` is the fourth on purpose — a project created and not yet used, which
 * is what every project looks like on its first day and the case a row that
 * assumes runs and spend renders as a broken cell.
 */

export interface SeedProject {
  id: string
  /**
   * The handle. It is what appears as a column in every other list in the
   * product, so it is a value rather than a name: lowercase, no spaces, and
   * stable once created.
   */
  slug: string
  /** Prose. The only field on a project written for a reader. */
  name: string
  /**
   * Where this project's worker profiles live — prompt, skills and tools as
   * git, which is how a profile is authored. Optional: a project without one
   * runs on the platform's own defaults until somebody points it at a repo.
   */
  gitProfileRepo: string | null
  /** ISO day. Dates are values and read in the data voice. */
  createdAt: string
}

export const PLATFORM_PROJECTS_SEED: SeedProject[] = [
  {
    id: "p_comuki",
    slug: "comuki",
    name: "Comuki platform",
    gitProfileRepo: "git@github.com:comuki/worker-profiles.git",
    createdAt: "2026-03-04",
  },
  {
    id: "p_plexor",
    slug: "plexor",
    name: "Plexor",
    gitProfileRepo: "git@gitlab.com:plexor/agent-profiles.git",
    createdAt: "2026-05-19",
  },
  {
    // Running a full swarm on the platform defaults — the repository is
    // genuinely absent rather than pending, and the row has to say so.
    id: "p_atlas",
    slug: "atlas",
    name: "Atlas",
    gitProfileRepo: null,
    createdAt: "2026-06-27",
  },
  {
    // Two days old: no runs, no spend, no repository. Every derived column on
    // this row degrades, which is the point of seeding it.
    id: "p_vega",
    slug: "vega",
    name: "Vega",
    gitProfileRepo: null,
    createdAt: "2026-08-28",
  },
]

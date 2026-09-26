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
  readonly id: string
  /**
   * The handle. It is what appears as a column in every other list in the
   * product, so it is a value rather than a name: lowercase, no spaces, and
   * stable once created.
   */
  readonly slug: string
  /** Prose. The only field on a project written for a reader. */
  readonly name: string
  /**
   * Where this project's worker profiles live — prompt, skills and tools as
   * git, which is how a profile is authored. Optional: a project without one
   * runs on the platform's own defaults until somebody points it at a repo.
   */
  readonly gitProfileRepo: string | null
  /** ISO day. Dates are values and read in the data voice. */
  readonly createdAt: string
  /**
   * The identity fields, shaped exactly as the wire serves them (design D9 —
   * mock and real modes stay shape-identical). Optional on the seed so a
   * bare project spells its absence rather than its default.
   */
  readonly icon?: string | null
  readonly color?: string | null
  readonly tags?: readonly string[]
}

export const PLATFORM_PROJECTS_SEED: SeedProject[] = [
  {
    id: "p_comuki",
    slug: "comuki",
    name: "Comuki platform",
    gitProfileRepo: "git@github.com:comuki/worker-profiles.git",
    createdAt: "2026-03-04",
    // The stored override: an emoji mark, an accent, a vocabulary. This is
    // the row the identity fields exist for — everything on it renders.
    icon: "🛰️",
    color: "#3c5a86",
    tags: ["platform", "orchestration"],
  },
  {
    id: "p_plexor",
    slug: "plexor",
    name: "Plexor",
    gitProfileRepo: "git@gitlab.com:plexor/agent-profiles.git",
    createdAt: "2026-05-19",
    // GitLab-hosted and deliberately bare: the mark derives from the host,
    // the dot and chips fall back to muted ink. Derivation is a real
    // rendering path, not a corner case, and this row exercises it.
  },
  {
    // Running a full swarm on the platform defaults — the repository is
    // genuinely absent rather than pending, and the row has to say so.
    id: "p_atlas",
    slug: "atlas",
    name: "Atlas",
    gitProfileRepo: null,
    createdAt: "2026-06-27",
    // Colour and tags without an icon: the neutral glyph wears the accent,
    // proving the two identity channels compose rather than substitute.
    color: "#6d5b4b",
    tags: ["billing", "web"],
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

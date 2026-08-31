import { PLATFORM_PROJECTS_SEED, type SeedProject } from "./projects.seed"

/**
 * Mutable mock store for the project registry.
 *
 * Same reason `runs.store.ts` exists: the seed is a module constant, and a
 * query whose `queryFn` maps a constant silently reverts an optimistic write on
 * the next refetch — the project appears, then vanishes about two hundred
 * milliseconds later, which reads as a bug in the form rather than in the mock.
 * This holds the registry for the session so creating a project sticks.
 *
 * Session-scoped and in-memory by design: a reload is a fresh platform.
 */

let projects: SeedProject[] = PLATFORM_PROJECTS_SEED.map((entry) => ({
  ...entry,
}))

export function listSeedProjects(): SeedProject[] {
  return projects
}

export function findSeedProjectBySlug(slug: string): SeedProject | undefined {
  return projects.find((entry) => entry.slug === slug)
}

export interface CreateSeedProjectInput {
  name: string
  slug: string
  gitProfileRepo: string | null
}

/**
 * Creates a project and returns it.
 *
 * The id is derived from the slug rather than from a counter, because the slug
 * is the thing the operator chose and the thing every other list will show —
 * an id that agreed with it in the seeds and stopped agreeing the moment a
 * project was created would be a difference with no meaning behind it.
 */
export function createSeedProject(input: CreateSeedProjectInput): SeedProject {
  const created: SeedProject = {
    id: `p_${input.slug.replace(/-/g, "_")}`,
    slug: input.slug,
    name: input.name,
    gitProfileRepo: input.gitProfileRepo,
    createdAt: new Date().toISOString().slice(0, 10),
  }
  projects = [...projects, created]
  return created
}

/** Back to the seeded platform — used by tests and stories. */
export function resetSeedProjects(): void {
  projects = PLATFORM_PROJECTS_SEED.map((entry) => ({ ...entry }))
}

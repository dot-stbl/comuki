/**
 * Pi-extension registering materialized skills with pi.
 *
 * Spec (openspec/changes/harden-pi-worker-sandbox §6.2): skills materialized
 * under the working directory SHALL be registered with pi through this
 * worker-SDK extension so the agent can invoke them. A directory of
 * `SKILL.md` files WITHOUT the extension does not count — `listSkills`
 * alone is not enough. Once `prepare` has copied a valid skill into the
 * skills root and this extension is loaded, the agent can invoke it.
 *
 * Implementation note: pi exposes a `resources_discover` event whose result
 * can carry `skillPaths`. Pi's resource loader then resolves each path
 * (file or directory) into a `Skill` for the model — that is the seam
 * this extension uses.
 */
import { join } from "node:path"
import { listSkills } from "../skills/loader.js"
import type {
  ExtensionFactory,
  PiExtensionApi,
  PiResourcesDiscoverEvent,
  PiResourcesDiscoverResult,
} from "./api"

export interface SkillsExtensionOptions {
  /** Root directory containing the materialized `<skill>/SKILL.md` pairs. */
  readonly skillsRoot: string
}

export function createSkillsExtension(
  options: SkillsExtensionOptions
): ExtensionFactory {
  const skillsRoot = options.skillsRoot

  return async function skillsExtension(
    api: PiExtensionApi
  ): Promise<void> {
    api.on("resources_discover", (event) =>
      discoverSkills(event, skillsRoot)
    )
  }
}

async function discoverSkills(
  _event: PiResourcesDiscoverEvent,
  skillsRoot: string
): Promise<PiResourcesDiscoverResult> {
  // The skills root may not exist yet (fresh checkout, first claim) — the
  // extension is a no-op then, matching the loader's "broken skill is
  // skipped" rule: a missing root hides zero skills because there were
  // none to hide.
  const skills = await listSkills(skillsRoot).catch((error: unknown) => {
    if (isMissingDirError(error)) {
      return []
    }
    throw error
  })
  return {
    skillPaths: skills.map((skill) => join(skillsRoot, skill.dirName)),
  }
}

function isMissingDirError(error: unknown): boolean {
  if (typeof error !== "object" || error === null) {
    return false
  }
  return errorCode(error) === "ENOENT"
}

function errorCode(error: object): unknown {
  return Object.getOwnPropertyDescriptor(error, "code")?.value
}
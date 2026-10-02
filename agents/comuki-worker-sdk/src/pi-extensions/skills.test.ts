import { afterAll, beforeAll, describe, expect, test } from "bun:test"
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { createSkillsExtension } from "./skills"
import type {
  ExtensionFactory,
  PiExtensionApi,
  PiResourcesDiscoverEvent,
  PiResourcesDiscoverResult,
} from "./api"

let skillsRoot: string

beforeAll(async () => {
  skillsRoot = await mkdtemp(join(tmpdir(), "comuki-pi-skills-"))

  await mkdir(join(skillsRoot, "git-workflow"))
  await writeFile(
    join(skillsRoot, "git-workflow", "SKILL.md"),
    [
      "---",
      "name: git-workflow",
      "description: Safe branch and commit flow",
      "---",
      "",
      "Branch first.",
    ].join("\n"),
    "utf8"
  )

  await mkdir(join(skillsRoot, "deploy"))
  await writeFile(
    join(skillsRoot, "deploy", "SKILL.md"),
    [
      "---",
      "name: deploy-runbook",
      "description: How to ship a worker image",
      "---",
      "",
      "Build, tag, push.",
    ].join("\n"),
    "utf8"
  )

  await mkdir(join(skillsRoot, "broken"))
  await writeFile(
    join(skillsRoot, "broken", "SKILL.md"),
    "No frontmatter at all.\n",
    "utf8"
  )
})

afterAll(async () => {
  await rm(skillsRoot, { recursive: true, force: true })
})

type ResourcesDiscoverHandler = (
  event: PiResourcesDiscoverEvent,
  ctx: { hasUI: boolean }
) => Promise<PiResourcesDiscoverResult | undefined>

interface Harness {
  readonly api: PiExtensionApi
  readonly trigger: () => Promise<PiResourcesDiscoverResult | undefined>
}

function makeHarness(): Harness {
  let handler: ResourcesDiscoverHandler | undefined

  const api: PiExtensionApi = {
    on(event, h) {
      if (event === "resources_discover") {
        handler = h as ResourcesDiscoverHandler
        return () => {
          handler = undefined
        }
      }
      // tool_call is never subscribed by the skills extension.
      void h
      void event
      return () => undefined
    },
    registerMcpServer(name, config) {
      void name
      void config
    },
  }

  const trigger = async (): Promise<PiResourcesDiscoverResult | undefined> => {
    if (handler === undefined) {
      throw new Error("skills extension did not register a resources_discover handler")
    }
    return handler({ cwd: "/work", reason: "startup" }, { hasUI: false })
  }

  return { api, trigger }
}

function runSkills(): Promise<PiResourcesDiscoverResult | undefined> {
  const factory: ExtensionFactory = createSkillsExtension({ skillsRoot })
  const { api, trigger } = makeHarness()
  factory(api)
  return trigger()
}

describe("createSkillsExtension", () => {
  test("registers a resources_discover handler on the pi api", () => {
    let registered = 0
    const api: PiExtensionApi = {
      on(event, handler) {
        if (event === "resources_discover") {
          registered++
        }
        void handler
        return () => undefined
      },
      registerMcpServer(name, config) {
        void name
        void config
      },
    }

    createSkillsExtension({ skillsRoot })(api)

    expect(registered).toBe(1)
  })

  test("returns one skillPath per valid skill, omitting broken ones", async () => {
    const result = await runSkills()

    expect(result?.skillPaths).toBeDefined()
    const names = (result?.skillPaths ?? []).map((p) => p.split(/[\\/]/).pop() ?? "")
    // Sorted by skill name in listSkills, but path order mirrors that sort.
    expect(names).toEqual(["deploy", "git-workflow"])
  })

  test("skill paths point at each materialized skill directory", async () => {
    const result = await runSkills()

    const paths = result?.skillPaths ?? []
    expect(paths).toContain(join(skillsRoot, "git-workflow"))
    expect(paths).toContain(join(skillsRoot, "deploy"))
    expect(paths).not.toContain(join(skillsRoot, "broken"))
  })

  test("returns an empty skillPaths when the skills root is empty", async () => {
    const emptyRoot = await mkdtemp(join(tmpdir(), "comuki-empty-skills-"))
    try {
      const factory: ExtensionFactory = createSkillsExtension({
        skillsRoot: emptyRoot,
      })
      const { api, trigger } = makeHarness()
      factory(api)
      const result = await trigger()

      expect(result?.skillPaths).toEqual([])
    } finally {
      await rm(emptyRoot, { recursive: true, force: true })
    }
  })

  test("returns an empty skillPaths when the skills root is missing", async () => {
    const missing = join(skillsRoot, "does-not-exist")
    const factory: ExtensionFactory = createSkillsExtension({
      skillsRoot: missing,
    })
    const { api, trigger } = makeHarness()
    factory(api)
    const result = await trigger()

    expect(result?.skillPaths).toEqual([])
  })

  test("matches the platform scenario — listed skill is registered for the agent", async () => {
    // Spec scenario: WHEN prepare has copied a valid skill into the
    // skills root and the extension is loaded THEN pi can invoke that
    // skill by name. The bridge between listSkills and "the agent can
    // invoke it" is the resources_discover path the extension returns;
    // if `git-workflow` is missing from skillPaths the agent cannot reach
    // it, regardless of what listSkills says on its own.
    const result = await runSkills()
    const gitWorkflowPath = join(skillsRoot, "git-workflow")
    expect(result?.skillPaths ?? []).toContain(gitWorkflowPath)
  })

  test("returned factory is reusable — calling it twice is safe", () => {
    const factory = createSkillsExtension({ skillsRoot })
    const first = makeHarness()
    const second = makeHarness()
    factory(first.api)
    factory(second.api)

    expect(first.api).toBeDefined()
    expect(second.api).toBeDefined()
  })

  test("skips a stray file at the skills root (non-directory entry)", async () => {
    // listSkills filters via `entry.isDirectory()`; the extension must
    // surface that filter to the pi runtime — a stray readme at the root
    // must NOT appear in skillPaths, because pi would try to load it as
    // a skill and fail.
    const strayRoot = await mkdtemp(join(tmpdir(), "comuki-pi-skills-stray-"))
    try {
      await mkdir(join(strayRoot, "real-skill"))
      await writeFile(
        join(strayRoot, "real-skill", "SKILL.md"),
        [
          "---",
          "name: real-skill",
          "description: only valid skill",
          "---",
          "",
          "Body.",
        ].join("\n"),
        "utf8"
      )
      await writeFile(
        join(strayRoot, "README.md"),
        "This is not a skill.\n",
        "utf8"
      )

      const factory: ExtensionFactory = createSkillsExtension({
        skillsRoot: strayRoot,
      })
      const { api, trigger } = makeHarness()
      factory(api)
      const result = await trigger()

      const skillDirs = (result?.skillPaths ?? []).map(
        (p) => p.split(/[\\/]/).pop() ?? ""
      )
      expect(skillDirs).toEqual(["real-skill"])
      expect(skillDirs).not.toContain("README.md")
    } finally {
      await rm(strayRoot, { recursive: true, force: true })
    }
  })

  test("ignores nested skill directories (skills are top-level only)", async () => {
    // The control-plane layout is `<root>/<skill>/SKILL.md`. A skill
    // directory nested inside another directory (e.g. `experiments/foo/`)
    // is not a top-level entry — listSkills reads only direct children,
    // so the nested skill is invisible to the pi runtime.
    const nestedRoot = await mkdtemp(join(tmpdir(), "comuki-pi-skills-nested-"))
    try {
      await mkdir(join(nestedRoot, "top-skill"))
      await writeFile(
        join(nestedRoot, "top-skill", "SKILL.md"),
        [
          "---",
          "name: top-skill",
          "description: top-level skill",
          "---",
          "",
          "Body.",
        ].join("\n"),
        "utf8"
      )
      await mkdir(join(nestedRoot, "experiments", "nested-skill"), {
        recursive: true,
      })
      await writeFile(
        join(nestedRoot, "experiments", "nested-skill", "SKILL.md"),
        [
          "---",
          "name: nested-skill",
          "description: nested skill, must be invisible",
          "---",
          "",
          "Body.",
        ].join("\n"),
        "utf8"
      )

      const factory: ExtensionFactory = createSkillsExtension({
        skillsRoot: nestedRoot,
      })
      const { api, trigger } = makeHarness()
      factory(api)
      const result = await trigger()

      const skillDirs = (result?.skillPaths ?? []).map(
        (p) => p.split(/[\\/]/).pop() ?? ""
      )
      expect(skillDirs).toEqual(["top-skill"])
      expect(skillDirs).not.toContain("nested-skill")
      expect(skillDirs).not.toContain("experiments")
    } finally {
      await rm(nestedRoot, { recursive: true, force: true })
    }
  })

  test("skips a skill whose SKILL.md is missing the description field", async () => {
    // The control-plane rule-doc schema requires both `name` and
    // `description`. A SKILL.md with `name` only is malformed and must
    // not poison the catalog — the rest of the skills stay loadable.
    const partialRoot = await mkdtemp(
      join(tmpdir(), "comuki-pi-skills-partial-")
    )
    try {
      await mkdir(join(partialRoot, "complete-skill"))
      await writeFile(
        join(partialRoot, "complete-skill", "SKILL.md"),
        [
          "---",
          "name: complete-skill",
          "description: present and correct",
          "---",
          "",
          "Body.",
        ].join("\n"),
        "utf8"
      )
      await mkdir(join(partialRoot, "name-only"))
      await writeFile(
        join(partialRoot, "name-only", "SKILL.md"),
        [
          "---",
          "name: name-only",
          "---",
          "",
          "Body.",
        ].join("\n"),
        "utf8"
      )

      const factory: ExtensionFactory = createSkillsExtension({
        skillsRoot: partialRoot,
      })
      const { api, trigger } = makeHarness()
      factory(api)
      const result = await trigger()

      const skillDirs = (result?.skillPaths ?? []).map(
        (p) => p.split(/[\\/]/).pop() ?? ""
      )
      expect(skillDirs).toEqual(["complete-skill"])
      expect(skillDirs).not.toContain("name-only")
    } finally {
      await rm(partialRoot, { recursive: true, force: true })
    }
  })
})
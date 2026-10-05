import { afterAll, beforeAll, describe, expect, test } from "bun:test"
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { listSkills, readSkill } from "./loader"

/**
 * Task 25.5 (openspec/changes/add-mission-cowork §25.5) — guard test: the
 * loader must never narrow a query by `trigger_when`. Today it does not have
 * any selection logic at all (`listSkills` and `readSkill` return the raw
 * catalog); this test pins the contract so a future commit that adds an
 * auto-select path will fail this guard.
 */
describe("no auto-select on trigger_when (task 25.5)", () => {
  test("listSkills returns every skill regardless of trigger_when", async () => {
    const skills = await listSkills(skillsRoot)

    // citation-cleanup has a trigger_when; git-workflow and deploy-runbook do not.
    // All three must still be present — the loader must not pre-filter.
    expect(skills.map((skill) => skill.name).sort()).toEqual([
      "citation-cleanup",
      "deploy-runbook",
      "git-workflow",
    ])
  })

  test("readSkill returns the skill even when its trigger_when is a free-text hint", async () => {
    const skill = await readSkill(skillsRoot, "citation-cleanup")

    expect(skill).not.toBeNull()
    expect(skill?.triggerWhen).toBe(
      "drafting a long-form document that needs citation cleanup"
    )
  })

  test("the loader exports no auto-select helper (catalog stays a list, not a selector)", async () => {
    // If a future commit adds a select-by-trigger helper, this fails by name.
    // The intended surface is list/read — anything else belongs to the brain.
    const exported = Object.keys(
      await import("./loader")
    ).sort()

    expect(exported).toEqual(["listSkills", "readSkill"])
  })
})

let skillsRoot: string

beforeAll(async () => {
  skillsRoot = await mkdtemp(join(tmpdir(), "comuki-skills-"))

  await mkdir(join(skillsRoot, "git-workflow"))
  await writeFile(
    join(skillsRoot, "git-workflow", "SKILL.md"),
    [
      "---",
      "name: git-workflow",
      "description: Safe branch and commit flow",
      "---",
      "",
      "## Steps",
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

  await mkdir(join(skillsRoot, "citation-cleanup"))
  await writeFile(
    join(skillsRoot, "citation-cleanup", "SKILL.md"),
    [
      "---",
      "name: citation-cleanup",
      "description: Cleans up inline citations",
      "trigger_when: drafting a long-form document that needs citation cleanup",
      "validate_against:",
      '  - "../../rules/citation-format.md"',
      '  - { kind: knowledge, id: "doc/citation-style@v3" }',
      "version: 1.2.0",
      "---",
      "",
      "Strip duplicates, normalise.",
    ].join("\n"),
    "utf8"
  )

  await mkdir(join(skillsRoot, "broken"))
  await writeFile(
    join(skillsRoot, "broken", "SKILL.md"),
    "No frontmatter at all.\n",
    "utf8"
  )

  await mkdir(join(skillsRoot, "empty-dir"))

  await writeFile(
    join(skillsRoot, "stray-notes.md"),
    "not a skill directory\n",
    "utf8"
  )
})

afterAll(async () => {
  await rm(skillsRoot, { recursive: true, force: true })
})

describe("listSkills", () => {
  test("lists only valid skills, sorted by name", async () => {
    const skills = await listSkills(skillsRoot)

    expect(skills.map((skill) => skill.name)).toEqual([
      "citation-cleanup",
      "deploy-runbook",
      "git-workflow",
    ])
  })

  test("skill docs carry frontmatter, body and dir name", async () => {
    const skills = await listSkills(skillsRoot)
    const gitWorkflow = skills.find((skill) => skill.name === "git-workflow")

    expect(gitWorkflow?.description).toBe("Safe branch and commit flow")
    expect(gitWorkflow?.body).toContain("## Steps")
    expect(gitWorkflow?.dirName).toBe("git-workflow")
  })

  test("skills without metadata default version to 0.1.0 and omit the new fields", async () => {
    const skills = await listSkills(skillsRoot)
    const gitWorkflow = skills.find((skill) => skill.name === "git-workflow")

    expect(gitWorkflow?.version).toBe("0.1.0")
    expect(gitWorkflow?.triggerWhen).toBeUndefined()
    expect(gitWorkflow?.validateAgainst).toBeUndefined()
  })

  test("skills with full metadata expose trigger_when, validate_against and version", async () => {
    const skills = await listSkills(skillsRoot)
    const skill = skills.find((entry) => entry.name === "citation-cleanup")

    expect(skill?.triggerWhen).toBe(
      "drafting a long-form document that needs citation cleanup"
    )
    expect(skill?.validateAgainst).toEqual([
      "../../rules/citation-format.md",
      { kind: "knowledge", id: "doc/citation-style@v3" },
    ])
    expect(skill?.version).toBe("1.2.0")
  })
})

describe("readSkill", () => {
  test("reads a single skill by directory name", async () => {
    const skill = await readSkill(skillsRoot, "deploy")

    expect(skill).not.toBeNull()
    expect(skill?.name).toBe("deploy-runbook")
    expect(skill?.dirName).toBe("deploy")
  })

  test("returns null for a missing directory", async () => {
    expect(await readSkill(skillsRoot, "does-not-exist")).toBeNull()
  })

  test("returns null for a directory without SKILL.md", async () => {
    expect(await readSkill(skillsRoot, "empty-dir")).toBeNull()
  })

  test("returns null for a SKILL.md without valid frontmatter", async () => {
    expect(await readSkill(skillsRoot, "broken")).toBeNull()
  })
})

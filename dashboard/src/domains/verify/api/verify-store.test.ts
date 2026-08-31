import { afterEach, describe, expect, it } from "vitest"

import {
  commandsFor,
  failingCount,
  neverRanCount,
  resultLabel,
  sourceLocation,
} from "@/domains/verify/model/gate"
import { PROJECTS_SEED } from "@/shared/api/mock/session.seed"
import { VERIFY_SEED } from "@/shared/api/mock/verify.seed"
import {
  readSeedVerify,
  resetSeedVerify,
  setSeedVerifyEnabled,
} from "@/shared/api/mock/verify.store"

afterEach(() => {
  resetSeedVerify()
})

const PROJECT_IDS = new Set(PROJECTS_SEED.map((project) => project.id))

/**
 * The seeded gate, as a contract.
 *
 * The screen's job is to make three states legible — a check that fails, a
 * check nothing has reached, and a project that declares nothing at all — so
 * the seed has to actually contain all three. A mock that settled into six
 * green checks would let the copy for the other two rot while every gate ran
 * green.
 */
describe("the seeded gate", () => {
  it("covers every project the shift can name", () => {
    expect(VERIFY_SEED.projects.map((project) => project.projectId).sort()).toEqual(
      [...PROJECT_IDS].sort()
    )
  })

  it("declares every command against a project that exists", () => {
    for (const command of VERIFY_SEED.commands) {
      expect(PROJECT_IDS.has(command.projectId)).toBe(true)
    }
  })

  it("holds a check that has never run, and one that is failing", () => {
    expect(neverRanCount(VERIFY_SEED.commands)).toBeGreaterThan(0)
    expect(failingCount(VERIFY_SEED.commands)).toBeGreaterThan(0)
  })

  it("puts a sentence behind every failure", () => {
    const failures = VERIFY_SEED.commands.filter(
      (command) => command.last?.outcome === "failed"
    )
    for (const command of failures) {
      // A red cell with nothing behind it sends the operator to the run to find
      // out what a line here could have told them.
      expect(command.last?.detail).toBeTruthy()
    }
  })

  it("names the run behind every result it has", () => {
    for (const command of VERIFY_SEED.commands) {
      if (command.last) {
        expect(command.last.runId.length).toBeGreaterThan(0)
      }
    }
  })

  it("holds a project whose gate is off, and it still declares checks", () => {
    const off = VERIFY_SEED.projects.filter((project) => !project.enabled)
    expect(off.length).toBeGreaterThan(0)
    for (const project of off) {
      // A switch here does not delete a file over there.
      expect(commandsFor(VERIFY_SEED.commands, project.projectId).length)
        .toBeGreaterThan(0)
    }
  })

  it("holds a project whose git declares nothing at all", () => {
    const bare = VERIFY_SEED.projects.filter(
      (project) =>
        commandsFor(VERIFY_SEED.commands, project.projectId).length === 0
    )
    expect(bare.length).toBeGreaterThan(0)
    // The empty state has to name a file, so the coordinates are there even
    // when the file is not.
    for (const project of bare) {
      expect(project.source.path.length).toBeGreaterThan(0)
      expect(project.source.repo.length).toBeGreaterThan(0)
    }
  })

  it("gives every project somewhere to open", () => {
    for (const project of VERIFY_SEED.projects) {
      expect(project.source.url.startsWith("https://")).toBe(true)
    }
  })
})

describe("the one thing this screen can change", () => {
  it("keeps a flipped gate across a re-read", () => {
    const project = readSeedVerify().projects[0]

    setSeedVerifyEnabled(project.projectId, !project.enabled)

    // The point of the store: a query that mapped the seed constant would
    // restore it on the refetch and the switch would flip back 200 ms later.
    expect(
      readSeedVerify().projects.find(
        (entry) => entry.projectId === project.projectId
      )?.enabled
    ).toBe(!project.enabled)
  })

  it("leaves the commands exactly where they were", () => {
    const before = readSeedVerify().commands

    setSeedVerifyEnabled("p_comuki", false)

    // There is no setter for a command anywhere in this domain, and this is the
    // property that says so: turning the gate off changes the gate, not the
    // client's git.
    expect(readSeedVerify().commands).toEqual(before)
  })
})

describe("the gate's own readings", () => {
  it("names where a project's commands live, narrowing left to right", () => {
    expect(
      sourceLocation({
        repo: "here/web-app",
        ref: "main",
        path: ".comuki/verify.yaml",
        url: "https://example.test",
      })
    ).toBe("here/web-app @ main · .comuki/verify.yaml")
  })

  it("reads a missing result as never ran rather than as a failure", () => {
    expect(resultLabel(null)).toBe("never ran")
    expect(
      resultLabel({
        outcome: "failed",
        runId: "r1",
        at: "now",
        durationSec: 1,
      })
    ).toBe("failed")
    expect(
      resultLabel({
        outcome: "success",
        runId: "r1",
        at: "now",
        durationSec: 1,
      })
    ).toBe("passed")
  })
})

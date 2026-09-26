import { describe, expect, it } from "vitest"

import {
  gitHost,
  isImageIcon,
  resolveProjectMark,
} from "@/domains/projects/model/identity"

/**
 * The mark resolution — the display half of the identity contract (design D3).
 *
 * Pure by construction: the cases are value tables, because the whole point
 * of deriving the mark client-side is that a renderer can call this with
 * nothing but a row and get a drawable answer.
 */

describe("gitHost", () => {
  it("reads the host out of the scp-like shape the seeds use", () => {
    expect(gitHost("git@github.com:comuki/worker-profiles.git")).toBe(
      "github.com"
    )
  })

  it("reads the host out of an https URL", () => {
    expect(gitHost("https://gitlab.com/plexor/agent-profiles")).toBe(
      "gitlab.com"
    )
  })

  it("lower-cases whatever host it found", () => {
    expect(gitHost("git@GitHub.COM:org/repo.git")).toBe("github.com")
  })

  it("answers null for no URL, not a parse failure", () => {
    expect(gitHost(null)).toBeNull()
    expect(gitHost("")).toBeNull()
  })

  it("answers null for a string that is neither shape", () => {
    expect(gitHost("just-a-path")).toBeNull()
  })
})

describe("resolveProjectMark", () => {
  it("shows a stored icon over everything else", () => {
    expect(
      resolveProjectMark({
        icon: "🛰️",
        gitProfileRepo: "git@github.com:org/repo.git",
      })
    ).toEqual({ kind: "stored", value: "🛰️" })
  })

  it("derives the GitHub mark from a github host in either URL shape", () => {
    expect(
      resolveProjectMark({
        icon: null,
        gitProfileRepo: "git@github.com:org/worker-profiles.git",
      })
    ).toEqual({
      kind: "brand",
      brand: "github",
    })
    expect(
      resolveProjectMark({
        icon: null,
        gitProfileRepo: "https://github.com/org/worker-profiles",
      })
    ).toEqual({
      kind: "brand",
      brand: "github",
    })
  })

  it("derives the GitLab mark from gitlab.com and a self-hosted gitlab host", () => {
    expect(
      resolveProjectMark({
        icon: null,
        gitProfileRepo: "git@gitlab.com:plexor/agent-profiles.git",
      })
    ).toEqual({ kind: "brand", brand: "gitlab" })
    expect(
      resolveProjectMark({
        icon: null,
        gitProfileRepo: "https://gitlab.internal.example/group/repo",
      })
    ).toEqual({ kind: "brand", brand: "gitlab" })
  })

  it("derives the generic git glyph for an unknown host", () => {
    expect(
      resolveProjectMark({
        icon: null,
        gitProfileRepo: "git@gitea.internal:org/repo.git",
      })
    ).toEqual({ kind: "brand", brand: "git" })
  })

  it("answers the neutral glyph when there is nothing to derive from", () => {
    expect(resolveProjectMark({ icon: null, gitProfileRepo: null })).toEqual({
      kind: "neutral",
    })
  })

  it("falls through to derivation when the stored icon is only whitespace", () => {
    expect(
      resolveProjectMark({
        icon: "  ",
        gitProfileRepo: "git@github.com:org/repo.git",
      })
    ).toEqual({ kind: "brand", brand: "github" })
  })
})

describe("isImageIcon", () => {
  it("says an https URL is an image", () => {
    expect(isImageIcon("https://example.com/logo.png")).toBe(true)
  })

  it("says an emoji and a word are text", () => {
    expect(isImageIcon("🛰️")).toBe(false)
    expect(isImageIcon("satellite")).toBe(false)
  })

  it("does not mistake an scp-like git path for a scheme", () => {
    expect(isImageIcon("git@github.com:org/repo.git")).toBe(false)
  })
})

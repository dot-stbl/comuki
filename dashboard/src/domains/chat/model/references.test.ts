import { describe, expect, it } from "vitest"

import type { SearchCatalogue } from "@/app/search"
import {
  chatHandoffs,
  referenceFromLocation,
  tokenizeReferences,
} from "@/domains/chat/model/references"
import type { Role, Session } from "@/shared/session"

/**
 * Typed references, resolved through the product's own shape catalogue.
 *
 * The point of these cases is not that `5b1d7e40` becomes a link — it is that
 * it becomes a link *by the same rule the command palette uses*, and that the
 * two tiers of that rule are not both allowed in prose.
 */

function session(platformRoles: Role[] = ["member"]): Session {
  return {
    user: {
      id: "u_test",
      name: "Test",
      email: "test@comuki.local",
      platformRoles,
      projectRoles: {},
    },
    projects: [
      { id: "p_comuki", key: "comuki", name: "Comuki platform" },
      { id: "p_atlas", key: "atlas", name: "Atlas" },
    ],
  }
}

const catalogue: SearchCatalogue = {
  projects: session().projects,
  apps: ["web-app", "billing-api", "atlas-gateway"],
}

/** Only the runs that actually resolved, as `text → href`. */
function links(tokens: ReturnType<typeof tokenizeReferences>) {
  return tokens
    .filter((token) => token.target)
    .map((token) => [token.text, token.target?.href])
}

describe("identifiers inside a message", () => {
  it("resolves a run id to the run, through the shared catalogue", () => {
    const tokens = tokenizeReferences(
      "прогон 5b1d7e40 ждёт человека",
      catalogue,
      session()
    )
    expect(links(tokens)).toEqual([["5b1d7e40", "/runs/5b1d7e40"]])
  })

  it("keeps the punctuation an identifier was wearing outside the link", () => {
    const tokens = tokenizeReferences("см. «5b1d7e40».", catalogue, session())
    expect(links(tokens)).toEqual([["5b1d7e40", "/runs/5b1d7e40"]])
    // And the sentence still reads back exactly as it was written.
    expect(tokens.map((token) => token.text).join("")).toBe("см. «5b1d7e40».")
  })

  it("does not trim the characters that live inside identifiers", () => {
    const tokens = tokenizeReferences(
      "wi_0101 and wk_e34d and ap-14",
      catalogue,
      session(["platform-admin"])
    )
    expect(links(tokens).map(([text]) => text)).toEqual([
      "wi_0101",
      "wk_e34d",
      "ap-14",
    ])
  })

  it("refuses the catalogue tier — a word in a sentence is not a link", () => {
    // `resolveShapes` would offer `atlas` as a project and `web` as two
    // applications. In a search box that is a disambiguation the operator
    // asked for; in a paragraph it is the interface underlining nouns.
    const tokens = tokenizeReferences(
      "atlas выкатили, web тоже",
      catalogue,
      session()
    )
    expect(links(tokens)).toEqual([])
  })

  it("renders a reference this session cannot open as plain text", () => {
    // A link into a forbidden state teaches the shape of somebody else's
    // access. A `member` cannot open identity, so the address stays words.
    const tokens = tokenizeReferences(
      "написал duty@comuki.local",
      catalogue,
      session(["member"])
    )
    expect(links(tokens)).toEqual([])

    const admin = tokenizeReferences(
      "написал duty@comuki.local",
      catalogue,
      session(["platform-admin"])
    )
    expect(links(admin)).toEqual([
      ["duty@comuki.local", "/identity?tab=users&q=duty%40comuki.local"],
    ])
  })

  it("preserves the shape of a pasted block", () => {
    const pasted = "line one\n  line two\n\nline four"
    const tokens = tokenizeReferences(pasted, catalogue, session())
    expect(tokens.map((token) => token.text).join("")).toBe(pasted)
  })
})

describe("the hand-off", () => {
  it("offers a filter on the real screen, with the right href", () => {
    const handoffs = chatHandoffs("webhook", session(), catalogue)
    expect(handoffs.map((entry) => [entry.where, entry.href])).toEqual([
      ["live runs", "/runs?q=webhook"],
      ["the queue", "/queue?q=webhook"],
      ["the inbox", "/tasks?q=webhook"],
    ])
  })

  it("encodes what it hands over", () => {
    const [first] = chatHandoffs("a b&c", session(), catalogue)
    expect(first?.href).toBe("/runs?q=a%20b%26c")
  })

  it("offers only the screens this session can open", () => {
    // A viewer holds `runs.view` and nothing else, so the queue and the inbox
    // are not offered — the same rule the palette keeps.
    const handoffs = chatHandoffs("webhook", session(["viewer"]), catalogue)
    expect(handoffs.map((entry) => entry.where)).toEqual(["live runs"])
  })

  it("has nothing to hand off when there is no question", () => {
    expect(chatHandoffs("   ", session(), catalogue)).toEqual([])
  })
})

describe("what the operator was looking at", () => {
  it("reads the entity a detail screen is about out of the path", () => {
    expect(
      referenceFromLocation("/runs/5b1d7e40", "", catalogue, session())
    ).toMatchObject({ kind: "run", id: "5b1d7e40" })
  })

  it("reads a list narrowed to one thing out of the search", () => {
    expect(
      referenceFromLocation("/queue", "?w=wk_e34d", catalogue, session())
    ).toMatchObject({ kind: "worker", id: "wk_e34d" })
  })

  it("prefers the path: a screen about one thing is about that thing", () => {
    expect(
      referenceFromLocation(
        "/queue/workers/wk_e34d",
        "?w=wk_other",
        catalogue,
        session()
      )
    ).toMatchObject({ id: "wk_e34d" })
  })

  it("seeds nothing from a screen that names no entity", () => {
    // "the queue" is a screen, not a subject. A seed that was a word would be
    // a mode the conversation is "in" — the composer's seed is an entity or
    // absent.
    expect(
      referenceFromLocation("/queue", "?status=waiting", catalogue, session())
    ).toBeNull()
    expect(referenceFromLocation("/runs", "", catalogue, session())).toBeNull()
  })

  it("does not offer a seed the session cannot follow", () => {
    // The same rule prose keeps: a reference into a forbidden state teaches
    // the shape of somebody else's access. `member` cannot open identity, so
    // an api-key screen seeds nothing.
    expect(
      referenceFromLocation(
        "/identity",
        "?tab=keys&q=cmk_9f21ab77",
        catalogue,
        session(["member"])
      )
    ).toBeNull()
    expect(
      referenceFromLocation(
        "/identity",
        "?tab=keys&q=cmk_9f21ab77",
        catalogue,
        session(["platform-admin"])
      )
    ).toMatchObject({ id: "cmk_9f21ab77" })
  })
})

import { describe, expect, it } from "vitest"

import { SLUG_MAX, slugify, validateSlug } from "./slug"

describe("the slug a name proposes", () => {
  it("lowercases and hyphenates", () => {
    expect(slugify("Payments Platform")).toBe("payments-platform")
  })

  it("drops accents rather than the letters under them", () => {
    expect(slugify("Inés Moreau")).toBe("ines-moreau")
  })

  it("collapses punctuation into single hyphens", () => {
    expect(slugify("Atlas / billing (v2)")).toBe("atlas-billing-v2")
  })

  it("never proposes a leading or trailing hyphen", () => {
    expect(slugify("  --Atlas--  ")).toBe("atlas")
  })

  it("proposes nothing from a name with nothing in it", () => {
    expect(slugify("!!!")).toBe("")
  })

  it("stays inside the length the field accepts", () => {
    const proposed = slugify("a".repeat(SLUG_MAX + 20))
    expect(proposed.length).toBe(SLUG_MAX)
    // And what it proposes is always something the field would accept.
    expect(validateSlug(proposed)).toBeNull()
  })
})

describe("what makes a slug a handle", () => {
  it("accepts the shape the product shows in a column", () => {
    expect(validateSlug("comuki")).toBeNull()
    expect(validateSlug("billing-api")).toBeNull()
    expect(validateSlug("web2")).toBeNull()
  })

  it("asks for one at all", () => {
    expect(validateSlug("")).toBe("a slug is required")
    expect(validateSlug("   ")).toBe("a slug is required")
  })

  it("names the space before anything else, because that is what was typed", () => {
    expect(validateSlug("Payments Platform")).toBe("no spaces — use a hyphen")
  })

  it("refuses capitals rather than quietly lowercasing them", () => {
    // Silently rewriting the field would hand the operator a handle they did
    // not choose, in the column where they have to recognise it.
    expect(validateSlug("Atlas")).toBe("slugs are lowercase")
  })

  it("refuses anything that is not a letter, a digit or a hyphen", () => {
    expect(validateSlug("bill_api")).toBe("letters, digits and single hyphens only")
    expect(validateSlug("bill.api")).toBe("letters, digits and single hyphens only")
    expect(validateSlug("bill--api")).toBe(
      "letters, digits and single hyphens only"
    )
  })

  it("refuses a hyphen at either end", () => {
    expect(validateSlug("-atlas")).toBe(
      "must start and end with a letter or digit"
    )
    expect(validateSlug("atlas-")).toBe(
      "must start and end with a letter or digit"
    )
  })

  it("keeps it long enough to read and short enough to be a column", () => {
    expect(validateSlug("a")).toBe("at least 2 characters")
    expect(validateSlug("a".repeat(SLUG_MAX + 1))).toBe(
      `at most ${SLUG_MAX} characters`
    )
  })

  it("refuses a handle somebody already has", () => {
    expect(validateSlug("atlas", ["comuki", "atlas"])).toBe("that slug is taken")
    expect(validateSlug("vega", ["comuki", "atlas"])).toBeNull()
  })
})

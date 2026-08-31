import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import {
  AutonomyModeMark,
  EnvTags,
  KeyStatusMark,
  RuleKindMark,
} from "@/domains/settings/ui/settings-badges"

const find = (test: string) => document.querySelector(`[data-test="${test}"]`)

const glyph = (test: string) => find(test)?.querySelector("svg")?.outerHTML ?? ""

const words = (test: string) => find(test)?.textContent ?? ""

/**
 * The control plane's four marks, checked on the one rule that is easy to lose
 * in a rewrite: a value is never carried by hue alone. Every mark here has to
 * say its own word, and every one of them has to carry a glyph beside it — so
 * the reading survives greyscale, a colour-blind operator and a photocopied
 * screenshot.
 */
describe("the control-plane marks, read without colour", () => {
  it("says a rule's kind in a word and in a glyph", () => {
    const { rerender } = render(<RuleKindMark kind="hard" />)
    expect(words("rule-kind-mark")).toBe("hard")
    expect(find("rule-kind-mark")?.getAttribute("data-kind")).toBe("hard")
    const hardGlyph = glyph("rule-kind-mark")
    expect(hardGlyph).not.toBe("")

    rerender(<RuleKindMark kind="soft" />)
    expect(words("rule-kind-mark")).toBe("soft")
    expect(find("rule-kind-mark")?.getAttribute("data-kind")).toBe("soft")
    // Two kinds, two silhouettes: the same glyph in two colours would be one
    // channel wearing a disguise.
    expect(glyph("rule-kind-mark")).not.toBe(hardGlyph)
  })

  it("says what a provider actually reported, not the enum behind it", () => {
    render(<KeyStatusMark status="warn" label="budget 67%" />)
    // `warn` is a category; `budget 67%` is the only one of the two that tells
    // anybody what is about to happen.
    expect(words("key-status-mark")).toBe("budget 67%")
    expect(find("key-status-mark")?.getAttribute("data-status")).toBe("warn")
    expect(glyph("key-status-mark")).not.toBe("")
  })

  it("names who decides a change class", () => {
    const { rerender } = render(<AutonomyModeMark mode="auto" />)
    expect(words("autonomy-mode-mark")).toBe("auto")
    const autoGlyph = glyph("autonomy-mode-mark")

    rerender(<AutonomyModeMark mode="human" />)
    expect(words("autonomy-mode-mark")).toBe("human")
    expect(find("autonomy-mode-mark")?.getAttribute("data-mode")).toBe("human")
    expect(glyph("autonomy-mode-mark")).not.toBe(autoGlyph)
  })

  it("says where an app deploys, and says so when it deploys nowhere", () => {
    const { rerender } = render(<EnvTags envs={["prod", "staging"]} />)
    expect(words("env-tags")).toBe("prodstaging")

    rerender(<EnvTags envs={[]} />)
    // A blank cell reads as a broken render; a word reads as a fact.
    expect(find("env-tags")).toBeNull()
    expect(document.body.textContent).toBe("nowhere")
  })

  it("keeps every word lowercase, the way the product spells its values", () => {
    render(
      <>
        <RuleKindMark kind="hard" />
        <AutonomyModeMark mode="human" />
        <EnvTags envs={["prod"]} />
      </>
    )
    for (const test of ["rule-kind-mark", "autonomy-mode-mark", "env-tags"]) {
      const text = words(test)
      expect(text).not.toBe("")
      expect(text).toBe(text.toLowerCase())
    }
  })
})

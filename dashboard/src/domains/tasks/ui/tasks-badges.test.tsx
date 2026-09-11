import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { PROVIDERS } from "@/domains/sources/model/providers"

import { TaskSourceBadge } from "./tasks-badges"
import { TaskSourceCards } from "./task-source-cards"

/**
 * The backlog's provenance stamp and the intake's provider cards read the one
 * registry in `domains/sources/model/providers.ts`.
 *
 * They used to read a second catalogue of their own — five closed members, the
 * product's own intake spelled `manual` where the sources half said `native`,
 * and three more tables (`TASK_SOURCE_LABEL`, `_BRAND`, `_NOTE`) to keep in
 * step with six on the other side. These tests hold the seam shut: the cards
 * are the registry read out, and a provider with no entry still says a word.
 */

const badge = () =>
  document.querySelector('[data-test="task-source-badge"]') as HTMLElement

const mark = () =>
  document.querySelector('[data-test="brand-icon"]') as SVGElement | null

describe("the backlog's provenance stamp", () => {
  it("shows the tracker's own id for a ticket that came off one", () => {
    render(<TaskSourceBadge source="github" id="COMUKI-128" />)

    // The id *is* the identity for a tracker row, so it is what the badge
    // reads; the octocat beside it is a recognition cue and nothing more.
    expect(badge().textContent).toBe("COMUKI-128")
    expect(mark()?.getAttribute("data-brand")).toBe("github")
  })

  it("says the provider for the product's own intake, which has no id", () => {
    render(<TaskSourceBadge source="native" id="m-3042" />)

    // `native`, not `manual`: one word for one idea, and it is the word the
    // wire uses. The mark is the product's own container, exactly as it is on
    // the sources table and in the topbar.
    expect(badge().textContent).toBe("native")
    expect(mark()?.getAttribute("data-brand")).toBe("comuki")
  })

  it("keeps the row readable for a provider it has never met", () => {
    render(<TaskSourceBadge source="linear" id="LIN-77" />)

    // No registry entry, so no mark to draw — and drawing somebody's
    // trademark from memory is the one thing worse than not drawing it. The
    // row still carries its id and the word travels on `data-source`, which
    // is what a filter and a screenshot both read.
    expect(badge().textContent).toBe("LIN-77")
    expect(badge().getAttribute("data-source")).toBe("linear")
    expect(mark()).toBeNull()
  })
})

describe("the intake's provider cards", () => {
  const cards = () =>
    Array.from(
      document.querySelectorAll<HTMLElement>('[data-test="task-source-card"]')
    )

  it("is the registry read out, in the registry's order", () => {
    render(<TaskSourceCards value="native" onValueChange={() => {}} />)

    // Not a list that happens to agree with the registry — the list. A sixth
    // provider appears here because somebody wrote its entry, and there is no
    // second array for them to forget.
    expect(cards().map((card) => card.dataset.value)).toEqual(
      PROVIDERS.map((provider) => provider.key)
    )
  })

  it("gives every card the entry's own word and its own sentence", () => {
    render(<TaskSourceCards value="native" onValueChange={() => {}} />)

    for (const provider of PROVIDERS) {
      // The word is on the radio either way — a card whose mark already says
      // the provider's name does not spell it twice, but nobody listening
      // rather than looking loses it.
      expect(
        screen.getByRole("radio", { name: provider.label })
      ).not.toBeNull()
      expect(
        cards()
          .find((card) => card.dataset.value === provider.key)
          ?.textContent
      ).toContain(provider.intakeNote)
    }
  })

  it("spells the provider whose mark would be a guess, and the one that is ours", () => {
    render(<TaskSourceCards value="native" onValueChange={() => {}} />)

    const visible = (key: string) =>
      cards()
        .find((card) => card.dataset.value === key)
        ?.querySelector('[class*="name"]')?.textContent

    // A mark whose brand id *is* the provider says the name already. The two
    // that stand in for something else — the board glyph for yandex tracker,
    // the product's own container for native — keep the word beside them.
    expect(visible("github")).toBeUndefined()
    expect(visible("yandex-tracker")).toBe("yandex tracker")
    expect(visible("native")).toBe("native")
  })
})

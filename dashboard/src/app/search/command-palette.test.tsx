import { useState } from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { CommandPalette } from "./command-palette"
import type { SearchItem } from "./resolve"

/* Rows shaped the way the resolver shapes them, written out rather than
   resolved: this file is about the surface, and a view that had to be fed
   through the resolver to be tested would be a view that knew about it. */
const RESOLVED: SearchItem = {
  id: "resolved:run:5b1d7e40",
  group: "resolved",
  kind: "run",
  label: "5b1d7e40",
  value: true,
  hint: "in live runs",
  href: "/runs/5b1d7e40",
}

const SECTION: SearchItem = {
  id: "section:/queue",
  group: "section",
  kind: "section",
  label: "Queue",
  value: false,
  hint: "observe",
  href: "/queue",
}

const HANDOFF: SearchItem = {
  id: "handoff:/runs",
  group: "handoff",
  kind: "search",
  label: "webhook",
  value: true,
  hint: "in live runs",
  href: "/runs?q=webhook",
}

function rows() {
  return Array.from(
    document.querySelectorAll<HTMLElement>(
      '[data-test="command-palette-item"]'
    )
  )
}

interface HarnessProps {
  items: SearchItem[]
  onSelect?: (item: SearchItem) => void
}

/** The palette as the shell drives it: open, with the query held above it. */
function Harness({ items, onSelect = () => {} }: HarnessProps) {
  const [open, setOpen] = useState(true)
  const [query, setQuery] = useState("")

  return (
    <CommandPalette
      open={open}
      onOpenChange={setOpen}
      query={query}
      onQueryChange={setQuery}
      items={items}
      onSelect={onSelect}
    />
  )
}

describe("the palette's surface", () => {
  it("bands the rows and names each band", async () => {
    render(<Harness items={[RESOLVED, SECTION, HANDOFF]} />)

    await screen.findByRole("dialog")
    const bands = Array.from(
      document.querySelectorAll('[data-test="command-palette-band"]')
    )
    expect(bands).toHaveLength(3)
    expect(rows().map((row) => row.dataset.kind)).toEqual([
      "run",
      "section",
      "search",
    ])
  })

  it("renders no band for a layer that answered with nothing", () => {
    render(<Harness items={[HANDOFF]} />)

    // A heading standing over nothing is a worse artefact than the missing
    // rows were — the rail drops an empty group for the same reason.
    expect(
      document.querySelectorAll('[data-test="command-palette-band"]')
    ).toHaveLength(1)
  })

  it("says so rather than showing an empty list", () => {
    render(<Harness items={[]} />)

    expect(
      document.querySelector('[data-test="command-palette-empty"]')?.textContent
    ).toContain("nothing here answers to that")
    expect(rows()).toHaveLength(0)
  })

  it("takes the highlighted row on enter, after the arrows have moved it", async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<Harness items={[RESOLVED, SECTION, HANDOFF]} onSelect={onSelect} />)

    const input = document.querySelector<HTMLInputElement>(
      '[data-test="command-palette-input"]'
    )!
    input.focus()

    // The caret never leaves the field: React Aria moves through the list with
    // virtual focus, so the row enter would take is the one marked focused.
    await user.keyboard("{ArrowDown}{ArrowDown}")
    await user.keyboard("{Enter}")

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(onSelect.mock.calls[0][0]).toMatchObject({ href: "/queue" })
  })

  it("takes a row that is clicked", async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<Harness items={[RESOLVED, SECTION]} onSelect={onSelect} />)

    await user.click(rows()[0])

    expect(onSelect.mock.calls[0][0]).toMatchObject({ href: "/runs/5b1d7e40" })
  })

  it("closes on escape rather than clearing the field", async () => {
    const user = userEvent.setup()
    render(<Harness items={[RESOLVED]} />)

    const input = document.querySelector<HTMLInputElement>(
      '[data-test="command-palette-input"]'
    )!
    input.focus()
    await user.keyboard("wi_")
    await user.keyboard("{Escape}")

    // A search field would have eaten this key to clear itself, and the
    // palette would still be open. It is a text field for exactly that reason.
    expect(
      document.querySelector('[data-test="command-palette"]')
    ).toBeNull()
  })

  it("sets identifiers in the data voice and words in the interface one", () => {
    render(<Harness items={[RESOLVED, SECTION]} />)

    const [identifier, words] = rows()
    const valueClass = identifier
      .querySelector("span:nth-child(2)")
      ?.className.includes("itemValue")
    const wordsClass = words
      .querySelector("span:nth-child(2)")
      ?.className.includes("itemValue")

    // Two voices, and a run id is a value. The class is the carrier; the
    // stylesheet is what puts the mono face on it.
    expect({ identifier: valueClass, words: wordsClass }).toEqual({
      identifier: true,
      words: false,
    })
  })
})

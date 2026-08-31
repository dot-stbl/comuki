import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

import { KnowledgeDetailSheet } from "./knowledge-detail-sheet"

/* The product marks its own elements with `data-test`, not `data-testid`. */
const find = (selector: string) => document.querySelector(selector)
const sheet = () => find('[data-test="knowledge-sheet"]')

const ENTRY: KnowledgeEntry = {
  id: "api-errors",
  kind: "rule",
  title: "api-errors",
  scope: "global",
  ruleKind: "hard",
  revision: "a1b9e0",
  pinned: true,
  summary: "Ошибки API типизированы, с кодом и retry-hint",
  body: "Все HTTP-ошибки возвращают ProblemDetails с стабильным code и retry-hint.",
  updated: "2h ago",
}

describe("the entry sheet", () => {
  it("shows the whole entry, not a summary of it", () => {
    render(<KnowledgeDetailSheet entry={ENTRY} open onOpenChange={vi.fn()} />)

    const text = sheet()?.textContent ?? ""
    expect(text).toContain("api-errors")
    expect(text).toContain(ENTRY.summary)
    expect(text).toContain(ENTRY.body)
    expect(text).toContain("global")
    expect(text).toContain("2h ago")
    expect(text).toContain("pinned @ a1b9e0")
  })

  it("names its revision plainly when nothing pins it", () => {
    render(
      <KnowledgeDetailSheet
        entry={{ ...ENTRY, pinned: false }}
        open
        onOpenChange={vi.fn()}
      />
    )

    expect(find('[data-test="knowledge-pinned"]')).toBeNull()
    expect(sheet()?.textContent).toContain("revision @a1b9e0")
  })

  it("closes from the glyph as well as from escape and the scrim", () => {
    const onOpenChange = vi.fn()
    render(<KnowledgeDetailSheet entry={ENTRY} open onOpenChange={onOpenChange} />)

    // Named for what it closes, because "×" is not the name of anything.
    fireEvent.click(screen.getByRole("button", { name: "Close the entry" }))
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it("renders nothing at all while it is closed", () => {
    render(
      <KnowledgeDetailSheet entry={ENTRY} open={false} onOpenChange={vi.fn()} />
    )

    expect(sheet()).toBeNull()
  })
})

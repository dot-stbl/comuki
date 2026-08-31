import { fireEvent, render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { KnowledgeEntry } from "@/domains/knowledge/model/types"

import { KnowledgeEntryRow } from "./knowledge-entry-row"

/* The product marks its own elements with `data-test`, not `data-testid`, so
   these go through the DOM rather than through Testing Library's default. */
const find = (selector: string) => document.querySelector(selector)
const row = () => find('[data-test="knowledge-entry"]')

const RULE: KnowledgeEntry = {
  id: "no-secrets",
  kind: "rule",
  title: "no-secrets",
  scope: "global",
  ruleKind: "hard",
  revision: "9f2c1a",
  pinned: true,
  summary: "Секреты только из vault, не в коде и логах",
  body: "Токены и пароли читаются из env / secret store.",
  updated: "1d ago",
}

const DOC: KnowledgeEntry = {
  id: "architecture-overview",
  kind: "doc",
  title: "Architecture overview",
  scope: "docs",
  revision: "2.4.1",
  pinned: false,
  summary: "Ведущая модель + рой эфемерных воркеров",
  body: "Comuki декомпозирует задачу ведущей моделью.",
  updated: "1w ago",
}

describe("an entry in the rule set", () => {
  it("says everything the list is scanned for, without opening anything", () => {
    render(<KnowledgeEntryRow entry={RULE} selected={false} onSelect={vi.fn()} />)

    const text = row()?.textContent ?? ""
    expect(text).toContain("no-secrets")
    // Both vocabularies, and both of them said in words rather than in hue —
    // which is what makes the row readable in greyscale.
    expect(text).toContain("rule")
    expect(text).toContain("hard")
    expect(text).toContain("pinned")
    expect(text).toContain("@9f2c1a")
    expect(text).toContain("global")
    expect(text).toContain("updated 1d ago")
  })

  it("leaves out the marks an entry does not carry", () => {
    render(<KnowledgeEntryRow entry={DOC} selected={false} onSelect={vi.fn()} />)

    // A doc has no rule kind and this one is not pinned: absent, not blank.
    expect(find('[data-test="knowledge-rule-kind"]')).toBeNull()
    expect(find('[data-test="knowledge-pinned"]')).toBeNull()
    expect(find('[data-test="knowledge-kind"]')?.textContent).toContain("doc")
  })

  it("is the control, so the whole row opens the entry", () => {
    const onSelect = vi.fn()
    render(<KnowledgeEntryRow entry={RULE} selected={false} onSelect={onSelect} />)

    fireEvent.click(screen.getByRole("button"))
    expect(onSelect).toHaveBeenCalledWith("no-secrets")
  })

  it("says it is the selected one in the accessibility tree, not only in paint", () => {
    const { rerender } = render(
      <KnowledgeEntryRow entry={RULE} selected={false} onSelect={vi.fn()} />
    )
    expect(row()?.getAttribute("aria-pressed")).toBe("false")

    rerender(<KnowledgeEntryRow entry={RULE} selected onSelect={vi.fn()} />)
    expect(row()?.getAttribute("aria-pressed")).toBe("true")
    expect(row()?.getAttribute("data-selected")).toBe("true")
  })
})

import { fireEvent, render } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { TagFilter } from "@/domains/projects/ui/tag-filter"

const at = (test: string) => document.querySelector(`[data-test="${test}"]`)

describe("TagFilter", () => {
  it("renders one toggle per available tag, and nothing without a vocabulary", () => {
    const { rerender } = render(
      <TagFilter
        availableTags={["web", "billing"]}
        selectedTags={[]}
        onToggleTag={() => {}}
      />
    )

    expect(at("project-tag-filter")).not.toBeNull()
    expect(at("project-tag-filter-web")).not.toBeNull()
    expect(at("project-tag-filter-billing")).not.toBeNull()

    // The empty registry is the absent control, not an empty group.
    rerender(
      <TagFilter availableTags={[]} selectedTags={[]} onToggleTag={() => {}} />
    )
    expect(at("project-tag-filter")).toBeNull()
  })

  it("marks exactly the selected tags as pressed", () => {
    render(
      <TagFilter
        availableTags={["web", "billing"]}
        selectedTags={["web"]}
        onToggleTag={() => {}}
      />
    )

    expect(at("project-tag-filter-web")?.getAttribute("aria-pressed")).toBe(
      "true"
    )
    expect(at("project-tag-filter-billing")?.getAttribute("aria-pressed")).toBe(
      "false"
    )
  })

  it("reports the flipped tag to the owner and keeps no state of its own", () => {
    const onToggleTag = vi.fn()
    render(
      <TagFilter
        availableTags={["web", "billing"]}
        selectedTags={["web"]}
        onToggleTag={onToggleTag}
      />
    )

    fireEvent.click(at("project-tag-filter-billing") as Element)

    expect(onToggleTag).toHaveBeenCalledTimes(1)
    expect(onToggleTag).toHaveBeenCalledWith("billing")
  })
})

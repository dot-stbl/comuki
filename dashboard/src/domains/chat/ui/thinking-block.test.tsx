import { render } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { ThinkingBlock } from "@/domains/chat/ui/thinking-block"

/**
 * The two tempers of one block: moving while the turn moves, folded when it
 * is history. What each case pins is the *honesty* of the marks — a check
 * only on lines the brain moved past, the spinner only on the line it is on,
 * and neither of them anywhere once the turn is settled.
 */

const WORKING_OUT = [
  'memory.search("идемпотентность вебхуков") — 2 факта',
  "Проверил, что ключ берётся из заголовка.",
  'read_code("src/webhooks/stripe.ts") читаю',
].join("\n")

describe("while the turn is arriving", () => {
  it("draws the steps open, in the working-out's own words", () => {
    render(<ThinkingBlock text={WORKING_OUT} active />)

    const block = document.querySelector('[data-test="chat-thinking"]')
    expect(block?.tagName).toBe("DIV")
    expect(block?.getAttribute("data-active")).toBe("true")

    const steps = block?.querySelectorAll('[data-test="chat-thinking-step"]')
    expect(steps).toHaveLength(3)
    expect(block?.textContent).toContain('memory.search("идемпотентность вебхуков")')
    expect(block?.textContent).toContain("Проверил, что ключ берётся из заголовка.")
  })

  it("spins only the newest line; the finished ones keep their checks", () => {
    const { container } = render(<ThinkingBlock text={WORKING_OUT} active />)

    const marks = container.querySelectorAll("svg")
    // The last step's mark is the spinner (the loader's class is on it);
    // every step before it carries the check.
    expect(marks).toHaveLength(3)
    const spinner = marks[marks.length - 1]
    expect(spinner?.getAttribute("class")).toContain("stepRunningIcon")
    for (const mark of Array.from(marks).slice(0, -1)) {
      expect(mark.getAttribute("class")).toContain("stepDoneIcon")
    }
  })
})

describe("once the turn has settled", () => {
  it("folds the same lines away as evidence, closed by default", () => {
    render(<ThinkingBlock text={WORKING_OUT} tokens={1840} />)

    const details = document.querySelector(
      '[data-test="chat-thinking"]'
    ) as HTMLDetailsElement
    expect(details.tagName).toBe("DETAILS")
    expect(details.open).toBe(false)
    expect(details.textContent).toContain("1,840 tokens")

    // The steps are there behind the disclosure — reachable, not removed.
    expect(
      details.querySelectorAll('[data-test="chat-thinking-step"]')
    ).toHaveLength(3)
  })

  it("checks every step — history does not spin", () => {
    const { container } = render(<ThinkingBlock text={WORKING_OUT} />)

    for (const mark of Array.from(container.querySelectorAll("svg"))) {
      expect(mark.getAttribute("class")).toContain("stepDoneIcon")
    }
  })
})

import { render } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { AppShellTwoPaneOuter } from "@/app/layout/app-shell-two-pane-sidebar"
import { productNavSections } from "@/app/layout/nav-sections"
import { i18n, loadLocale } from "@/shared/i18n"
import { TestSession } from "@/shared/session/test-session"

/* CSS Module compilation cannot be observed through jsdom — `getComputedStyle`
   returns browser defaults rather than the stylesheet declarations, and
   `document.styleSheets` does not necessarily contain the module's rules.
   The rules that drive visual width, height, padding and active state are
   covered by manual browser checks instead — a CSS module test that passed
   here would not prove anything on screen. */

describe("AppShellTwoPaneOuter — section buttons in the DOM", () => {
  it("renders one button per visible section", () => {
    const { container } = render(
      <TestSession roles={["platform-admin"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={() => {}}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    expect(buttons.length).toBe(4)
    const labels = [...buttons].map((b) => b.getAttribute("aria-label"))
    expect(labels).toEqual(["Integrations", "Observe", "Configure", "Platform"])
  })

  it("marks the active section button", () => {
    const { container } = render(
      <TestSession roles={["platform-admin"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={() => {}}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    const active = [...buttons].find(
      (b) => b.getAttribute("aria-current") === "true"
    )
    expect(active?.getAttribute("aria-label")).toBe("Observe")
  })

  it("filters out sections the session cannot see", () => {
    // A viewer keeps only Observe.
    const { container } = render(
      <TestSession roles={["viewer"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={() => {}}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    expect(buttons.length).toBe(1)
    expect(buttons[0].getAttribute("aria-label")).toBe("Observe")
  })

  it("renders an SVG icon inside each button", () => {
    const { container } = render(
      <TestSession roles={["platform-admin"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={() => {}}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    for (const button of buttons) {
      expect(button.querySelector("svg")).not.toBeNull()
    }
  })

  it("invokes onSelect with the clicked section", () => {
    const calls: string[] = []
    const { container } = render(
      <TestSession roles={["platform-admin"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={(section) => calls.push(section.id)}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    const configureButton = [...buttons].find(
      (b) => b.getAttribute("aria-label") === "Configure"
    )
    configureButton?.dispatchEvent(new MouseEvent("click", { bubbles: true }))
    expect(calls).toEqual(["configure"])
  })
})

/* The rail's words are a property of the reader, not of the data: the ru
   catalogue lands through the same lazy door the switcher uses, and every
   section is named in russian while hrefs, ids and icons stay as they were.
   Language resets after the case so the file's other readings keep their
   en posture. */
describe("AppShellTwoPaneOuter in russian", () => {
  afterEach(async () => {
    await i18n.changeLanguage("en")
  })

  it("names the sections in russian, active mark and all", async () => {
    await loadLocale("ru")
    await i18n.changeLanguage("ru")

    const { container } = render(
      <TestSession roles={["platform-admin"]}>
        <AppShellTwoPaneOuter
          sections={productNavSections}
          activeId="observe"
          onSelect={() => {}}
        />
      </TestSession>
    )

    const buttons = container.querySelectorAll('[data-test="two-pane-section"]')
    expect([...buttons].map((b) => b.getAttribute("aria-label"))).toEqual([
      "Интеграции",
      "Наблюдение",
      "Настройка",
      "Платформа",
    ])
    const active = [...buttons].find(
      (b) => b.getAttribute("aria-current") === "true"
    )
    expect(active?.getAttribute("aria-label")).toBe("Наблюдение")
  })
})

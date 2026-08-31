/* The palette control on its own.
 *
 * It is deliberately a second control rather than a longer version of the mode
 * menu: the questions "which palette" and "which room" are independent, and a
 * single list of twelve would answer both every time either was asked.
 */
import { readFileSync } from "node:fs"
import { dirname, join } from "node:path"
import { fileURLToPath } from "node:url"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { ThemeProvider } from "@/app/theme-provider"

import { ThemePicker } from "./theme-picker"
import { THEMES } from "./themes"

const MODE_KEY = "picker-mode"
const THEME_KEY = "picker-mode-name"

function mount() {
  return render(
    <ThemeProvider defaultTheme="dark" storageKey={MODE_KEY}>
      <ThemePicker />
    </ThemeProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.className = ""
  document.documentElement.removeAttribute("data-theme")
})

describe("the trigger says what is showing without being opened", () => {
  it("names the current palette in its label", async () => {
    localStorage.setItem(THEME_KEY, "blueprint")
    mount()

    expect(
      await screen.findByRole("button", { name: "Palette — Blueprint" })
    ).not.toBeNull()
  })

  it("falls back to the first palette rather than rendering nothing", () => {
    localStorage.setItem(THEME_KEY, "a-theme-that-was-removed")
    mount()

    expect(document.querySelector('[data-test="theme-picker"]')).not.toBeNull()
  })
})

describe("the menu offers every palette in the registry", () => {
  it("lists all of them, each named without printing its name", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Palette/ }))

    for (const theme of THEMES) {
      const card = await screen.findByRole("menuitemradio", {
        name: theme.name,
      })
      // The card carries no words at all — the palette is the reading, and the
      // name reaches a screen reader and a pointer without taking room from
      // the thing being chosen.
      expect(card.textContent).toBe("")
      expect(card.getAttribute("aria-label")).toBe(theme.name)
    }
  })

  it("marks the current one, and only that one", async () => {
    const user = userEvent.setup()
    localStorage.setItem(THEME_KEY, "bureau")
    mount()

    await user.click(screen.getByRole("button", { name: /^Palette/ }))
    const items = await screen.findAllByRole("menuitemradio")
    const selected = items.filter(
      (item) => item.getAttribute("aria-checked") === "true"
    )

    expect(selected).toHaveLength(1)
    // Named, not printed: the card is wordless, so the mark and the accessible
    // name are the only two things saying which theme is on.
    expect(selected[0]?.getAttribute("aria-label")).toBe("Bureau")
  })

  it("draws each card from its own palette rather than from the live one", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Palette/ }))
    const item = await screen.findByRole("menuitemradio", {
      name: "Dockside",
    })
    const swatch = item

    // Every colour arrives as a `--preview-*` token published by the theme
    // sheet, so the swatch shows dockside while the board is wearing something
    // else — and so no component holds a hex.
    expect(swatch?.getAttribute("style")).toContain("--preview-dockside-floor")
    expect(swatch?.getAttribute("style")).toContain("--preview-dockside-failed")
  })
})

describe("choosing a palette changes exactly one axis", () => {
  it("applies and remembers the palette, and leaves the mode alone", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Palette/ }))
    await user.click(
      await screen.findByRole("menuitemradio", { name: /Aperture/ })
    )

    expect(document.documentElement.getAttribute("data-theme")).toBe("aperture")
    expect(localStorage.getItem(THEME_KEY)).toBe("aperture")
    expect(document.documentElement.classList.contains("dark")).toBe(true)
    expect(localStorage.getItem(MODE_KEY)).toBe(null)
  })
})

describe("the control follows the house rules it cannot be seen breaking", () => {
  const sheet = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), "theme-picker.module.css"),
    "utf8"
  )

  it("shouts at nobody", () => {
    // No `text-transform`, and no capitals in the copy either — the registry's
    // names and notes are what this control renders.
    expect(sheet).not.toMatch(/text-transform/)
    for (const theme of THEMES) {
      expect({
        id: theme.id,
        shouty: /\b[A-Z]{2,}\b/.test(`${theme.name} ${theme.note}`),
      }).toEqual({ id: theme.id, shouty: false })
    }
  })

  it("takes a corner from the scale and never the retired one", () => {
    expect(sheet).not.toMatch(/--r-pill/)
    expect(sheet).not.toMatch(/#[0-9a-fA-F]{3,8}\b/)
  })
})

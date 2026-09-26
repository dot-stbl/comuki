import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"
import { useTranslation } from "react-i18next"

import { DEFAULT_LOCALE, i18n, loadLocale } from "@/shared/i18n"
import { LOCALES } from "@/shared/i18n/locales"

import { LocaleSwitcher } from "./locale-switcher"

/* The control beside the appearance control: it enumerates the registry,
   lands the chosen locale's lazy chunk, and persists the choice. The
   probe renders one translated shell key so a switch is observable
   without mounting a whole screen. */

function Probe() {
  const { t } = useTranslation("shell")
  return <p data-test="locale-probe">{t("locale.label")}</p>
}

function mount() {
  return render(
    <>
      <LocaleSwitcher />
      <Probe />
    </>
  )
}

const at = (test: string) =>
  document.querySelector<HTMLElement>(`[data-test="${test}"]`)

const all = (pattern: string) => [
  ...document.querySelectorAll<HTMLElement>(`[data-test^="${pattern}"]`),
]

afterEach(async () => {
  await i18n.changeLanguage(DEFAULT_LOCALE)
  localStorage.clear()
})

describe("the default boot", () => {
  it("renders English immediately, with no non-default locale chunk requested", () => {
    mount()

    // The instance initialises synchronously with `en`, so the probe reads
    // a real English word, not a key path — and nothing has landed a
    // non-default locale's bundles (the lazy glob's door is `loadLocale`,
    // which nothing has called).
    expect(at("locale-probe")?.textContent).toBe("Language")
    expect(document.documentElement.lang).toBe("en")
    expect(i18n.hasResourceBundle("ru", "shell")).toBe(false)
    expect(i18n.hasResourceBundle("ja", "shell")).toBe(false)
  })
})

describe("the menu", () => {
  it("enumerates exactly the shipped set, each by its native endonym", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Language/ }))

    await waitFor(() => expect(all("locale-option-").length).toBeGreaterThan(0))
    const items = all("locale-option-")
    expect(items.length).toBe(LOCALES.length)
    for (const locale of LOCALES) {
      expect(at(`locale-option-${locale.code}`)?.textContent).toBe(
        locale.endonym
      )
    }
  })

  it("marks the active locale, and only it", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Language/ }))
    const selected = (await screen.findAllByRole("menuitemradio")).filter(
      (item) => item.getAttribute("aria-checked") === "true"
    )

    expect(selected).toHaveLength(1)
    expect(selected[0]?.getAttribute("data-test")).toBe("locale-option-en")
  })
})

describe("switching", () => {
  it("re-renders in Russian, syncs the document, and keeps the choice", async () => {
    const user = userEvent.setup()
    mount()

    await user.click(screen.getByRole("button", { name: /^Language/ }))
    await user.click(at("locale-option-ru") as HTMLElement)

    /* The ru catalogues land through 23 lazy dynamic imports; under a full
       parallel suite run Vite's transform of those chunks alone can exceed
       waitFor's 1s default, so the budget names the known-slow path rather
       than the flake's observed ceiling on an idle machine. */
    await waitFor(() => expect(at("locale-probe")?.textContent).toBe("Язык"), {
      timeout: 10_000,
    })
    expect(document.documentElement.lang).toBe("ru")
    expect(localStorage.getItem("comuki-locale")).toBe("ru")
  })

  it("survives a remount — the board reopens where it was left", async () => {
    // The arrange goes through the same door the control uses.
    await loadLocale("ru")
    await i18n.changeLanguage("ru")

    const first = mount()
    expect(at("locale-probe")?.textContent).toBe("Язык")
    first.unmount()

    mount()
    // The trigger names the active language in its own words: the choice
    // lived in storage, and the instance kept it across the remount.
    expect(screen.getByRole("button", { name: "Язык — Русский" })).toBeTruthy()
    expect(at("locale-probe")?.textContent).toBe("Язык")
  })
})

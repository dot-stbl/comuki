import { cleanup, render, screen } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { ForbiddenState } from "@/shared/ui/screen-state/forbidden-state"
import { Skeleton } from "@/shared/ui/skeleton"
import { i18n, loadLocale } from "@/shared/i18n"

/* The kit's own ru-render pin (`dashboard-i18n` wave 6): the shared
   primitives resolve their words through the kit catalogue, so a locale
   switch re-renders them in the active language — aria labels included,
   per the spec's accessibility scenario. */

afterEach(async () => {
  cleanup()
  await i18n.changeLanguage("en")
})

describe("the shared kit in russian", () => {
  it("localises the skeleton's polite loading announcement", async () => {
    await loadLocale("ru")
    await i18n.changeLanguage("ru")

    render(<Skeleton />)

    expect(screen.getByRole("status").getAttribute("aria-label")).toBe(
      "загрузка"
    )
  })

  it("localises the forbidden state the kit writes for the caller", async () => {
    await loadLocale("ru")
    await i18n.changeLanguage("ru")

    render(<ForbiddenState needs="нужен approver" />)

    expect(screen.getByText("этот экран закрыт для ваших ролей")).toBeTruthy()
    expect(
      screen.getByText(
        "нужен approver — попросите роль или перейдите в проект, где она у вас уже есть."
      )
    ).toBeTruthy()
  })

  it("keeps the english reading under the en locale", () => {
    render(<ForbiddenState needs="needs approver" />)

    expect(screen.getByText("This view is closed to your roles")).toBeTruthy()
  })
})

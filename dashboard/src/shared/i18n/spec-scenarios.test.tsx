import { cleanup, render, screen } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { Button } from "@/shared/ui"
import { ScreenState } from "@/shared/ui/screen-state/screen-state"
import { i18n, loadLocale } from "@/shared/i18n"
import { requestFailureMessage } from "@/shared/api/problem"

/* Pins the spec's end-to-end scenarios that do not already live in a
   surface's own test file: the missing-key path (spec "Unknown key does
   not blank the screen"), the `lang` attribute sync (spec "Document
   language follows the locale") and the copy boundary around API error
   detail (spec "API error detail passes through").

   The registry-in-russian scenario (R1) is pinned by
   `domains/projects/pages/project-detail-page.test.tsx` ("renders the
   registry chrome ... in russian"); the switcher/persistence scenarios by
   `app/layout/locale-switcher.test.tsx`; the en-default boot by the same
   file's boot assertion. */

afterEach(async () => {
  cleanup()
  await i18n.changeLanguage("en")
})

describe("spec scenarios: fallback and missing keys", () => {
  it("renders the full ns:key path for a key no resource carries", () => {
    // The honest failure: the operator sees the key path itself — never a
    // blank, never a crash (spec R3 / design D8).
    expect(i18n.t("kit:no.such.key.exists", { ns: "kit" })).toBe(
      "kit:no.such.key.exists"
    )
    expect(i18n.t("common:also.not.here", { ns: "common" })).toBe(
      "common:also.not.here"
    )
  })

  it("falls back to the english value when a locale omits a key", async () => {
    // The en catalogue is the parity-enforced source; the fallback chain
    // is what keeps an untranslated-but-shipped key legible (spec R2 of
    // the fallback requirement).
    await loadLocale("ru")
    await i18n.changeLanguage("ru")
    expect(i18n.t("relativeTime.now", { ns: "common" })).toBe("только что")
  })
})

describe("spec scenarios: document language follows the locale", () => {
  it("syncs documentElement.lang on every switch", async () => {
    expect(document.documentElement.lang).toBe("en")

    await loadLocale("ru")
    await i18n.changeLanguage("ru")
    expect(document.documentElement.lang).toBe("ru")

    await loadLocale("zh-CN")
    await i18n.changeLanguage("zh-CN")
    expect(document.documentElement.lang).toBe("zh-CN")
  })
})

describe("spec scenarios: api error detail passes through untranslated", () => {
  it("renders the wire's detail sentence verbatim inside russian chrome", async () => {
    await loadLocale("ru")
    await i18n.changeLanguage("ru")

    const failure = Object.assign(new Error("request failed 501"), {
      data: { detail: "the claim loop has no per-worker drain flag" },
    })

    render(
      <ScreenState
        kind="error"
        title={i18n.t("home:header.errorTitle", { ns: "home" })}
        description={requestFailureMessage(
          failure,
          i18n.t("errors.unknown", { ns: "common" })
        )}
        action={<Button>{i18n.t("actions.retry", { ns: "common" })}</Button>}
      />
    )

    // The chrome is russian; the API's own sentence is english, verbatim.
    expect(screen.getByText("Смена не загрузилась")).toBeTruthy()
    expect(
      screen.getByText("the claim loop has no per-worker drain flag")
    ).toBeTruthy()
    expect(screen.getByRole("button", { name: "повторить" })).toBeTruthy()
    expect(screen.queryByText("неизвестная ошибка")).toBeNull()
  })
})

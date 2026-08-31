import { describe, expect, it } from "vitest"

import { SETTINGS_TABS, isSettingsTab } from "@/domains/settings/model/tabs"

describe("the sections the address bar may name", () => {
  it("accepts each of the seven", () => {
    for (const tab of SETTINGS_TABS) {
      expect(isSettingsTab(tab)).toBe(true)
    }
  })

  it("refuses anything else, so a hand-typed url falls back rather than throws", () => {
    for (const value of ["", "Apps", "budget", 3, null, undefined, {}]) {
      expect(isSettingsTab(value)).toBe(false)
    }
  })
})

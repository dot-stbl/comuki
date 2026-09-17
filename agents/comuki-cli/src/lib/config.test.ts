import { describe, expect, it } from "bun:test"
import { DEFAULT_URL, resolveConfig } from "./config"

describe("resolveConfig", () => {
  it("falls back to the default url when nothing is set", () => {
    expect(resolveConfig()).toEqual({ url: DEFAULT_URL })
  })

  it("strips trailing slashes from the url", () => {
    expect(resolveConfig({ COMUKI_URL: "http://host:8080///" }).url).toBe(
      "http://host:8080"
    )
  })

  it("prefers env over the config file", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env", COMUKI_API_KEY: "ck_env" },
      { url: "http://file", apiKey: "ck_file" }
    )
    expect(config.url).toBe("http://env")
    expect(config.apiKey).toBe("ck_env")
  })

  it("prefers cli overrides over env", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env", COMUKI_API_KEY: "ck_env" },
      {},
      { url: "http://override", apiKey: "ck_override", project: "nova" }
    )
    expect(config.url).toBe("http://override")
    expect(config.apiKey).toBe("ck_override")
    expect(config.defaultProject).toBe("nova")
  })

  it("reads the config file when env is silent", () => {
    const config = resolveConfig(
      {},
      {
        url: "http://file",
        apiKey: "ck_file",
        tenant: "acme",
        cookie: "session=abc",
        defaultProject: "nova",
      }
    )
    expect(config).toEqual({
      url: "http://file",
      apiKey: "ck_file",
      tenant: "acme",
      cookie: "session=abc",
      defaultProject: "nova",
    })
  })

  it("resolves the project from COMUKI_PROJECT env", () => {
    expect(resolveConfig({ COMUKI_PROJECT: "nova" }).defaultProject).toBe(
      "nova"
    )
  })

  it("drops blank strings to undefined", () => {
    const config = resolveConfig({ COMUKI_URL: "  ", COMUKI_API_KEY: "" })
    expect(config.url).toBe(DEFAULT_URL)
    expect(config.apiKey).toBeUndefined()
  })
})

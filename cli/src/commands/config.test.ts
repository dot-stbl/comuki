import { describe, expect, it } from "bun:test"
import {
  displayPath,
  formatConfigShow,
  maskApiKey,
  maskCookie,
  resolveUrlDisplay,
  type ConfigShowInput,
} from "./config"

const HOME = "/home/tester"

function input(partial: Partial<ConfigShowInput> = {}): ConfigShowInput {
  return {
    overrides: {},
    env: {},
    file: {},
    configPath: "/home/tester/.config/comuki/config.json",
    configExists: true,
    sessionsPath: "/home/tester/.config/comuki/sessions.json",
    sessionsExists: false,
    ...partial,
  }
}

describe("maskApiKey", () => {
  it("keeps only the first 8 chars plus an ellipsis", () => {
    expect(maskApiKey("ck_z6bc48d1secretpart")).toBe("ck_z6bc4…")
  })

  it("still appends the ellipsis for a short key", () => {
    expect(maskApiKey("ck_short")).toBe("ck_short…")
  })
})

describe("maskCookie", () => {
  it("shows only the cookie name before the equals sign", () => {
    expect(maskCookie("comuki.auth=opaque-session-value")).toBe(
      "comuki.auth=***"
    )
  })

  it("degrades to bare stars when the name is empty", () => {
    expect(maskCookie("=value")).toBe("***")
  })
})

describe("resolveUrlDisplay", () => {
  it("prefers arg over env and file", () => {
    expect(
      resolveUrlDisplay(
        { url: "http://arg" },
        { COMUKI_URL: "http://env" },
        { url: "http://file" }
      )
    ).toEqual({ url: "http://arg", source: "arg" })
  })

  it("falls back to env", () => {
    expect(
      resolveUrlDisplay({}, { COMUKI_URL: "http://env" }, { url: "http://file" })
    ).toEqual({ url: "http://env", source: "env" })
  })

  it("falls back to the config file for display", () => {
    expect(resolveUrlDisplay({}, {}, { url: "http://file" })).toEqual({
      url: "http://file",
      source: "file",
    })
  })

  it("reports none when unset everywhere", () => {
    expect(resolveUrlDisplay({}, {}, {})).toEqual({
      url: undefined,
      source: "none",
    })
  })

  it("treats blank values as missing", () => {
    expect(
      resolveUrlDisplay({ url: "  " }, { COMUKI_URL: " " }, { url: " " }).source
    ).toBe("none")
  })
})

describe("displayPath", () => {
  it("folds the home prefix to a tilde", () => {
    expect(displayPath("/home/tester/.config/comuki/config.json", HOME)).toBe(
      "~/.config/comuki/config.json"
    )
  })

  it("leaves paths outside home untouched", () => {
    expect(displayPath("/etc/hosts", HOME)).toBe("/etc/hosts")
  })
})

describe("formatConfigShow", () => {
  it("prints aligned rows for api-key auth with arg url and file project", () => {
    const text = formatConfigShow(
      input({
        overrides: {
          url: "http://comuki.example",
          apiKey: "ck_z6bc48d1secret",
        },
        file: { defaultProject: "nova" },
      }),
      HOME
    )
    expect(text).toBe(
      [
        "url        http://comuki.example (source: arg)",
        "auth       api-key ck_z6bc4…",
        "project    nova",
        "config     ~/.config/comuki/config.json (exists)",
        "sessions   ~/.config/comuki/sessions.json (missing)",
      ].join("\n")
    )
    expect(text).not.toContain("d1secret")
  })

  it("prints cookie auth masked to the cookie name", () => {
    const text = formatConfigShow(
      input({
        env: { COMUKI_URL: "http://env" },
        file: { cookie: "comuki.auth=super-secret" },
      }),
      HOME
    )
    expect(text.split("\n")[1]).toBe("auth       cookie comuki.auth=***")
    expect(text).not.toContain("super-secret")
  })

  it("prefers the api key over the cookie when both are set", () => {
    const text = formatConfigShow(
      input({
        env: { COMUKI_API_KEY: "ck_env1234" },
        file: { cookie: "comuki.auth=x" },
      }),
      HOME
    )
    expect(text.split("\n")[1]).toBe("auth       api-key ck_env12…")
  })

  it("prints anonymous when no credential is configured", () => {
    const text = formatConfigShow(input(), HOME)
    expect(text.split("\n")[1]).toBe("auth       anonymous")
  })

  it("prints an em dash url with source none when unset everywhere", () => {
    const text = formatConfigShow(input(), HOME)
    expect(text.split("\n")[0]).toBe("url        — (source: none)")
  })

  it("shows the file url source when only the config file has one", () => {
    const text = formatConfigShow(input({ file: { url: "http://file" } }), HOME)
    expect(text.split("\n")[0]).toBe("url        http://file (source: file)")
  })

  it("prints an em dash project when unset", () => {
    const text = formatConfigShow(input(), HOME)
    expect(text.split("\n")[2]).toBe("project    —")
  })

  it("resolves the project from env over file", () => {
    const text = formatConfigShow(
      input({
        env: { COMUKI_PROJECT: "env-proj" },
        file: { defaultProject: "file-proj" },
      }),
      HOME
    )
    expect(text.split("\n")[2]).toBe("project    env-proj")
  })
})

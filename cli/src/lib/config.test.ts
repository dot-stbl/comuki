import { afterEach, describe, expect, it } from "bun:test"
import { rm } from "node:fs/promises"
import { join } from "node:path"
import {
  ConfigError,
  archiveDir,
  configDir,
  configFilePath,
  decodeConfigFile,
  readConfigFile,
  resolveConfig,
  sessionsFilePath,
  writeConfigFile,
} from "./config"

const temporaryFiles: string[] = []

afterEach(async () => {
  await Promise.all(temporaryFiles.splice(0).map((path) => rm(path, { force: true })))
})

describe("decodeConfigFile", () => {
  it("rejects malformed root values", () => {
    expect(decodeConfigFile(null).ok).toBe(false)
    expect(decodeConfigFile([]).ok).toBe(false)
  })

  it("accepts partial config and preserves extra fields", () => {
    const result = decodeConfigFile({ theme: "dockside-dark", future: { v: 1 } })

    expect(result).toEqual({
      ok: true,
      value: { theme: "dockside-dark", future: { v: 1 } },
    })
  })

  it("drops known fields with wrong types", () => {
    const result = decodeConfigFile({
      apiKey: 42,
      bell: "yes",
      contextWindow: Number.NaN,
      tenant: "acme",
    })

    expect(result).toEqual({ ok: true, value: { tenant: "acme" } })
  })

  it("round-trips a valid file", async () => {
    const path = `${import.meta.dir}/config-roundtrip.tmp.json`
    temporaryFiles.push(path)
    const contents = {
      apiKey: "ck_test",
      bell: false,
      future: { enabled: true },
    }

    await writeConfigFile(contents, path)

    expect(await readConfigFile(path)).toEqual(contents)
  })

  it("treats malformed JSON as an empty config", async () => {
    const path = `${import.meta.dir}/config-malformed.tmp.json`
    temporaryFiles.push(path)
    await Bun.write(path, "{not json")

    expect(await readConfigFile(path)).toEqual({})
  })

  it("compatibility writes merge with fields added after a stale read", async () => {
    const path = `${import.meta.dir}/config-compatibility-merge.tmp.json`
    temporaryFiles.push(path)
    await writeConfigFile({ cookie: "session=old" }, path)
    const stale = await readConfigFile(path)
    await writeConfigFile({ ...stale, theme: "dockside-dark" }, path)
    await writeConfigFile({ preferredProfile: "implement" }, path)

    expect(await readConfigFile(path)).toEqual({
      cookie: "session=old",
      theme: "dockside-dark",
      preferredProfile: "implement",
    })
  })
})

describe("resolveConfig", () => {
  it("throws with hint when url is neither arg nor env", () => {
    expect(() => resolveConfig()).toThrow(ConfigError)
    expect(() => resolveConfig()).toThrow(/--url/)
    expect(() => resolveConfig()).toThrow(/COMUKI_URL/)
  })

  it("arg overrides env when both set (arg wins)", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env", COMUKI_API_KEY: "ck_env" },
      {},
      { url: "http://arg", apiKey: "ck_arg", project: "nova" }
    )
    expect(config.url).toBe("http://arg")
    expect(config.apiKey).toBe("ck_arg")
    expect(config.defaultProject).toBe("nova")
  })

  it("env fallback works when no arg (env wins)", () => {
    const config = resolveConfig({
      COMUKI_URL: "http://env",
      COMUKI_API_KEY: "ck_env",
      COMUKI_PROJECT: "nova",
    })
    expect(config.url).toBe("http://env")
    expect(config.apiKey).toBe("ck_env")
    expect(config.defaultProject).toBe("nova")
  })

  it("strips trailing slashes from the url", () => {
    expect(resolveConfig({ COMUKI_URL: "http://host:8080///" }).url).toBe(
      "http://host:8080"
    )
  })

  it("blank url from env is treated as missing and throws", () => {
    expect(() => resolveConfig({ COMUKI_URL: "  " })).toThrow(ConfigError)
  })

  it("blank url from override is treated as missing and throws", () => {
    expect(() => resolveConfig({}, {}, { url: "   " })).toThrow(ConfigError)
  })

  it("prefers env over the config file for the api key", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env", COMUKI_API_KEY: "ck_env" },
      { url: "http://file", apiKey: "ck_file" }
    )
    expect(config.url).toBe("http://env")
    expect(config.apiKey).toBe("ck_env")
  })

  it("falls back to the config file for api key / tenant / cookie / project", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env" },
      {
        apiKey: "ck_file",
        tenant: "acme",
        cookie: "session=abc",
        defaultProject: "nova",
      }
    )
    expect(config).toEqual({
      url: "http://env",
      apiKey: "ck_file",
      tenant: "acme",
      cookie: "session=abc",
      defaultProject: "nova",
      bell: true,
      contextWindow: 128_000,
    })
  })

  it("resolves the theme from override over the config file", () => {
    expect(
      resolveConfig(
        { COMUKI_URL: "http://x" },
        { theme: "graphite-light" },
        { theme: "dockside-dark" }
      ).theme
    ).toBe("dockside-dark")
    expect(
      resolveConfig({ COMUKI_URL: "http://x" }, { theme: "graphite-light" })
        .theme
    ).toBe("graphite-light")
    expect(resolveConfig({ COMUKI_URL: "http://x" }).theme).toBeUndefined()
    expect(
      resolveConfig({ COMUKI_URL: "http://x" }, {}, { theme: "  " }).theme
    ).toBeUndefined()
  })

  it("reads preferredProfile and a positive contextWindow from the file", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://x" },
      { preferredProfile: "implement", contextWindow: 64_000 }
    )
    expect(config.preferredProfile).toBe("implement")
    expect(config.contextWindow).toBe(64_000)
  })

  it("defaults contextWindow to 128k and drops a blank preferredProfile", () => {
    expect(resolveConfig({ COMUKI_URL: "http://x" }).contextWindow).toBe(
      128_000
    )
    expect(
      resolveConfig({ COMUKI_URL: "http://x" }, { preferredProfile: "  " })
        .preferredProfile
    ).toBeUndefined()
    expect(
      resolveConfig({ COMUKI_URL: "http://x" }, { contextWindow: 0 })
        .contextWindow
    ).toBe(128_000)
  })

  it("bell defaults on and only an explicit false turns it off", () => {
    expect(resolveConfig({ COMUKI_URL: "http://x" }).bell).toBe(true)
    expect(
      resolveConfig({ COMUKI_URL: "http://x" }, { bell: false }).bell
    ).toBe(false)
    expect(resolveConfig({ COMUKI_URL: "http://x" }, { bell: true }).bell).toBe(
      true
    )
  })

  it("ignores file url — only arg and env are honoured", () => {
    const config = resolveConfig(
      { COMUKI_URL: "http://env" },
      { url: "http://file" }
    )
    expect(config.url).toBe("http://env")
  })

  it("resolves the project from COMUKI_PROJECT env", () => {
    expect(
      resolveConfig({ COMUKI_URL: "http://x", COMUKI_PROJECT: "nova" })
        .defaultProject
    ).toBe("nova")
  })

  it("resolves the tenant from COMUKI_TENANT env", () => {
    expect(
      resolveConfig({ COMUKI_URL: "http://x", COMUKI_TENANT: "acme" }).tenant
    ).toBe("acme")
  })

  it("drops blank api key from any source to undefined", () => {
    const config = resolveConfig({
      COMUKI_URL: "http://x",
      COMUKI_API_KEY: "",
    })
    expect(config.apiKey).toBeUndefined()
  })
})

describe("config paths (xdg layout)", () => {
  it("places config under ~/.config/comuki by default", () => {
    const path = configFilePath()
    expect(
      path.endsWith([".config", "comuki", "config.json"].join("/")) ||
        path.endsWith([".config", "comuki", "config.json"].join("\\"))
    ).toBe(true)
  })

  it("honors XDG_CONFIG_HOME when set", () => {
    expect(configDir("/custom/xdg")).toBe(join("/custom/xdg", "comuki"))
  })

  it("falls back to ~/.config when XDG_CONFIG_HOME is blank", () => {
    const dir = configDir("  ")
    expect(dir).not.toContain("custom")
    expect(
      dir.endsWith([".config", "comuki"].join("/")) ||
        dir.endsWith([".config", "comuki"].join("\\"))
    ).toBe(true)
  })

  it("places sessions.json next to config.json", () => {
    expect(sessionsFilePath()).toBe(
      configFilePath().replace("config.json", "sessions.json")
    )
  })

  it("places the archive directory next to config.json", () => {
    expect(archiveDir("/custom/xdg")).toBe(join("/custom/xdg", "comuki", "archive"))
  })
})

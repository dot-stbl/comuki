import { afterAll, beforeAll, describe, expect, test } from "bun:test"
import {
  createMcpExtension,
  DEFAULT_MCP_SERVER_NAME,
  readMcpConfig,
} from "./mcp"
import type { PiExtensionApi, PiMcpServerConfig } from "./api"

interface McpRegistration {
  readonly name: string
  readonly config: PiMcpServerConfig
}

interface Harness {
  readonly api: PiExtensionApi
  readonly registrations: readonly McpRegistration[]
}

function makeHarness(): Harness {
  const registrations: McpRegistration[] = []
  const api: PiExtensionApi = {
    on(event, handler) {
      // The MCP extension never subscribes — silence unused-arg lints
      // so the harness can still satisfy the structural interface.
      void event
      void handler
      return () => undefined
    },
    registerMcpServer(name, config) {
      registrations.push({ name, config })
    },
  }
  return { api, registrations }
}

describe("readMcpConfig", () => {
  test("returns null when COMUKI_MCP_URL is unset", () => {
    expect(readMcpConfig({})).toBeNull()
  })

  test("returns null when COMUKI_MCP_URL is blank or whitespace", () => {
    expect(readMcpConfig({ COMUKI_MCP_URL: "" })).toBeNull()
    expect(readMcpConfig({ COMUKI_MCP_URL: "   " })).toBeNull()
  })

  test("returns the URL plus token when both are present", () => {
    expect(
      readMcpConfig({
        COMUKI_MCP_URL: "http://mcp/mcp",
        COMUKI_MCP_TOKEN: "secret",
      })
    ).toEqual({ url: "http://mcp/mcp", token: "secret" })
  })

  test("ignores a blank token", () => {
    expect(
      readMcpConfig({
        COMUKI_MCP_URL: "http://mcp/mcp",
        COMUKI_MCP_TOKEN: "  ",
      })
    ).toEqual({ url: "http://mcp/mcp", token: undefined })
  })

  test("trims surrounding whitespace from the URL", () => {
    expect(readMcpConfig({ COMUKI_MCP_URL: "  http://mcp/mcp  " })).toEqual({
      url: "http://mcp/mcp",
      token: undefined,
    })
  })
})

describe("createMcpExtension", () => {
  test("registers nothing when COMUKI_MCP_URL is unset", () => {
    const { api, registrations } = makeHarness()
    createMcpExtension({ env: {} })(api)

    expect(registrations).toEqual([])
  })

  test("matches the spec scenario — unset URL means no MCP tools", () => {
    // The pi runtime sees zero mcp__<name>__<tool> entries because the
    // extension never calls registerMcpServer when the URL is missing.
    const { api, registrations } = makeHarness()
    createMcpExtension({ env: {} })(api)

    expect(registrations).toHaveLength(0)
  })

  test("registers a comuki server when COMUKI_MCP_URL is set", () => {
    const { api, registrations } = makeHarness()
    createMcpExtension({
      env: { COMUKI_MCP_URL: "http://mcp/mcp" },
    })(api)

    expect(registrations).toHaveLength(1)
    expect(registrations[0]?.name).toBe(DEFAULT_MCP_SERVER_NAME)
    expect(registrations[0]?.config.url).toBe("http://mcp/mcp")
  })

  test("attaches the bearer token to MCP requests when COMUKI_MCP_TOKEN is set", () => {
    const { api, registrations } = makeHarness()
    createMcpExtension({
      env: {
        COMUKI_MCP_URL: "http://mcp/mcp",
        COMUKI_MCP_TOKEN: "secret-token",
      },
    })(api)

    expect(registrations).toHaveLength(1)
    expect(registrations[0]?.config.url).toBe("http://mcp/mcp")
    expect(registrations[0]?.config.headers?.authorization).toBe(
      "Bearer secret-token"
    )
  })

  test("omits the Authorization entry when no token is provided", () => {
    const { api, registrations } = makeHarness()
    createMcpExtension({
      env: { COMUKI_MCP_URL: "http://mcp/mcp" },
    })(api)

    expect(registrations[0]?.config.headers?.authorization).toBeUndefined()
  })

  test("honors a custom server name", () => {
    const { api, registrations } = makeHarness()
    createMcpExtension({
      env: { COMUKI_MCP_URL: "http://mcp/mcp" },
      serverName: "knowledge",
    })(api)

    expect(registrations[0]?.name).toBe("knowledge")
  })

  test("reads the env at construction time, not at registration time", () => {
    const envRef: { current: { COMUKI_MCP_URL?: string } } = {
      current: {},
    }
    const { api, registrations } = makeHarness()

    // Capture env at construction (URL unset)
    createMcpExtension({
      env: envRef.current,
    })(api)

    // Mutate after construction; must NOT retroactively register.
    envRef.current.COMUKI_MCP_URL = "http://later/mcp"

    expect(registrations).toEqual([])
  })

  test("returns a reusable ExtensionFactory", () => {
    const factory = createMcpExtension({
      env: { COMUKI_MCP_URL: "http://mcp/mcp" },
    })
    const first = makeHarness()
    const second = makeHarness()
    factory(first.api)
    factory(second.api)

    expect(first.registrations).toHaveLength(1)
    expect(second.registrations).toHaveLength(1)
  })
})

describe("createMcpExtension — process.env fallback (no env passed)", () => {
  // The contract: `createMcpExtension()` with no `env` reads
  // `process.env.COMUKI_MCP_URL` and `process.env.COMUKI_MCP_TOKEN` at
  // construction time — exactly once. Tests below mutate process.env
  // and restore it via beforeAll/afterAll so the suite never leaks.
  let originalUrl: string | undefined
  let originalToken: string | undefined

  beforeAll(() => {
    originalUrl = process.env.COMUKI_MCP_URL
    originalToken = process.env.COMUKI_MCP_TOKEN
  })

  afterAll(() => {
    if (originalUrl === undefined) {
      delete process.env.COMUKI_MCP_URL
    } else {
      process.env.COMUKI_MCP_URL = originalUrl
    }
    if (originalToken === undefined) {
      delete process.env.COMUKI_MCP_TOKEN
    } else {
      process.env.COMUKI_MCP_TOKEN = originalToken
    }
  })

  test("picks up COMUKI_MCP_URL from process.env when no env is passed", () => {
    process.env.COMUKI_MCP_URL = "http://from-process-env/mcp"
    delete process.env.COMUKI_MCP_TOKEN

    const { api, registrations } = makeHarness()
    createMcpExtension()(api)

    expect(registrations).toHaveLength(1)
    expect(registrations[0]?.name).toBe(DEFAULT_MCP_SERVER_NAME)
    expect(registrations[0]?.config.url).toBe("http://from-process-env/mcp")
    expect(registrations[0]?.config.headers?.authorization).toBeUndefined()
  })

  test("registers nothing when process.env.COMUKI_MCP_URL is unset", () => {
    delete process.env.COMUKI_MCP_URL
    delete process.env.COMUKI_MCP_TOKEN

    const { api, registrations } = makeHarness()
    createMcpExtension()(api)

    expect(registrations).toEqual([])
  })

  test("process.env snapshot is taken at construction, not on registration", () => {
    process.env.COMUKI_MCP_URL = "http://first-snapshot/mcp"
    const factory = createMcpExtension()

    // Mutate the env after construction; the factory must NOT pick it up.
    process.env.COMUKI_MCP_URL = "http://mutated-after-construction/mcp"

    const { api, registrations } = makeHarness()
    factory(api)

    expect(registrations).toHaveLength(1)
    expect(registrations[0]?.config.url).toBe("http://first-snapshot/mcp")
  })

  test("reads COMUKI_MCP_TOKEN from process.env when both env vars are set", () => {
    process.env.COMUKI_MCP_URL = "http://from-process-env/mcp"
    process.env.COMUKI_MCP_TOKEN = "env-token"

    const { api, registrations } = makeHarness()
    createMcpExtension()(api)

    expect(registrations).toHaveLength(1)
    expect(registrations[0]?.config.headers?.authorization).toBe(
      "Bearer env-token"
    )
  })

  test("registers nothing when only COMUKI_MCP_TOKEN is set (URL is the gate)", () => {
    // The URL gates registration — a token alone cannot summon an MCP
    // server without a target. This guards against a future change that
    // tries to make the token a sufficient signal on its own.
    delete process.env.COMUKI_MCP_URL
    process.env.COMUKI_MCP_TOKEN = "orphan-token"

    const { api, registrations } = makeHarness()
    createMcpExtension()(api)

    expect(registrations).toEqual([])
  })
})

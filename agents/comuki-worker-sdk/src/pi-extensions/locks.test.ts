import { describe, expect, test } from "bun:test"
import { BLOCKED_TOOL_TARGETS } from "@comuki/agent-core"
import type { LockRule } from "@comuki/agent-core"
import {
  createLocksExtension,
  __testing,
  gitPushRefCandidates,
} from "./locks"
import type {
  ExtensionFactory,
  PiExtensionApi,
  PiExtensionContext,
  PiToolCallEvent,
  PiToolCallResult,
} from "./api"

type ToolCallHandler = (
  event: PiToolCallEvent,
  ctx: PiExtensionContext
) => Promise<PiToolCallResult | undefined>

interface Harness {
  readonly api: PiExtensionApi
  readonly toolCall: (event: PiToolCallEvent) => Promise<PiToolCallResult | undefined>
}

function makeHarness(): Harness {
  let toolCallHandler: ToolCallHandler | undefined

  const api: PiExtensionApi = {
    on(event, handler) {
      if (event === "tool_call") {
        toolCallHandler = handler as ToolCallHandler
        return () => {
          toolCallHandler = undefined
        }
      }
      // The locks extension never registers resources_discover — silence
      // the unused branches by acknowledging them explicitly.
      void event
      void handler
      return () => undefined
    },
    registerMcpServer(name, config) {
      void name
      void config
    },
  }

  const toolCall = async (
    event: PiToolCallEvent
  ): Promise<PiToolCallResult | undefined> => {
    if (toolCallHandler === undefined) {
      throw new Error("locks extension did not register a tool_call handler")
    }
    return toolCallHandler(event, { hasUI: false })
  }

  return { api, toolCall }
}

function runLocks(
  event: PiToolCallEvent,
  profileLocks?: readonly LockRule[]
): Promise<PiToolCallResult | undefined> {
  const factory: ExtensionFactory = createLocksExtension({ profileLocks })
  const { api, toolCall } = makeHarness()
  factory(api)
  return toolCall(event)
}

describe("createLocksExtension", () => {
  test("registers a tool_call handler on the pi api", () => {
    let registered = 0
    const api: PiExtensionApi = {
      on(event, handler) {
        if (event === "tool_call") {
          registered++
        }
        void handler
        return () => undefined
      },
      registerMcpServer(name, config) {
        void name
        void config
      },
    }

    createLocksExtension()(api)

    expect(registered).toBe(1)
  })

  test("returned factory is reusable — calling it twice is safe", () => {
    const factory = createLocksExtension()
    const first = makeHarness()
    const second = makeHarness()
    factory(first.api)
    factory(second.api)

    expect(first.api).toBeDefined()
    expect(second.api).toBeDefined()
  })
})

describe("edit-path enforcement", () => {
  const cases: { readonly path: string; readonly ruleId: string }[] = [
    { path: "src/app.test.ts", ruleId: "no-edit-tests" },
    { path: "deep/pkg/foo.spec.tsx", ruleId: "no-edit-spec" },
    { path: "tests/unit/parser.test.js", ruleId: "no-edit-tests-dir" },
    { path: "src/__tests__/helper.ts", ruleId: "no-edit-underscore-tests-dir" },
  ]

  test.each(cases)("denies $path via %s", async ({ path, ruleId }) => {
    const denied = await runLocks({ toolName: "edit", input: { path } })
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBe(
      BLOCKED_TOOL_TARGETS.find((r) => r.id === ruleId)?.reason
    )
  })

  test("write tool hits the same locks as edit", async () => {
    const denied = await runLocks({
      toolName: "write",
      input: { path: "src/foo.test.ts", content: "" },
    })
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBeDefined()
  })

  test("allows editing regular source files", async () => {
    expect(
      await runLocks({ toolName: "edit", input: { path: "src/foo.ts" } })
    ).toBeUndefined()
  })

  test("normalizes windows separators in paths", async () => {
    const denied = await runLocks({
      toolName: "edit",
      input: { path: "src\\deep\\foo.test.ts" },
    })
    expect(denied?.block).toBe(true)
  })

  test("ignores write/edit calls without a path argument", async () => {
    expect(
      await runLocks({ toolName: "write", input: { content: "" } })
    ).toBeUndefined()
    expect(
      await runLocks({ toolName: "edit", input: {} })
    ).toBeUndefined()
  })
})

describe("tool-name enforcement (installs and side installs)", () => {
  const commands: string[] = [
    "npm install zod",
    "npm add zod",
    "bun add @types/bun",
    "bun install",
    "pnpm add left-pad",
    "yarn add left-pad",
    "pip install requests",
    "dotnet add package Newtonsoft.Json",
  ]

  test.each(commands)('denies "Bash($command)"', async (command) => {
    const denied = await runLocks({ toolName: "bash", input: { command } })
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBeDefined()
  })

  test("allows ordinary package-manager scripts", async () => {
    expect(
      await runLocks({ toolName: "bash", input: { command: "npm run build" } })
    ).toBeUndefined()
    expect(
      await runLocks({ toolName: "bash", input: { command: "bun test" } })
    ).toBeUndefined()
  })

  test("ignores bash calls without a string command", async () => {
    expect(
      await runLocks({ toolName: "bash", input: { command: 42 } })
    ).toBeUndefined()
    expect(
      await runLocks({ toolName: "bash", input: {} })
    ).toBeUndefined()
  })
})

describe("git-ref enforcement (push to protected branches)", () => {
  const protectedPushes: string[] = [
    "git push origin main",
    "git push origin master",
    "git push origin HEAD:main",
    "git push --force origin main",
    "git -C packages/api push origin main",
    "cd packages/api && git push origin main",
    "git push origin refs/heads/main",
  ]

  test.each(protectedPushes)('denies "Bash($command)"', async (command) => {
    const denied = await runLocks({ toolName: "bash", input: { command } })
    expect(denied?.block).toBe(true)
    expect(
      ["no-push-main", "no-push-master"].some((id) =>
        BLOCKED_TOOL_TARGETS.some((r) => r.id === id && r.reason === denied?.reason)
      )
    ).toBe(true)
  })

  test("allows pushes to feature branches and non-push mentions", async () => {
    expect(
      await runLocks({
        toolName: "bash",
        input: { command: "git push origin feature/dev-sdk" },
      })
    ).toBeUndefined()
    expect(
      await runLocks({
        toolName: "bash",
        input: { command: 'git commit -m "push main now"' },
      })
    ).toBeUndefined()
  })

  test("ignores non-bash tools entirely", async () => {
    expect(
      await runLocks({
        toolName: "read",
        input: { file_path: "src/foo.test.ts" },
      })
    ).toBeUndefined()
  })
})

describe("profile locks are appended to the default set", () => {
  const profileLock: LockRule = {
    id: "profile-no-touch-AGENTS",
    kind: "edit-path",
    pattern: "**/AGENTS.md",
    reason: "AGENTS.md is the project boot contract",
  }

  const profileToolLock: LockRule = {
    id: "profile-no-touch-prod-secret",
    kind: "tool-name",
    pattern: "Bash(cat /etc/prod-secret*)",
    reason: "Prod secrets live behind the orchestrator, not the worker shell",
  }

  const profileGitLock: LockRule = {
    id: "profile-no-push-release",
    kind: "git-ref",
    pattern: "refs/heads/release/**",
    reason: "Release branches are bumped by the release pipeline, not by agents",
  }

  test("profile rule denies on top of the default locks", async () => {
    const denied = await runLocks(
      { toolName: "edit", input: { path: "AGENTS.md" } },
      [profileLock]
    )
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBe(profileLock.reason)
  })

  test("profile rule does not shadow the default locks", async () => {
    const denied = await runLocks(
      { toolName: "edit", input: { path: "src/foo.test.ts" } },
      [profileLock]
    )
    expect(denied?.block).toBe(true)
    expect(denied?.reason).not.toBe(profileLock.reason)
  })

  test("without profile locks the gate uses the default ruleset only", async () => {
    const denied = await runLocks({
      toolName: "edit",
      input: { path: "AGENTS.md" },
    })
    expect(denied).toBeUndefined()
  })

  test("profile tool-name lock extends the default tool-name set", async () => {
    const denied = await runLocks(
      { toolName: "bash", input: { command: "cat /etc/prod-secret" } },
      [profileToolLock]
    )
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBe(profileToolLock.reason)
  })

  test("profile tool-name lock does not match default-only patterns", async () => {
    // Without the profile lock, `npm install zod` is denied by the default
    // rule. With the profile lock added, the default rule still fires first
    // (defaults come before profile locks in the merged array) — this
    // confirms the merge appends without losing defaults.
    const denied = await runLocks(
      { toolName: "bash", input: { command: "npm install zod" } },
      [profileToolLock]
    )
    expect(denied?.block).toBe(true)
    expect(denied?.reason).not.toBe(profileToolLock.reason)
  })

  test("profile git-ref lock extends the default git-ref set", async () => {
    const denied = await runLocks(
      { toolName: "bash", input: { command: "git push origin release/1.2" } },
      [profileGitLock]
    )
    expect(denied?.block).toBe(true)
    expect(denied?.reason).toBe(profileGitLock.reason)
  })

  test("profile locks are independent of each other (kind is disjoint)", async () => {
    // An edit-path profile lock does NOT trigger on a bash push — confirms
    // the append uses findPathLock vs findToolLock vs findGitRefLock
    // correctly per kind.
    const allowed = await runLocks(
      { toolName: "bash", input: { command: "git push origin AGENTS.md" } },
      [profileLock]
    )
    expect(allowed).toBeUndefined()
  })
})

describe("gitPushRefCandidates", () => {
  test("extracts the ref after the remote", () => {
    expect(gitPushRefCandidates("git push origin main")).toEqual([
      "main",
      "refs/heads/main",
    ])
  })

  test("treats a single positional as the refspec", () => {
    expect(gitPushRefCandidates("git push main")).toEqual([
      "main",
      "refs/heads/main",
    ])
  })

  test("unwraps src:dst refspecs to the destination", () => {
    expect(gitPushRefCandidates("git push origin HEAD:main")).toContain(
      "refs/heads/main"
    )
  })

  test("keeps fully-qualified refs as-is", () => {
    expect(
      gitPushRefCandidates("git push origin refs/heads/master")
    ).toContain("refs/heads/master")
  })

  test("ignores non-push git commands and non-git commands", () => {
    expect(gitPushRefCandidates("git commit -m push")).toEqual([])
    expect(gitPushRefCandidates("git status")).toEqual([])
    expect(gitPushRefCandidates("docker push main")).toEqual([])
  })
})

describe("input-shape tolerance (write/edit/bash dispatch)", () => {
  // Guards that gate the handler on a real string before any lock lookup.
  // Without them a malformed input shape (or a future tool that emits
  // `path: undefined`) would silently bypass the gate.

  test.each(["write", "edit"])(
    "%s with empty-string path is ignored",
    async (toolName) => {
      expect(
        await runLocks({ toolName, input: { path: "" } })
      ).toBeUndefined()
    }
  )

  test.each(["write", "edit"])(
    "%s with numeric path is ignored",
    async (toolName) => {
      expect(
        await runLocks({ toolName, input: { path: 42 } })
      ).toBeUndefined()
    }
  )

  test.each(["write", "edit"])(
    "%s with null path is ignored",
    async (toolName) => {
      expect(
        await runLocks({ toolName, input: { path: null } })
      ).toBeUndefined()
    }
  )

  test.each(["write", "edit"])(
    "%s with explicit undefined path is ignored",
    async (toolName) => {
      expect(
        await runLocks({ toolName, input: { path: undefined } })
      ).toBeUndefined()
    }
  )

  test("bash with empty-string command is ignored (no lock match)", async () => {
    expect(
      await runLocks({ toolName: "bash", input: { command: "" } })
    ).toBeUndefined()
  })
})

describe("enforceLocks direct (pure-function smoke check)", () => {
  test("returns undefined for tools outside the gate's surface", () => {
    expect(
      __testing.enforceLocks(
        { toolName: "read", input: { file_path: "src/foo.test.ts" } },
        BLOCKED_TOOL_TARGETS
      )
    ).toBeUndefined()
  })

  test("matches the platform scenario from the spec — edit on src/app.test.ts", async () => {
    // Spec scenario: WHEN pi attempts to edit `src/app.test.ts` THEN the
    // pi-extension denies the call with the platform-gate reason BEFORE
    // the write. The handler returns synchronously enough that no I/O
    // happens before the block.
    const denied = await runLocks({
      toolName: "edit",
      input: { path: "src/app.test.ts" },
    })
    expect(denied).toEqual({
      block: true,
      reason: BLOCKED_TOOL_TARGETS.find((r) => r.id === "no-edit-tests")
        ?.reason,
    })
  })
})
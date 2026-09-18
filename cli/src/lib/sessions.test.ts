import { describe, expect, it } from "bun:test"
import { rm } from "node:fs/promises"
import {
  PENDING_PREFIX,
  addSession,
  adoptServerId,
  appendBlocks,
  appendHistory,
  appendLiveText,
  branchOpener,
  decodePersistedSessions,
  forkTitle,
  filterSessions,
  fromPersisted,
  markUnread,
  newPendingSession,
  patchSession,
  readSessionsFile,
  removeSession,
  renameSession,
  retryMessage,
  sessionNameFromMessage,
  setBlocks,
  stepActive,
  titleForFirstMessage,
  toPersisted,
  toggleBlocksExpanded,
  writeSessionsFile,
  type Session,
} from "./sessions"
import type { ChatMessageView } from "./client"

function message(id: string, content: string): ChatMessageView {
  return {
    id,
    role: "assistant",
    content,
    toolName: null,
    parts: null,
    meta: null,
    createdAt: "2026-01-01T00:00:00Z",
  }
}

function liveSession(id: string, name = "tab"): Session {
  return {
    ...newPendingSession(),
    id,
    name,
    hydrated: true,
  }
}

describe("sessionNameFromMessage", () => {
  it("uses the first 20 characters of the message", () => {
    expect(sessionNameFromMessage("identity-refactor everything")).toBe(
      "identity-refactor ev"
    )
  })

  it("keeps short messages as-is", () => {
    expect(sessionNameFromMessage("readme fix")).toBe("readme fix")
  })

  it("collapses whitespace before slicing", () => {
    expect(sessionNameFromMessage("fix   the\n\treadme now please ok")).toBe(
      "fix the readme now p"
    )
  })

  it("falls back for blank input", () => {
    expect(sessionNameFromMessage("   ")).toBe("session")
  })
})

describe("addSession", () => {
  it("appends and activates the new tab", () => {
    const first = addSession(
      { sessions: [], activeIndex: -1 },
      liveSession("s1")
    )
    expect(first.activeIndex).toBe(0)
    const second = addSession(first, liveSession("s2"))
    expect(second.sessions.map((session) => session.id)).toEqual(["s1", "s2"])
    expect(second.activeIndex).toBe(1)
  })
})

describe("removeSession", () => {
  it("focuses the right neighbour when a middle tab closes", () => {
    const state = {
      sessions: [liveSession("s1"), liveSession("s2"), liveSession("s3")],
      activeIndex: 1,
    }
    const next = removeSession(state, 1)
    expect(next.sessions.map((session) => session.id)).toEqual(["s1", "s3"])
    expect(next.activeIndex).toBe(1)
  })

  it("focuses the last tab when the rightmost closes", () => {
    const state = {
      sessions: [liveSession("s1"), liveSession("s2")],
      activeIndex: 1,
    }
    const next = removeSession(state, 1)
    expect(next.activeIndex).toBe(0)
  })

  it("empty list leaves activeIndex -1", () => {
    const next = removeSession(
      { sessions: [liveSession("s1")], activeIndex: 0 },
      0
    )
    expect(next.sessions).toEqual([])
    expect(next.activeIndex).toBe(-1)
  })

  it("ignores out-of-range indexes", () => {
    const state = { sessions: [liveSession("s1")], activeIndex: 0 }
    expect(removeSession(state, 5)).toBe(state)
  })
})

describe("stepActive", () => {
  it("wraps in both directions", () => {
    expect(stepActive(3, 2, 1)).toBe(0)
    expect(stepActive(3, 0, -1)).toBe(2)
    expect(stepActive(3, 1, 1)).toBe(2)
  })

  it("returns -1 with no sessions", () => {
    expect(stepActive(0, -1, 1)).toBe(-1)
  })
})

describe("patchSession / appendBlocks", () => {
  it("patches only the addressed session", () => {
    const sessions = [liveSession("s1"), liveSession("s2")]
    const next = patchSession(sessions, "s2", { status: "thinking" })
    expect(next[0]?.status).toBe("idle")
    expect(next[1]?.status).toBe("thinking")
  })

  it("appends message and line blocks with unique keys", () => {
    const sessions = [liveSession("s1")]
    const withMessage = appendBlocks(sessions, "s1", [
      { kind: "message", message: message("m1", "hello") },
    ])
    const withLines = appendBlocks(withMessage, "s1", [
      { kind: "lines", lines: ["one"] },
    ])
    const blocks = withLines[0]?.blocks ?? []
    expect(blocks).toHaveLength(2)
    expect(blocks[0]?.kind).toBe("message")
    expect(blocks[1]?.kind).toBe("lines")
    expect(new Set(blocks.map((block) => block.key)).size).toBe(2)
  })

  it("setBlocks replaces the transcript wholesale", () => {
    const sessions = appendBlocks([liveSession("s1")], "s1", [
      { kind: "lines", lines: ["old"] },
    ])
    const next = setBlocks(sessions, "s1", [])
    expect(next[0]?.blocks).toEqual([])
  })
})

describe("appendLiveText / markUnread", () => {
  it("caps the live tail at 4000 characters", () => {
    const big = "x".repeat(4500)
    const next = appendLiveText([liveSession("s1")], "s1", big)
    expect(next[0]?.liveText.length).toBe(4000)
  })

  it("marks unread only on background tabs", () => {
    const sessions = [liveSession("s1"), liveSession("s2")]
    expect(markUnread(sessions, "s1", "s1")[0]?.unread).toBe(false)
    expect(markUnread(sessions, "s2", "s1")[1]?.unread).toBe(true)
  })
})

describe("adoptServerId", () => {
  it("swaps a pending local id for the server uuid and names the tab", () => {
    const pending = addSession(
      { sessions: [], activeIndex: -1 },
      newPendingSession()
    )
    const localId = pending.sessions[0]?.id ?? ""
    expect(localId.startsWith(PENDING_PREFIX)).toBe(true)
    const next = adoptServerId(pending, localId, "uuid-1", "identity fix")
    expect(next.sessions[0]?.id).toBe("uuid-1")
    expect(next.sessions[0]?.name).toBe("identity fix")
    expect(next.activeIndex).toBe(0)
  })
})

describe("renameSession", () => {
  it("renames the tab and locks out auto-naming", () => {
    const next = renameSession(
      [liveSession("s1", "auto name")],
      "s1",
      "Manual Name"
    )
    expect(next[0]?.name).toBe("Manual Name")
    expect(next[0]?.renamed).toBe(true)
  })

  it("collapses whitespace in the title", () => {
    const next = renameSession([liveSession("s1", "auto")], "s1", "  a   b ")
    expect(next[0]?.name).toBe("a b")
  })

  it("ignores blank titles and unknown ids", () => {
    const sessions = [liveSession("s1", "auto")]
    expect(renameSession(sessions, "s1", "   ")).toBe(sessions)
    expect(renameSession(sessions, "nope", "x")).toBe(sessions)
  })
})

describe("filterSessions", () => {
  it("returns the list as-is for a blank query", () => {
    const sessions = [liveSession("s1", "alpha"), liveSession("s2", "beta")]
    expect(filterSessions(sessions, "")).toBe(sessions)
    expect(filterSessions(sessions, "   ")).toBe(sessions)
  })

  it("matches names case-insensitively as a substring", () => {
    const sessions = [
      liveSession("s1", "Fix Auth"),
      liveSession("s2", "docs"),
      liveSession("s3", "auth-review"),
    ]
    expect(filterSessions(sessions, "AUTH").map((session) => session.id)).toEqual(
      ["s1", "s3"]
    )
    expect(filterSessions(sessions, "doc").map((session) => session.name)).toEqual(
      ["docs"]
    )
  })

  it("yields an empty list when nothing matches", () => {
    expect(filterSessions([liveSession("s1", "alpha")], "zzz")).toEqual([])
  })
})

describe("retryMessage", () => {
  it("returns the last user message when there is history", () => {
    const session = { ...liveSession("s1"), lastUserMessage: "fix it" }
    expect(retryMessage(session)).toBe("fix it")
  })

  it("returns null without history", () => {
    expect(retryMessage(liveSession("s1"))).toBeNull()
  })

  it("returns null without a session", () => {
    expect(retryMessage(undefined)).toBeNull()
  })
})

describe("branchOpener", () => {
  it("reuses the last user message as the fork's opener", () => {
    const session = { ...liveSession("s1"), lastUserMessage: "fix it" }
    expect(branchOpener(session)).toBe("fix it")
  })

  it("returns null for a session without messages", () => {
    expect(branchOpener(liveSession("s1"))).toBeNull()
  })

  it("returns null without a session", () => {
    expect(branchOpener(undefined)).toBeNull()
  })
})

describe("forkTitle", () => {
  it("titles the fork after its source", () => {
    expect(forkTitle("readme fix")).toBe("fork of readme fix")
  })

  it("collapses whitespace in the source name", () => {
    expect(forkTitle("  fix   the  readme ")).toBe("fork of fix the readme")
  })

  it("falls back to a generic name for blank input", () => {
    expect(forkTitle("   ")).toBe("fork of session")
  })
})

describe("titleForFirstMessage", () => {
  it("keeps a manual rename over the auto-derived name", () => {
    const session = { ...liveSession("s1", "Manual"), renamed: true }
    expect(titleForFirstMessage(session, "a different message")).toBe("Manual")
  })

  it("auto-names from the message otherwise", () => {
    expect(titleForFirstMessage(liveSession("s1"), "fix the readme")).toBe(
      "fix the readme"
    )
    expect(titleForFirstMessage(undefined, "fix the readme")).toBe(
      "fix the readme"
    )
  })
})

describe("appendHistory", () => {
  it("appends submitted prompts oldest-first to the matching session", () => {
    const sessions = [liveSession("s1"), liveSession("s2")]
    const once = appendHistory(sessions, "s1", "first")
    const twice = appendHistory(once, "s1", "second")
    expect(twice[0]?.history).toEqual(["first", "second"])
    expect(twice[1]?.history).toEqual([])
  })

  it("keeps history when a pending tab adopts its server id", () => {
    const pending = addSession(
      { sessions: [], activeIndex: -1 },
      newPendingSession()
    )
    const localId = pending.sessions[0]?.id ?? ""
    const withHistory = appendHistory(pending.sessions, localId, "hello")
    const adopted = adoptServerId(
      { sessions: withHistory, activeIndex: 0 },
      localId,
      "uuid-1",
      "hello"
    )
    expect(adopted.sessions[0]?.history).toEqual(["hello"])
  })
})

describe("persistence round-trip", () => {
  it("rejects malformed roots and missing session arrays", () => {
    expect(decodePersistedSessions(null).ok).toBe(false)
    expect(decodePersistedSessions({}).ok).toBe(false)
  })

  it("normalizes partial entries and ignores extra fields", () => {
    expect(
      decodePersistedSessions({
        activeSessionId: 42,
        extra: true,
        sessions: [
          {
            id: "s1",
            name: 10,
            status: "foreign",
            createdAt: "yesterday",
            history: ["valid", 10],
            extra: "ignored",
          },
          { name: "missing id" },
        ],
      })
    ).toEqual({
      ok: true,
      value: {
        sessions: [
          {
            id: "s1",
            name: "session",
            status: "idle",
            createdAt: 0,
            history: ["valid"],
          },
        ],
      },
    })
  })

  it("persists only server tabs and restores the active one", () => {
    const state = {
      sessions: [
        liveSession("s1", "identity"),
        { ...newPendingSession(), id: "local-x" },
        liveSession("s2", "readme"),
      ],
      activeIndex: 2,
    }
    const persisted = toPersisted(state)
    expect(persisted.sessions.map((session) => session.id)).toEqual([
      "s1",
      "s2",
    ])
    expect(persisted.activeSessionId).toBe("s2")

    const restored = fromPersisted(persisted)
    expect(restored.sessions).toHaveLength(2)
    expect(restored.activeIndex).toBe(1)
    expect(restored.sessions[1]?.hydrated).toBe(false)
    expect(restored.sessions[1]?.blocks).toEqual([])
  })

  it("round-trips recall history through sessions.json", () => {
    const session = appendHistory([liveSession("s1", "identity")], "s1", "q1")
    const withHistory = appendHistory(session, "s1", "q2")
    const persisted = toPersisted({
      sessions: withHistory,
      activeIndex: 0,
    })
    expect(persisted.sessions[0]?.history).toEqual(["q1", "q2"])

    const restored = fromPersisted(persisted)
    expect(restored.sessions[0]?.history).toEqual(["q1", "q2"])
  })

  it("omits empty history from the persisted shape and restores []", () => {
    const persisted = toPersisted({
      sessions: [liveSession("s1")],
      activeIndex: 0,
    })
    expect(persisted.sessions[0]?.history).toBeUndefined()

    const restored = fromPersisted({
      sessions: [{ id: "s1", name: "old", status: "idle", createdAt: 0 }],
    })
    expect(restored.sessions[0]?.history).toEqual([])
  })

  it("restores an empty list to no tabs", () => {
    expect(fromPersisted({ sessions: [] })).toEqual({
      sessions: [],
      activeIndex: -1,
    })
  })

  it("drops entries without an id", () => {
    const restored = fromPersisted({
      sessions: [
        { id: "", name: "broken", status: "idle", createdAt: 0 },
        { id: "s1", name: "ok", status: "done", createdAt: 1 },
      ],
    })
    expect(restored.sessions.map((session) => session.id)).toEqual(["s1"])
  })

  it("round-trips activeSessionId through the sessions file", async () => {
    const path = `${import.meta.dir}/sessions-active-roundtrip.tmp.json`
    await writeSessionsFile(
      {
        sessions: [liveSession("s1", "one"), liveSession("s2", "two")],
        activeIndex: 1,
      },
      path
    )
    const persisted = await readSessionsFile(path)
    expect(persisted.activeSessionId).toBe("s2")
    expect(fromPersisted(persisted).activeIndex).toBe(1)
    await rm(path, { force: true })
  })


  it("round-trips a manual rename through the sessions file", async () => {
    const path = `${import.meta.dir}/sessions-rename-roundtrip.tmp.json`
    const state = {
      sessions: [{ ...liveSession("s1", "Manual Name"), renamed: true }],
      activeIndex: 0,
    }
    await writeSessionsFile(state, path)
    const restored = fromPersisted(await readSessionsFile(path))
    expect(restored.sessions[0]?.name).toBe("Manual Name")
    expect(restored.sessions[0]?.renamed).toBe(true)
    expect(restored.sessions[0]?.lastUserMessage).toBeNull()
    await rm(path, { force: true })
  })

  it("restores renamed false for auto-named and legacy entries", () => {
    const restored = fromPersisted({
      sessions: [
        { id: "s1", name: "auto", status: "idle", createdAt: 0 },
        { id: "s2", name: "manual", status: "idle", createdAt: 1, renamed: true },
      ],
    })
    expect(restored.sessions[0]?.renamed).toBe(false)
    expect(restored.sessions[1]?.renamed).toBe(true)
  })
})

describe("toggleBlocksExpanded", () => {
  it("defaults to collapsed for new and restored tabs", () => {
    expect(newPendingSession().blocksExpanded).toBe(false)
    const restored = fromPersisted({
      sessions: [{ id: "s1", name: "tab", status: "idle", createdAt: 0 }],
    })
    expect(restored.sessions[0]?.blocksExpanded).toBe(false)
  })

  it("flips the target tab and leaves other tabs untouched", () => {
    const sessions = [liveSession("s1"), liveSession("s2")]
    const once = toggleBlocksExpanded(sessions, "s1")
    expect(once[0]?.blocksExpanded).toBe(true)
    expect(once[1]?.blocksExpanded).toBe(false)

    const twice = toggleBlocksExpanded(once, "s1")
    expect(twice[0]?.blocksExpanded).toBe(false)
    expect(twice[1]?.blocksExpanded).toBe(false)
  })

  it("is not persisted — restore always starts collapsed", () => {
    const expanded = toggleBlocksExpanded([liveSession("s1")], "s1")
    const persisted = toPersisted({ sessions: expanded, activeIndex: 0 })
    expect(persisted.sessions[0]).not.toHaveProperty("blocksExpanded")
    expect(
      fromPersisted(persisted).sessions[0]?.blocksExpanded
    ).toBe(false)
  })
})

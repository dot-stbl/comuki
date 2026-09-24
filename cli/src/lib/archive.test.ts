import { afterEach, describe, expect, it } from "bun:test"
import { join } from "node:path"
import { mkdtemp, rm, stat } from "node:fs/promises"
import { tmpdir } from "node:os"
import {
  archiveFileName,
  archiveFilePath,
  EmptyArchiveError,
  fetchArchiveMessages,
  formatArchiveList,
  listArchiveFiles,
  saveArchiveFile,
  type ArchiveMessagesClient,
} from "./archive"

describe("archiveFileName", () => {
  const at = new Date(2026, 8, 18, 14, 5)

  it("composes {id}-{slug}-{yyyymmdd}.md", () => {
    expect(archiveFileName("018f-abcd", "fix the login", at)).toBe(
      "018f-abcd-fix-the-login-20260918.md"
    )
  })

  it("sanitises the id and falls back when the slug is empty", () => {
    expect(archiveFileName("a/b:c", "///", at)).toBe("a-b-c-session-20260918.md")
  })

  it("keeps a pending local- prefix in the filename", () => {
    expect(archiveFileName("local-123-0", "new", at)).toBe(
      "local-123-0-new-20260918.md"
    )
  })
})

describe("archiveFilePath", () => {
  it("joins the archive dir with the generated name", () => {
    const at = new Date(2026, 8, 18)
    expect(archiveFilePath("s1", "tab", at, "/tmp/archive")).toBe(
      join("/tmp/archive", "s1-tab-20260918.md")
    )
  })
})

describe("formatArchiveList", () => {
  it("prints a hint when the directory is empty", () => {
    expect(formatArchiveList([])).toBe("no archives yet\n")
  })

  it("aligns name, size and mtime columns", () => {
    const mtimeMs = new Date(2026, 8, 18, 14, 5).getTime()
    const listing = formatArchiveList([
      { name: "s1-fix-20260918.md", size: 2400, mtimeMs },
      { name: "short.md", size: 12, mtimeMs },
    ])
    expect(listing).toContain("s1-fix-20260918.md")
    expect(listing).toContain("short.md")
    expect(listing).toContain("2.3 KB")
    expect(listing).toContain("12 B")
    expect(listing).toContain("2026-09-18 14:05")
  })
})

const message = (
  id: string,
  role: "user" | "assistant",
  content: string
) => ({
  id,
  role,
  content,
  toolName: null,
  parts: null,
  meta: null,
  createdAt: "2026-09-18T00:00:00Z",
})

describe("fetchArchiveMessages", () => {
  it("walks every page and concatenates items in order", async () => {
    const calls: { page: number; pageSize: number }[] = []
    const client: ArchiveMessagesClient = {
      async listMessages(_sessionId, page = 1, pageSize = 50) {
        calls.push({ page, pageSize })
        if (page === 1) {
          return {
            items: [
              message("m1", "user", "hi"),
              message("m2", "assistant", "hello"),
            ],
            total: "60",
          }
        }
        if (page === 2) {
          return {
            items: [message("m3", "user", "another")],
            total: "60",
          }
        }
        return { items: [], total: "60" }
      },
    }
    const result = await fetchArchiveMessages(client, "s1")
    expect(result.map((item) => item.id)).toEqual(["m1", "m2", "m3"])
    expect(calls.map((call) => call.page).sort()).toEqual([1, 2])
  })

  it("returns the only page when total fits on one page", async () => {
    const client: ArchiveMessagesClient = {
      async listMessages() {
        return {
          items: [message("m1", "user", "hi")],
          total: "1",
        }
      },
    }
    const result = await fetchArchiveMessages(client, "s1")
    expect(result.map((item) => item.id)).toEqual(["m1"])
  })
})

describe("saveArchiveFile", () => {
  const dirs: string[] = []

  afterEach(async () => {
    await Promise.all(dirs.splice(0).map((dir) => rm(dir, { recursive: true, force: true })))
  })

  async function tmpDir(): Promise<string> {
    const dir = await mkdtemp(join(tmpdir(), "comuki-archive-save-"))
    dirs.push(dir)
    return dir
  }

  const at = new Date(2026, 8, 18, 14, 5)

  it("writes the file under the injected dir with archiveFileName's name", async () => {
    const dir = await tmpDir()
    const client: ArchiveMessagesClient = {
      async listMessages() {
        return {
          items: [message("m1", "user", "hi")],
          total: "1",
        }
      },
    }

    const path = await saveArchiveFile({
      client,
      sessionId: "s1",
      sessionName: "tab",
      now: at,
      dir,
    })

    expect(path).toBe(archiveFilePath("s1", "tab", at, dir))
    const info = await stat(path)
    expect(info.isFile()).toBe(true)
  })

  it("writes the transcript as `exportMarkdown` would render it", async () => {
    const dir = await tmpDir()
    const client: ArchiveMessagesClient = {
      async listMessages() {
        return {
          items: [
            message("m1", "user", "hi"),
            message("m2", "assistant", "hello"),
          ],
          total: "2",
        }
      },
    }

    const path = await saveArchiveFile({
      client,
      sessionId: "s1",
      sessionName: "tab",
      now: at,
      dir,
    })

    const content = await Bun.file(path).text()
    expect(content).toContain("## you")
    expect(content).toContain("hi")
    expect(content).toContain("## comuki")
    expect(content).toContain("hello")
  })

  it("captures content from every paged call, not just page 1", async () => {
    const dir = await tmpDir()
    const client: ArchiveMessagesClient = {
      async listMessages(_sessionId, page = 1) {
        if (page === 1) {
          return {
            items: [
              message("p1a", "user", "question one"),
              message("p1b", "assistant", "answer one"),
            ],
            total: "60",
          }
        }
        if (page === 2) {
          return {
            items: [
              message("p2a", "user", "question two"),
              message("p2b", "assistant", "answer two"),
            ],
            total: "60",
          }
        }
        return { items: [], total: "60" }
      },
    }

    const path = await saveArchiveFile({
      client,
      sessionId: "s-multi",
      sessionName: "multi",
      now: at,
      dir,
    })

    const content = await Bun.file(path).text()
    expect(content).toContain("question one")
    expect(content).toContain("answer one")
    expect(content).toContain("question two")
    expect(content).toContain("answer two")
  })

  it("rejects with EmptyArchiveError when the transcript is empty", async () => {
    const dir = await tmpDir()
    const client: ArchiveMessagesClient = {
      async listMessages() {
        return { items: [], total: "0" }
      },
    }

    await expect(
      saveArchiveFile({
        client,
        sessionId: "s-empty",
        sessionName: "empty",
        now: at,
        dir,
      })
    ).rejects.toBeInstanceOf(EmptyArchiveError)

    const listings = await listArchiveFiles(dir)
    expect(listings).toEqual([])
  })

  it("is discoverable by the live listArchiveFiles after a successful save", async () => {
    const dir = await tmpDir()
    const client: ArchiveMessagesClient = {
      async listMessages() {
        return {
          items: [message("m1", "user", "hi")],
          total: "1",
        }
      },
    }

    const path = await saveArchiveFile({
      client,
      sessionId: "s-discoverable",
      sessionName: "discoverable",
      now: at,
      dir,
    })

    const listings = await listArchiveFiles(dir)
    const names = listings.map((listing) => listing.name)
    const basename = path.split(/[\\/]/).pop() ?? ""
    expect(names).toContain(basename)
  })
})

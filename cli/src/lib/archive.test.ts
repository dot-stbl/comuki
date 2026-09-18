import { describe, expect, it } from "bun:test"
import { join } from "node:path"
import {
  archiveFileName,
  archiveFilePath,
  formatArchiveList,
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

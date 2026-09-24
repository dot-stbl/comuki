import { describe, expect, it } from "bun:test"
import { formatArchiveList } from "./archive"

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

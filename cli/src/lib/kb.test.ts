import { afterAll, beforeAll, describe, expect, it } from "bun:test"
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"
import {
  KB_MAX_FILE_BYTES,
  collectFiles,
  formatBytes,
  ingestRequestFor,
  kbAddErrorLine,
  kbAddResultLine,
  kbListLines,
  kbListRow,
  kbUsageLines,
  kbWriteUnavailableLines,
  mimeTypeFor,
  validateIngestFile,
} from "./kb"
import type { KnowledgeDocumentsPageView } from "./client"
import { stripAnsi } from "../theme"

let fixtureDir: string

beforeAll(() => {
  fixtureDir = mkdtempSync(join(tmpdir(), "comuki-kb-"))
  writeFileSync(join(fixtureDir, "notes.md"), "# notes")
  writeFileSync(join(fixtureDir, "readme.md"), "read me")
  mkdirSync(join(fixtureDir, "docs"))
  writeFileSync(join(fixtureDir, "docs", "deep.txt"), "deep")
})

afterAll(() => {
  rmSync(fixtureDir, { recursive: true, force: true })
})

describe("collectFiles", () => {
  it("expands a glob against the cwd and returns sorted matches", () => {
    expect(collectFiles(["*.md"], fixtureDir)).toEqual([
      "notes.md",
      "readme.md",
    ])
  })

  it("a recursive glob reaches subdirectories", () => {
    expect(collectFiles(["**/*.txt"], fixtureDir)).toEqual([
      "docs/deep.txt",
    ])
  })

  it("a literal path passes through when the file exists", () => {
    expect(collectFiles(["notes.md"], fixtureDir)).toEqual(["notes.md"])
  })

  it("an absent literal contributes nothing — no throw", () => {
    expect(collectFiles(["missing.md"], fixtureDir)).toEqual([])
  })

  it("unions multiple patterns, de-duplicated and sorted", () => {
    expect(collectFiles(["notes.md", "*.md", "docs/*.txt"], fixtureDir)).toEqual(
      ["docs/deep.txt", "notes.md", "readme.md"]
    )
  })

  it("empty patterns are skipped", () => {
    expect(collectFiles(["", "  "], fixtureDir)).toEqual([])
  })
})

describe("validateIngestFile", () => {
  it("accepts a non-empty text file under the cap", () => {
    expect(validateIngestFile("notes.md", 120)).toEqual({ ok: true })
    expect(validateIngestFile("app.ts", 4096)).toEqual({ ok: true })
  })

  it("rejects binary or unknown extensions with an honest reason", () => {
    const check = validateIngestFile("logo.png", 100)
    expect(check.ok).toBe(false)
    expect(!check.ok && check.reason).toContain("unsupported file type")
  })

  it("rejects extension-less paths", () => {
    const check = validateIngestFile("Dockerfile", 100)
    expect(check.ok).toBe(false)
  })

  it("rejects empty files", () => {
    const check = validateIngestFile("empty.md", 0)
    expect(check.ok).toBe(false)
    expect(!check.ok && check.reason).toContain("empty")
  })

  it("rejects files over the limit, naming both sizes", () => {
    const check = validateIngestFile("big.md", KB_MAX_FILE_BYTES + 1)
    expect(check.ok).toBe(false)
    expect(!check.ok && check.reason).toContain("2.0 MB")
  })

  it("honours a custom max", () => {
    expect(validateIngestFile("a.md", 500, 499).ok).toBe(false)
    expect(validateIngestFile("a.md", 499, 499).ok).toBe(true)
  })
})

describe("mimeTypeFor", () => {
  it("maps the common text kinds", () => {
    expect(mimeTypeFor("notes.md")).toBe("text/markdown")
    expect(mimeTypeFor("dump.txt")).toBe("text/plain")
    expect(mimeTypeFor("data.json")).toBe("application/json")
    expect(mimeTypeFor("app.ts")).toBe("text/x-typescript")
    expect(mimeTypeFor("run.sh")).toBe("text/x-shellscript")
  })

  it("unknown extensions fall back to text/plain, case-insensitive lookup", () => {
    expect(mimeTypeFor("weird.ext")).toBe("text/plain")
    expect(mimeTypeFor("NOTES.MD")).toBe("text/markdown")
  })
})

describe("formatBytes", () => {
  it("renders B, KB and MB", () => {
    expect(formatBytes(512)).toBe("512 B")
    expect(formatBytes(2048)).toBe("2.0 KB")
    expect(formatBytes(KB_MAX_FILE_BYTES)).toBe("2.0 MB")
  })
})

describe("ingestRequestFor", () => {
  it("builds an upload request with title, ref, mime and text", () => {
    expect(
      ingestRequestFor("docs/notes.md", "# hi", undefined)
    ).toEqual({
      title: "notes.md",
      source: "upload",
      sourceRef: "upload:docs/notes.md",
      mimeType: "text/markdown",
      text: "# hi",
    })
  })

  it("rides the project context when present — scoped keys cannot write global", () => {
    const request = ingestRequestFor("a.md", "x", "proj-1")
    expect(request.projectId).toBe("proj-1")
  })

  it("omits projectId entirely when absent", () => {
    const request = ingestRequestFor("a.md", "x", undefined)
    expect("projectId" in request).toBe(false)
  })
})

describe("kbUsageLines", () => {
  it("names both subcommands", () => {
    const usage = kbUsageLines().map(stripAnsi).join("\n")
    expect(usage).toContain("/kb add <file|glob>")
    expect(usage).toContain("/kb list")
  })
})

const now = new Date("2026-09-18T12:00:00Z")

function pageOf(
  items: KnowledgeDocumentsPageView["items"],
  total: number
): KnowledgeDocumentsPageView {
  return { items, page: 1, pageSize: 25, total }
}

describe("kbListLines", () => {
  const documents = [
    {
      id: "d1",
      projectId: null,
      title: "Comuki README",
      source: "upload",
      sourceRef: "upload:README.md",
      mimeType: "text/markdown",
      chunkCount: 14,
      tokenCount: 4200,
      createdAt: "2026-09-18T11:00:00Z",
    },
    {
      id: "d2",
      projectId: "p1",
      title: "Onboarding guide",
      source: "git",
      sourceRef: "https://git.internal/docs@main",
      mimeType: "text/markdown",
      chunkCount: 3,
      tokenCount: 900,
      createdAt: "2026-09-15T00:00:00Z",
    },
  ]

  it("renders a header, column line and one row per document", () => {
    const lines = kbListLines(pageOf(documents, 2), now).map(stripAnsi)
    expect(lines[0]).toContain("kb")
    expect(lines[0]).toContain("2 of 2")
    expect(lines[1]).toContain("AGE")
    expect(lines[2]).toContain("Comuki README")
    expect(lines[2]).toContain("1h")
    expect(lines[2]).toContain("14 chn")
    expect(lines[2]).toContain("upload · global")
    expect(lines[3]).toContain("3d")
    expect(lines[3]).toContain("git")
  })

  it("marks project-scoped rows without the global tag", () => {
    const row = stripAnsi(
      kbListRow(
        documents[1] as KnowledgeDocumentsPageView["items"][number],
        now
      )
    )
    expect(row).toContain("Onboarding guide")
    expect(row).not.toContain("global")
  })

  it("shows a +N more tail when the page is not the whole library", () => {
    const lines = kbListLines(pageOf(documents, 30), now).map(stripAnsi)
    expect(lines.at(-1)).toContain("+28 more")
  })

  it("an empty library says so and points at /kb add", () => {
    const lines = kbListLines(pageOf([], 0), now).map(stripAnsi)
    expect(lines).toHaveLength(2)
    expect(lines[1]).toContain("library is empty")
    expect(lines[1]).toContain("/kb add")
  })
})

describe("kbAdd lines", () => {
  it("the result line is `⏺ name → id · N chunks`", () => {
    const line = stripAnsi(
      kbAddResultLine("notes.md", {
        sourceDocumentId: "018f-abcd",
        chunksWritten: 7,
      })
    )
    expect(line).toContain("notes.md")
    expect(line).toContain("018f-abcd")
    expect(line).toContain("7 chunks")
  })

  it("the error line is `✗ name → reason`", () => {
    const line = stripAnsi(kbAddErrorLine("big.md", "too large"))
    expect(line).toContain("big.md")
    expect(line).toContain("too large")
  })
})

describe("kbWriteUnavailableLines", () => {
  it("403 names the missing knowledge:write permission and the roles that carry it", () => {
    const lines = kbWriteUnavailableLines(403).map(stripAnsi)
    expect(lines[0]).toContain(
      "knowledge write api not available for this subject"
    )
    expect(lines[0]).toContain("knowledge:write")
    expect(lines[1]).toContain("operator")
  })

  it("401 names the auth problem and the fix", () => {
    const lines = kbWriteUnavailableLines(401).map(stripAnsi)
    expect(lines[0]).toContain("not authenticated")
    expect(lines[1]).toContain("COMUKI_API_KEY")
  })
})

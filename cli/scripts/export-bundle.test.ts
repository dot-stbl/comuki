/**
 * Export bundle round-trip (issue #81).
 *
 * Two layers:
 *   1. The archive encoders (tar + zip) round-trip through stock OS
 *      tools: `tar -tvf` / `Expand-Archive`.
 *   2. The script glue: parent directories are created
 *      (mkdir -p semantics), composer draft bodies are NEVER in the
 *      archive, and the platform info block is present.
 */

import { afterEach, beforeEach, describe, expect, test } from "bun:test"
import { mkdir, rm, writeFile, readFile } from "node:fs/promises"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { encodeTarGz, encodeZip, exportBundle, readZip } from "./export-bundle"

async function mkTempDir(label: string): Promise<string> {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`
  const dir = join(tmpdir(), `${label}-${stamp}`)
  await rm(dir, { recursive: true, force: true })
  return dir
}

describe("encodeTarGz", () => {
  test("round-trips through `tar -tzf`", async () => {
    const entries = [
      { name: "manifest.json", data: Buffer.from('{"v":1}', "utf8") },
      { name: "diagnostics.log", data: Buffer.from("hello\n", "utf8") },
    ]
    const tar = encodeTarGz(entries)
    const tarPath = join(tmpdir(), `comuki-export-bundle-${Date.now()}-${Math.floor(Math.random() * 1e6)}.tar.gz`)
    await Bun.write(tarPath, tar)
    try {
      const proc = Bun.spawn({
        // `--force-local` keeps GNU tar from treating a Windows drive
        // letter (`C:\...`) as a `host:path` remote-shell target.
        cmd: ["tar", "--force-local", "-tzf", tarPath],
        stdout: "pipe",
      })
      const out = await new Response(proc.stdout).text()
      await proc.exited
      expect(out).toContain("manifest.json")
      expect(out).toContain("diagnostics.log")
    } finally {
      await rm(tarPath, { force: true })
    }
  })

  test("round-trips file bodies through `tar -xzOf`", async () => {
    const entries = [
      { name: "manifest.json", data: Buffer.from('{"v":1}', "utf8") },
      { name: "data.txt", data: Buffer.from("payload", "utf8") },
    ]
    const tar = encodeTarGz(entries)
    const tarPath = join(tmpdir(), `comuki-export-bundle-${Date.now()}-${Math.floor(Math.random() * 1e6)}.tar.gz`)
    await Bun.write(tarPath, tar)
    try {
      const proc = Bun.spawn({
        cmd: ["tar", "--force-local", "-xzOf", tarPath, "data.txt"],
        stdout: "pipe",
      })
      const out = await new Response(proc.stdout).text()
      await proc.exited
      expect(out.trim()).toBe("payload")
    } finally {
      await rm(tarPath, { force: true })
    }
  })
})

describe("encodeZip", () => {
  test("round-trips through readZip (in-process)", () => {
    const entries = [
      { name: "manifest.json", data: Buffer.from('{"v":1}', "utf8") },
      { name: "diagnostics.log", data: Buffer.from("hello\n", "utf8") },
    ]
    const zip = encodeZip(entries)
    const decoded = readZip(zip)
    expect(decoded).toHaveLength(2)
    expect(decoded[0]!.name).toBe("manifest.json")
    expect(decoded[0]!.data.toString("utf8")).toBe('{"v":1}')
    expect(decoded[1]!.name).toBe("diagnostics.log")
    expect(decoded[1]!.data.toString("utf8")).toBe("hello\n")
  })

  test("rejects malformed archives", () => {
    const bad = Buffer.from("not a zip archive")
    expect(() => readZip(bad)).toThrow(/EOCD/)
  })
})

describe("exportBundle — script behaviour", () => {
  let tempDir: string
  let bundleDir: string

  beforeEach(async () => {
    tempDir = await mkTempDir("comuki-bundle")
    bundleDir = join(tempDir, "state")
    await mkdir(bundleDir, { recursive: true })
    // Seed the source files so the archive has real content.
    await writeFile(
      join(bundleDir, "diagnostics.log"),
      '{"ts":1,"level":"info","kind":"test"}\n',
      "utf8"
    )
    await writeFile(
      join(bundleDir, "sessions.ndjson"),
      '{"id":"s-1","name":"seed","createdAt":1,"lastKnownCursor":0,"renamed":false,"archived":false}\n',
      "utf8"
    )
  })

  afterEach(async () => {
    await rm(tempDir, { recursive: true, force: true })
  })

  test("creates parent directories (mkdir -p semantics)", async () => {
    const nested = join(tempDir, "deep", "nested", "path", "bundle.tar.gz")
    const report = await exportBundle({
      stateDirectory: bundleDir,
      destination: nested,
      format: "tar.gz",
    })
    expect(report.destination).toBe(nested)
    expect(report.bytes).toBeGreaterThan(0)
    const fileExists = await readFile(nested, "utf8")
    expect(fileExists.length).toBeGreaterThan(0)
  })

  test("tar.gz bundle contains the manifest + sessions + diagnostics", async () => {
    const destination = join(tempDir, "bundle.tar.gz")
    const report = await exportBundle({
      stateDirectory: bundleDir,
      destination,
      format: "tar.gz",
    })
    expect(report.format).toBe("tar.gz")
    expect(report.entries).toBeGreaterThanOrEqual(4)

    // Use tar -tzf to list — fails fast if the encoding is broken.
    // `--force-local` keeps GNU tar from treating a Windows drive letter
    // (`C:\...`) as a `host:path` remote-shell target.
    const proc = Bun.spawn({
      cmd: ["tar", "--force-local", "-tzf", destination],
      stdout: "pipe",
    })
    const list = await new Response(proc.stdout).text()
    await proc.exited
    expect(list).toContain("manifest.json")
    expect(list).toContain("sessions.ndjson")
    expect(list).toContain("diagnostics.log")
    expect(list).toContain("drafts-index.json")
  })

  test("zip bundle contains the manifest + sessions + diagnostics", async () => {
    const destination = join(tempDir, "bundle.zip")
    const report = await exportBundle({
      stateDirectory: bundleDir,
      destination,
      format: "zip",
    })
    expect(report.format).toBe("zip")

    const buf = await Bun.file(destination).bytes()
    const entries = readZip(Buffer.from(buf))
    const manifest = entries.find((e) => e.name === "manifest.json")
    expect(manifest).toBeDefined()
    const out = manifest!.data.toString("utf8")
    expect(out).toContain("bundleVersion")
    expect(out).toContain("clientVersion")
  })

  test("composer draft bodies are never in the bundle", async () => {
    // Seed a composer draft body — it must NOT land in the archive.
    const draftsDir = join(bundleDir, "drafts")
    await mkdir(draftsDir, { recursive: true })
    await writeFile(
      join(draftsDir, "s-1.txt"),
      "secret private draft body — PII — must never ship in a bug report bundle\n",
      "utf8"
    )
    const destination = join(tempDir, "bundle.tar.gz")
    await exportBundle({
      stateDirectory: bundleDir,
      destination,
      format: "tar.gz",
    })

    const extractDir = join(tempDir, "extracted")
    await mkdir(extractDir, { recursive: true })
    // Extract via `cwd` rather than `-C <dir>` — on Windows, passing an
    // absolute `C:\...` path as tar's `-C` argument gets mangled by the
    // MSYS/Git-Bash tar build even with `--force-local` (which only
    // covers the archive path itself).
    const proc = Bun.spawn({
      cmd: ["tar", "--force-local", "-xzf", destination],
      cwd: extractDir,
      stdout: "pipe",
    })
    await new Response(proc.stdout).text()
    await proc.exited

    // Walk the extract — no entry should contain the secret string.
    const files = ["manifest.json", "diagnostics.log", "sessions.ndjson", "drafts-index.json", "receipts-index.json"]
    for (const file of files) {
      const text = await readFile(join(extractDir, file), "utf8")
      expect(text).not.toContain("secret private draft body")
    }
  })
})

// rule-audit.ts
// Reads the user-global + project rule corpus, prints a scope summary,
// and audits staged .ts/.tsx changes against the FE rules in
// ~/.agents/rules/typescript/ + .agents/rules/coding/.
//
// Mirrors the backend VerifyRuleAwareness + SelfAuditReport targets
// (platform/build/Comuki.Build.Tools.targets) so the same workflow
// discipline applies to FE work. Designed to be called from the
// `predev` and `prebuild` package.json hooks in dashboard/.
//
// Since `dashboard-i18n` it also carries the hardcoded-copy ban (design
// D10, deny-by-default per task 7.3): user-facing copy as a literal in
// JSX — text nodes or label-bearing attributes — fails the audit unless
// it is on a reviewed fixture allowlist (wire-shape placeholders, wire
// identifiers inside <code>, test fixtures).
//
// Output:
//   stdout           -- banner + scope summary + violations (terminal-readable)
//   audit-data/last-commit-audit-fe.md -- machine-checkable artefact
//
// Invocation:
//   bun run scripts/rule-audit.ts
//
// Environment variables (optional, override defaults):
//   COMUKI_AUDIT_OUTPUT  -- output report path (default audit-data/last-commit-audit-fe.md)
//   COMUKI_AUDIT_TARGET  -- what to audit: 'staged' (default) or 'head' (last commit)

import {
  readdirSync,
  statSync,
  mkdirSync,
  writeFileSync,
  existsSync,
  readFileSync,
} from "node:fs"
import { join, basename, dirname, resolve, relative } from "node:path"
import { spawnSync } from "node:child_process"

// On Windows, `new URL(import.meta.url).pathname` returns `/C:/...`, and
// `resolve()` on that keeps the leading slash which then breaks `join()`
// (it produces `/C:/.../foo` again, then re-anchors at `C:`). Strip the
// leading slash for Windows paths.
function scriptDir(): string {
  const raw = decodeURI(new URL(import.meta.url).pathname)
  const cleaned =
    process.platform === "win32" && raw.startsWith("/") ? raw.slice(1) : raw
  return dirname(cleaned)
}

const REPO_ROOT = resolve(scriptDir(), "..", "..")
const USER_HOME = process.env.USERPROFILE ?? process.env.HOME ?? ""
const TYPESCRIPT_RULES_DIR = join(USER_HOME, ".agents", "rules", "typescript")
const PROJECT_TS_RULES_DIR = join(REPO_ROOT, ".agents", "rules", "coding")
const OUTPUT_PATH =
  process.env.COMUKI_AUDIT_OUTPUT ??
  join(REPO_ROOT, "audit-data", "last-commit-audit-fe.md")
const AUDIT_TARGET = (process.env.COMUKI_AUDIT_TARGET ?? "staged") as
  "staged" | "head"

// Patterns from FE-equivalent rules. C# has dedicated PowerShell checks
// (self-audit.ps1); the FE side mirrors the same five bans because they
// are language-spanning habits (discard, hand-rolled async wrappers, etc.)
// plus a small TS-specific set drawn from the typescript/ rules corpus.
const PATTERNS: { id: string; label: string; regex: RegExp }[] = [
  {
    id: "tsx-default-export",
    label: "React default export",
    regex: /\bexport\s+default\s+/,
  },
  { id: "react-fc", label: "React.FC", regex: /:\s*React\.FC\b/ },
  { id: "any-cast", label: "any cast", regex: /\bas\s+any\b/ },
  { id: "fetch-direct", label: "fetch() in component", regex: /\bfetch\s*\(/ },
]

/* ------------------------------------------------------------------ */
/* Hardcoded-copy ban (`dashboard-i18n` D10, deny-by-default per 7.3). */

interface LiteralCopyFinding {
  file: string
  line: number
  kind: "jsx-text" | "label-attr"
  text: string
}

/**
 * Reviewed fixtures — copy-shaped literals that are data, not copy
 * (design D11): wire-shape placeholder examples, a wire identifier in a
 * `<code>` slot, and the session test fixture's stand-in names. Each entry
 * is `file :: text`; line numbers would drift.
 */
const LITERAL_COPY_ALLOWLIST: { file: string; text: string; reason: string }[] =
  [
    {
      file: "src/domains/chat/pages/init-wizard-page.tsx",
      text: "git@github.com:acme/checkout-web.git",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/chat/pages/init-wizard-page.tsx",
      text: "env:ACME_MODEL_KEY",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/identity/ui/invite-user-form.tsx",
      text: "name@example.com",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/sources/ui/native-ticket-form.tsx",
      text: "checkout-web, bug",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/sources/ui/connection-fields.tsx",
      text: "https://git.example.internal",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/sources/ui/connection-form.tsx",
      text: "COMUKI_GITHUB_TOKEN",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/sources/ui/connect-source-form.tsx",
      text: "COMUKI_GITHUB_TOKEN",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/sources/pages/source-detail-page.tsx",
      text: "secretEnvRef",
      reason: "wire identifier inside a <code> slot",
    },
    {
      file: "src/shared/session/test-session.tsx",
      text: "Test User",
      reason: "test fixture stand-in name",
    },
    {
      file: "src/shared/session/test-session.tsx",
      text: "Test project",
      reason: "test fixture stand-in name",
    },
    {
      file: "src/shared/session/test-session.tsx",
      text: "Other project",
      reason: "test fixture stand-in name",
    },
    {
      file: "src/domains/chat/pages/init-wizard-page.tsx",
      text: "https://api.example.com/v1",
      reason: "wire-shape placeholder example",
    },
    {
      file: "src/domains/identity/ui/link-oidc-form.tsx",
      text: "oidc|provider|00000000",
      reason: "wire-shape placeholder example",
    },
  ]

/** Text directly before a *closing* tag — the classic JSX text node. */
const JSX_TEXT_NODE = />(\s*[A-Za-z][A-Za-z0-9 ,.…'’&()-]*[A-Za-z.…!?)]\s*)<\//

/** Copy-bearing attributes receiving a string literal (not `t(...)`). */
const LABEL_ATTR =
  /\b(?:label|title|description|placeholder|summary|emptyLabel|aria-label|content)\s*=\s*"([^"{}]*[A-Za-z]{2}[^"{}]*)"/

/** True for lines that are prose about the code, not the code itself. */
function isCommentLine(trimmed: string): boolean {
  return (
    trimmed.startsWith("//") ||
    trimmed.startsWith("*") ||
    trimmed.startsWith("/*")
  )
}

function scanLiteralCopy(): LiteralCopyFinding[] {
  const srcRoot = join(REPO_ROOT, "dashboard", "src")
  const files: string[] = []
  collectTsx(srcRoot, files)

  const findings: LiteralCopyFinding[] = []
  for (const abs of files) {
    // `src/...` relative to dashboard/ — the shape the allowlist speaks.
    const rel = relative(join(REPO_ROOT, "dashboard"), abs).replaceAll(
      "\\",
      "/"
    )
    const lines = readFileSync(abs, "utf8").split(/\r?\n/)
    let inBlockComment = false
    lines.forEach((line, index) => {
      const trimmed = line.trim()
      if (inBlockComment) {
        if (trimmed.includes("*/")) {
          inBlockComment = false
        }
        return
      }
      if (trimmed.startsWith("/*") && !trimmed.includes("*/")) {
        inBlockComment = true
        return
      }
      if (isCommentLine(trimmed)) {
        return
      }

      const textMatch = line.match(JSX_TEXT_NODE)
      if (textMatch && textMatch[1]!.trim().length >= 2) {
        findings.push({
          file: rel,
          line: index + 1,
          kind: "jsx-text",
          text: textMatch[1]!.trim(),
        })
      }

      const attrMatch = line.match(LABEL_ATTR)
      if (attrMatch) {
        findings.push({
          file: rel,
          line: index + 1,
          kind: "label-attr",
          text: attrMatch[1]!,
        })
      }
    })
  }
  return findings
}

function collectTsx(dir: string, out: string[]): void {
  for (const entry of readdirSync(dir)) {
    if (entry === "node_modules" || entry === "dist" || entry.startsWith(".")) {
      continue
    }
    const full = join(dir, entry)
    const stats = statSync(full)
    if (stats.isDirectory()) {
      collectTsx(full, out)
    } else if (
      entry.endsWith(".tsx") &&
      !entry.includes(".test.") &&
      !entry.includes(".stories.") &&
      !full.includes("_generated")
    ) {
      out.push(full)
    }
  }
}

function isAllowlisted(finding: LiteralCopyFinding): boolean {
  return LITERAL_COPY_ALLOWLIST.some(
    (entry) => entry.file === finding.file && entry.text === finding.text
  )
}

interface RuleScope {
  name: string
  path: string
  files: string[]
}

function listRules(dir: string, name: string): RuleScope {
  if (!existsSync(dir)) {
    return { name, path: dir, files: [] }
  }
  const out: string[] = []
  walk(dir, out)
  return { name, path: dir, files: out.map((f) => basename(f, ".md")).sort() }
}

function walk(dir: string, out: string[]): void {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    const stat = statSync(full)
    if (stat.isDirectory()) {
      walk(full, out)
    } else if (entry.endsWith(".md")) {
      out.push(full)
    }
  }
}

function gitChangedFiles(scope: "staged" | "head"): string[] {
  const args =
    scope === "staged"
      ? ["diff", "--cached", "--name-only", "--", "*.ts", "*.tsx"]
      : ["diff", "HEAD~1..HEAD", "--name-only", "--", "*.ts", "*.tsx"]
  const result = spawnSync("git", args, { cwd: REPO_ROOT, encoding: "utf8" })
  if (result.status !== 0) {
    return []
  }
  return result.stdout.split("\n").filter((line) => line.trim().length > 0)
}

function gitGrepHits(file: string, regex: RegExp): string[] {
  const result = spawnSync("git", ["grep", "-nE", regex.source, "--", file], {
    cwd: REPO_ROOT,
    encoding: "utf8",
  })
  if (result.status !== 0) return []
  return result.stdout.split("\n").filter((line) => line.startsWith(file))
}

function isExcluded(file: string): boolean {
  // Generated / vendored -- not hand-written, not in audit scope.
  return (
    file.includes("/node_modules/") ||
    file.includes("/dist/") ||
    file.includes("/.vite/") ||
    file.includes("/coverage/") ||
    file.includes("/test-results/") ||
    file.includes("/Generated/") ||
    file.endsWith(".test.ts") ||
    file.endsWith(".test.tsx") ||
    file.endsWith(".stories.tsx")
  )
}

function main(): void {
  const tsScope = listRules(TYPESCRIPT_RULES_DIR, "typescript")
  const projectScope = listRules(PROJECT_TS_RULES_DIR, "project(coding)")

  // Banner + scope summary to stdout.
  const sep = "=".repeat(60)
  console.log("[rule-audit] " + sep)
  console.log(
    "[rule-audit] REMINDER: read these rule files before any FE code change."
  )
  console.log(
    "[rule-audit] Re-read at the end and self-audit (see RULES-BOOTSTRAP.md)."
  )
  console.log("[rule-audit] " + sep)
  for (const scope of [tsScope, projectScope]) {
    if (scope.files.length === 0) {
      console.log(`[rule-audit] [${scope.name}]: not found at ${scope.path}`)
    } else {
      console.log(
        `[rule-audit] [${scope.name}]: ${scope.files.length} files -- ${scope.files.join(", ")}`
      )
    }
  }
  console.log("[rule-audit] " + sep)

  // Audit FE changes.
  const changed = gitChangedFiles(AUDIT_TARGET)
  const scanned = changed.filter((f) => !isExcluded(f))

  const hitsByFile = new Map<string, Map<string, string[]>>()
  const hitsByPattern = new Map<string, number>()
  for (const p of PATTERNS) hitsByPattern.set(p.id, 0)

  for (const rel of scanned) {
    const fileHits = new Map<string, string[]>()
    for (const p of PATTERNS) {
      const lines = gitGrepHits(rel, p.regex)
      if (lines.length > 0) {
        fileHits.set(p.id, lines)
        hitsByPattern.set(p.id, (hitsByPattern.get(p.id) ?? 0) + lines.length)
      }
    }
    if (fileHits.size > 0) hitsByFile.set(rel, fileHits)
  }

  const total = Array.from(hitsByPattern.values()).reduce((a, b) => a + b, 0)

  // Build markdown report.
  const lines: string[] = []
  lines.push("# Last-commit FE self-audit")
  lines.push("")
  lines.push(`- **Target:** ${AUDIT_TARGET}`)
  lines.push(`- **Files scanned:** ${scanned.length} .ts/.tsx file(s)`)
  lines.push(
    `- **Total violations:** ${total} (across ${PATTERNS.length} patterns)`
  )
  lines.push("")
  lines.push("## Pattern tally")
  lines.push("")
  lines.push("| Pattern | Rule | Hits |")
  lines.push("|---------|------|-----:|")
  for (const p of PATTERNS) {
    const hits = hitsByPattern.get(p.id) ?? 0
    const marker = hits > 0 ? "!" : "ok"
    lines.push(`| ${p.id} | ${p.label} | ${hits} ${marker} |`)
  }
  lines.push("")
  lines.push("## Files with hits")
  lines.push("")
  if (hitsByFile.size === 0) {
    lines.push("_None -- clean._")
  } else {
    const sorted = Array.from(hitsByFile.keys()).sort()
    for (const rel of sorted) {
      const fileHits = hitsByFile.get(rel)!
      lines.push(`### \`${rel}\``)
      for (const p of PATTERNS) {
        const lines_ = fileHits.get(p.id)
        if (lines_) {
          lines.push("")
          lines.push(`- **${p.label}** (${lines_.length} hit(s)):`)
          for (const line of lines_) {
            const parts = line.split(":", 3)
            if (parts.length >= 3) {
              lines.push(`    - line ${parts[1]}: \`${parts[2].trim()}\``)
            } else {
              lines.push(`    - ${line}`)
            }
          }
        }
      }
      lines.push("")
    }
  }

  const dir = dirname(OUTPUT_PATH)
  if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
  writeFileSync(OUTPUT_PATH, lines.join("\n"), "utf8")

  const level = total > 0 ? "[WARN]" : "[INFO]"
  console.log(
    `[rule-audit] ${level} ${total} violation(s) across ${hitsByFile.size} file(s); report at ${OUTPUT_PATH}`
  )

  // Hardcoded-copy ban — deny-by-default (`dashboard-i18n` D10 / task 7.3).
  const literalFindings = scanLiteralCopy().filter((finding) => {
    const allowlisted = isAllowlisted(finding)
    if (!allowlisted) {
      console.log(
        `[rule-audit] [FAIL] hardcoded copy: ${finding.file}:${finding.line} (${finding.kind}) "${finding.text}"`
      )
    }
    return !allowlisted
  })
  console.log(
    `[rule-audit] literal-copy: ${literalFindings.length} violation(s), ${LITERAL_COPY_ALLOWLIST.length} allowlisted fixture(s)`
  )
  if (literalFindings.length > 0) {
    process.exit(1)
  }
}

main()

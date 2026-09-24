// .storybook/test-runner.ts
//
// @storybook/test-runner config (WS16, openspec/changes/add-agentic-test-contour,
// branch feat/agentic-test-contour-spec). Pinned to 0.23.0 — the last release
// whose peerDependencies accept Storybook 8 (0.24+ requires SB 10/11; this
// project is SB 8 by deliberate choice, see frontend-construct-rules.md §3).
//
// Play-function failures are the test-runner's own built-in behaviour
// (`__test(id, hasPlayFn)` throws inside the browser, caught by its generated
// per-story Jest test) — nothing here is needed to wire that up. What lives
// here is `postVisit`: after a story renders (and its play function, if any,
// has run) it re-visits the story once per theme and runs an axe a11y check
// plus a pixel-diff visual check, both from `axe-playwright` / `pixelmatch` —
// deliberately NOT `@storybook/addon-a11y` or `@storybook/addon-vitest`,
// which stay disabled per main.ts's TODO(phase-7) comments (SB10-only
// features, this project is SB8). This is a distinct mechanism running
// through Playwright directly, not a re-enable of either addon.
//
// Scoped to the "ws16-batch1" tag (runs/chat domains, the first batch per
// WS16 tasks.md 16.1/16.4) so `test:storybook` stays a one-shot, bounded run
// while the remaining ~88 stories are picked up incrementally — see
// storybook-tests/README.md for how a future batch adds its tag here.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs"
import { dirname, join, resolve } from "node:path"

import { getViolations, injectAxe } from "axe-playwright"
import type { Page } from "playwright"
import { PNG } from "pngjs"
import pixelmatch from "pixelmatch"
import { z } from "zod"

import type { TestHook, TestRunnerConfig } from "@storybook/test-runner"

type Theme = "dark" | "light"
type Stage = "a11y" | "visual"

const THEMES: readonly Theme[] = ["dark", "light"]
const VIEWPORT = { width: 1440, height: 900 } as const

function isTruthy(value: string | undefined): boolean {
  return value === "1" || value === "true"
}

function numberEnv(value: string | undefined, fallback: number): number {
  const parsed = value === undefined ? Number.NaN : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

const UPDATE_SNAPSHOTS = isTruthy(process.env.STORYBOOK_TEST_UPDATE_SNAPSHOTS)
const CI_MODE = isTruthy(process.env.STORYBOOK_TEST_CI) || isTruthy(process.env.CI)
const BASELINE_DIR = resolve(
  process.cwd(),
  process.env.STORYBOOK_TEST_BASELINE_DIR ?? "storybook-tests/visual-baselines"
)
const DIFF_DIR = resolve(
  process.cwd(),
  process.env.STORYBOOK_TEST_DIFF_DIR ?? "storybook-tests/diffs"
)
// Fraction of pixels allowed to differ before a visual check fails —
// documented tolerance, not pixel-exact (design.md Risks: font/rendering
// drift between CI and local is expected).
const VISUAL_DIFF_RATIO = numberEnv(process.env.STORYBOOK_TEST_VISUAL_THRESHOLD, 0.01)
// Per-pixel colour-distance threshold pixelmatch uses before counting a
// pixel as "different" at all (antialiasing tolerance).
const PIXEL_THRESHOLD = numberEnv(process.env.STORYBOOK_TEST_PIXEL_THRESHOLD, 0.1)
const INCLUDE_TAGS = (process.env.STORYBOOK_TEST_INCLUDE_TAGS ?? "ws16-batch1")
  .split(",")
  .map((tag) => tag.trim())
  .filter((tag) => tag.length > 0)

/**
 * Pre-existing a11y debt this WS16 harness *found* but is not in scope to
 * *fix* — the file-touch boundary for this change is story files and test
 * config, not component `.tsx`/`.module.css` (see the PR description). 61
 * story+theme+rule combinations were already broken (mostly `color-contrast`
 * across Runs/Chat components — looks like one or two token-level root
 * causes, worth a design-system-owned follow-up) the moment this check was
 * turned on. Rather than either silently skip a11y entirely or ship the
 * harness permanently red, known issues are allowlisted by exact
 * `(storyId, theme, ruleId)` — anything NOT already in this file still fails
 * the run, so this cannot mask a regression or a new component's bugs, only
 * the debt captured at seed time. Shrinks as issues get fixed; never grows
 * silently (a `bun run test:storybook` run with a NEW violation fails,
 * forcing a conscious choice to fix it or add it here with a reason).
 */
const knownIssueSchema = z.object({
  storyId: z.string(),
  theme: z.enum(["dark", "light"]),
  ruleId: z.string(),
})

const KNOWN_ISSUES_PATH = resolve(process.cwd(), "storybook-tests/a11y-known-issues.json")

function loadKnownA11yIssues(): ReadonlySet<string> {
  if (!existsSync(KNOWN_ISSUES_PATH)) {
    return new Set()
  }
  const parsed = z.array(knownIssueSchema).parse(JSON.parse(readFileSync(KNOWN_ISSUES_PATH, "utf8")))
  return new Set(parsed.map((issue) => `${issue.storyId}|${issue.theme}|${issue.ruleId}`))
}

const KNOWN_A11Y_ISSUES = loadKnownA11yIssues()

function isKnownA11yIssue(storyId: string, theme: Theme, ruleId: string): boolean {
  return KNOWN_A11Y_ISSUES.has(`${storyId}|${theme}|${ruleId}`)
}

export class StorybookCheckError extends Error {
  override readonly name = "StorybookCheckError"
  readonly stage: Stage
  readonly theme: Theme

  constructor(stage: Stage, theme: Theme, detail: string) {
    super(`[${stage}:${theme}] ${detail}`)
    this.stage = stage
    this.theme = theme
  }
}

function slugify(storyId: string): string {
  return storyId.replace(/[^a-z0-9-]+/gi, "_")
}

/**
 * Switches the theme in place instead of `page.goto()`-ing a `globals=`
 * query param. `--index-json` mode injects `__test` into the page's JS heap
 * once per visit; a full navigation away from that URL wipes it, and the
 * *next* story's `page.evaluate(() => __test(...))` then fails with
 * `ReferenceError: __test is not defined` (observed empirically — every
 * story after the first `postVisit` that navigated). `preview.ts`'s own
 * theme decorator is exactly this one-line class toggle on the iframe's
 * `<html>`, run inside the same page the story is already rendered in — so
 * mirroring it here re-themes the story without touching navigation state.
 */
async function setTheme(page: Page, theme: Theme): Promise<void> {
  await page.evaluate((nextTheme: string) => {
    document.documentElement.classList.toggle("dark", nextTheme === "dark")
  }, theme)
}

async function checkAccessibility(page: Page, storyId: string, theme: Theme): Promise<string | null> {
  await injectAxe(page)
  const violations = await getViolations(page, "#storybook-root")
  if (violations.length === 0) {
    return null
  }

  const unknown = violations.filter((violation) => !isKnownA11yIssue(storyId, theme, violation.id))
  const knownCount = violations.length - unknown.length
  if (knownCount > 0) {
    console.warn(
      `[test:storybook] ${storyId} (${theme}): ${knownCount} known a11y violation(s) allowlisted via storybook-tests/a11y-known-issues.json — not failing`
    )
  }
  if (unknown.length === 0) {
    return null
  }

  const lines = unknown.map((violation) => {
    const targets = violation.nodes.map((node) => node.target.join(" ")).join(", ")
    return `${violation.id} (${violation.impact ?? "unknown"}): ${violation.help} — ${targets}`
  })
  return lines.join("\n")
}

async function checkVisual(page: Page, storySlug: string, theme: Theme): Promise<string | null> {
  const root = page.locator("#storybook-root")
  const buffer = await root.screenshot({ animations: "disabled" })
  const baselinePath = join(BASELINE_DIR, `${storySlug}--${theme}.png`)

  if (UPDATE_SNAPSHOTS) {
    mkdirSync(dirname(baselinePath), { recursive: true })
    writeFileSync(baselinePath, buffer)
    return null
  }

  if (!existsSync(baselinePath)) {
    if (CI_MODE) {
      return `no baseline at ${baselinePath} — run the baseline-refresh job (test:storybook -- --update-snapshots) first`
    }
    console.warn(
      `[test:storybook] no baseline for ${storySlug} (${theme}) — skipping locally, run with --update-snapshots to create one`
    )
    return null
  }

  const baseline = PNG.sync.read(readFileSync(baselinePath))
  const current = PNG.sync.read(buffer)

  if (baseline.width !== current.width || baseline.height !== current.height) {
    const diffPath = join(DIFF_DIR, `${storySlug}--${theme}.current.png`)
    mkdirSync(dirname(diffPath), { recursive: true })
    writeFileSync(diffPath, buffer)
    return `dimension mismatch: baseline ${baseline.width}x${baseline.height} vs current ${current.width}x${current.height} — current saved to ${diffPath}`
  }

  const { width, height } = current
  const diff = new PNG({ width, height })
  const changed = pixelmatch(baseline.data, current.data, diff.data, width, height, {
    threshold: PIXEL_THRESHOLD,
  })
  const ratio = changed / (width * height)

  if (ratio <= VISUAL_DIFF_RATIO) {
    return null
  }

  const diffPath = join(DIFF_DIR, `${storySlug}--${theme}.diff.png`)
  mkdirSync(dirname(diffPath), { recursive: true })
  writeFileSync(diffPath, PNG.sync.write(diff))
  return `${(ratio * 100).toFixed(2)}% of pixels differ (threshold ${(VISUAL_DIFF_RATIO * 100).toFixed(2)}%) — diff image at ${diffPath}`
}

export const preVisit: TestHook = async (page) => {
  await page.setViewportSize(VIEWPORT)
}

export const postVisit: TestHook = async (page, context) => {
  if (context.hasFailure) {
    // The play function (or the visit itself) already failed this story's
    // test — a11y/visual noise on top of that would bury the real cause.
    return
  }

  const storySlug = slugify(context.id)
  const failures: StorybookCheckError[] = []

  for (const theme of THEMES) {
    await setTheme(page, theme)

    const a11yDetail = await checkAccessibility(page, context.id, theme)
    if (a11yDetail !== null) {
      failures.push(new StorybookCheckError("a11y", theme, `${context.id} — ${a11yDetail}`))
    }

    const visualDetail = await checkVisual(page, storySlug, theme)
    if (visualDetail !== null) {
      failures.push(new StorybookCheckError("visual", theme, `${context.id} — ${visualDetail}`))
    }
  }

  if (failures.length > 0) {
    throw new Error(failures.map((failure) => failure.message).join("\n\n"))
  }
}

const config: TestRunnerConfig = {
  tags: { include: INCLUDE_TAGS },
  preVisit,
  postVisit,
}

export default config

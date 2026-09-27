// storybook-tests/harness.spec.ts
//
// The WS16 storybook harness, ported from `.storybook/test-runner.ts`
// (@storybook/test-runner postVisit hooks) onto the vitest `stories`
// project — browser mode, real chromium via the playwright provider, per
// `.storybook/main.ts`'s `@storybook/addon-vitest` (owner decision
// 2026-09-26: Storybook 10.4). One spec, three checks per story:
//
//   1. interaction — `composedStory.run()` renders the story through the
//      real `.storybook/preview.ts` annotations and plays its `play`
//      function (a throwing play fails the test, exactly like the old
//      test-runner's own `__test` mechanism);
//   2. a11y — in-page `axe-core` per theme (dark + light), filtered
//      through `storybook-tests/a11y-known-issues.json` keyed by exact
//      `(storyId, theme, ruleId)` — known debt warns, anything new fails;
//   3. visual — pixelmatch against a CI-generated baseline PNG per theme
//      (`storybook-tests/visual-baselines/`, gitignored — never written
//      outside STORYBOOK_TEST_UPDATE_SNAPSHOTS=1), diff PNG to
//      `storybook-tests/diffs/` on failure.
//
// Scope: stories whose `meta.tags` include a tag from
// STORYBOOK_TEST_INCLUDE_TAGS (default `ws16-batch1` — the same batch the
// old harness ran; widen per README). Everything runs inside ONE iframe
// served by the project's vite server, so no static build, no scratch
// HTTP server and no `setCurrentStory` channel transitions are involved.
//
// That last point retires the old `ws16-portal` workaround wholesale: the
// test-runner-era hang was its per-story channel transition never
// resolving for portal content, and the capture root had to be switched
// to `document.body` for tagged stories. Here every story is composed and
// rendered directly in this iframe, and both checks read `document.body`
// — which by definition already contains portaled children (BottomSheet
// and every react-aria Modal/Dialog). The `ws16-portal` tag remains pure
// metadata in story files: harmless, and no longer load-bearing.
//
// Disk access (baselines/diffs) goes through the /__sb-harness middleware
// (storybook-tests/harness-io.ts) — a browser test cannot use node:fs.

import { page } from "@vitest/browser/context"
import axe from "axe-core"
import pixelmatch from "pixelmatch"
import { describe, it } from "vitest"
import { composeStory, setProjectAnnotations } from "storybook/preview-api"
import { isExportStory, storyNameFromExport, toId } from "storybook/internal/csf"
import * as reactEntryPreview from "@storybook/react/entry-preview"

import previewAnnotations from "../.storybook/preview"
import knownIssuesJson from "./a11y-known-issues.json"

type Theme = "dark" | "light"

const THEMES: readonly Theme[] = ["dark", "light"]

/** Node-side vitest.config.ts injects the run knobs (read there from
 *  process.env, so both `scripts/test-stories.ts` and a direct
 *  `vitest run --project stories` stay in control) as a compile-time
 *  define — the only channel that reliably reaches a browser-mode test. */
declare const __STORYBOOK_TEST_CONFIG__: {
  readonly includeTags: readonly string[]
  readonly updateSnapshots: boolean
  readonly ci: boolean
  readonly visualDiffRatio: number
  readonly pixelThreshold: number
}

const CONFIG = __STORYBOOK_TEST_CONFIG__

interface KnownIssue {
  readonly storyId: string
  readonly theme: string
  readonly ruleId: string
}

const KNOWN_A11Y_ISSUES: ReadonlySet<string> = new Set(
  (knownIssuesJson as readonly KnownIssue[]).map(
    (issue) => `${issue.storyId}|${issue.theme}|${issue.ruleId}`
  )
)

// `.storybook/preview.ts` carries the decorators and parameters every
// story renders through (index.css, i18n bootstrap, theme class toggle) —
// registering it here gives composed stories the exact same annotations
// the book itself uses. `@storybook/react/entry-preview`'s render +
// renderToCanvas + mount are merged alongside — without them, classic CSF
// stories that only declare `meta.component` (no `render`) hit
// NoRenderFunctionError inside `prepareStory`. The book's own UI does the
// same merge via `@storybook/react/preset`'s previewAnnotations; this
// harness does it inline because we don't go through the preset pipeline.
setProjectAnnotations([previewAnnotations, reactEntryPreview])

interface StoryModule {
  readonly default: {
    readonly title?: string
    readonly tags?: readonly string[]
  }
}

const storyModules = import.meta.glob<StoryModule>("../src/**/*.stories.tsx", {
  eager: true,
})

function slugify(storyId: string): string {
  return storyId.replace(/[^a-z0-9-]+/gi, "_")
}

function base64ToBytes(base64: string): Uint8Array {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}

interface DecodedImage {
  readonly width: number
  readonly height: number
  readonly data: Uint8ClampedArray
}

async function decodePng(bytes: Uint8Array): Promise<DecodedImage> {
  const bitmap = await createImageBitmap(new Blob([bytes], { type: "image/png" }))
  const canvas = new OffscreenCanvas(bitmap.width, bitmap.height)
  const context = canvas.getContext("2d")
  if (context === null) {
    throw new Error("cannot acquire a 2d context for PNG decoding")
  }
  context.drawImage(bitmap, 0, 0)
  const imageData = context.getImageData(0, 0, bitmap.width, bitmap.height)
  return { width: bitmap.width, height: bitmap.height, data: imageData.data }
}

async function encodePng(data: Uint8ClampedArray, width: number, height: number): Promise<Uint8Array> {
  const canvas = new OffscreenCanvas(width, height)
  const context = canvas.getContext("2d")
  if (context === null) {
    throw new Error("cannot acquire a 2d context for PNG encoding")
  }
  context.putImageData(new ImageData(data, width, height), 0, 0)
  const blob = await canvas.convertToBlob({ type: "image/png" })
  return new Uint8Array(await blob.arrayBuffer())
}

async function putHarnessPng(kind: "baseline" | "diff", name: string, bytes: Uint8Array): Promise<void> {
  const response = await fetch(`/__sb-harness/${kind}/${name}`, {
    method: "PUT",
    headers: { "Content-Type": "image/png" },
    body: bytes,
  })
  if (!response.ok) {
    throw new Error(`harness io: PUT ${kind}/${name} -> ${response.status}`)
  }
}

/** The story's own theme decorator mirrors exactly this class toggle on
 *  `<html>` — switching it in place after `run()` re-themes the already
 *  rendered story without a second render, the same trick the old
 *  harness's `setTheme` used. */
function setTheme(theme: Theme): void {
  document.documentElement.classList.toggle("dark", theme === "dark")
}

async function nextPaint(): Promise<void> {
  await new Promise<void>((resolve) => {
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        resolve()
      })
    })
  })
}

async function checkAccessibility(storyId: string, theme: Theme): Promise<void> {
  const results = await axe.run(document.body)
  const violations = results.violations
  const unknown = violations.filter(
    (violation) => !KNOWN_A11Y_ISSUES.has(`${storyId}|${theme}|${violation.id}`)
  )
  const knownCount = violations.length - unknown.length
  if (knownCount > 0) {
    console.warn(
      `[test:stories] ${storyId} (${theme}): ${knownCount} known a11y violation(s) allowlisted via storybook-tests/a11y-known-issues.json — not failing`
    )
  }
  if (unknown.length > 0) {
    const lines = unknown.map((violation) => {
      const targets = violation.nodes.map((node) => node.target.join(" ")).join(", ")
      return `${violation.id} (${violation.impact ?? "unknown"}): ${violation.help} — ${targets}`
    })
    throw new Error(`[a11y:${theme}] ${storyId} — ${lines.join("\n")}`)
  }
}

async function checkVisual(storyId: string, theme: Theme): Promise<void> {
  const slug = slugify(storyId)
  const shot = await page.screenshot({ save: false })
  if (shot.base64 === undefined) {
    throw new Error(`[visual:${theme}] ${storyId} — screenshot returned no PNG bytes`)
  }
  const currentBytes = base64ToBytes(shot.base64)

  const baselineResponse = await fetch(`/__sb-harness/baseline/${slug}--${theme}.png`)

  if (CONFIG.updateSnapshots) {
    await putHarnessPng("baseline", `${slug}--${theme}.png`, currentBytes)
    return
  }

  if (baselineResponse.status === 404) {
    if (CONFIG.ci) {
      throw new Error(
        `[visual:${theme}] no baseline at storybook-tests/visual-baselines/${slug}--${theme}.png — run the baseline-refresh job (test:stories -- --update-snapshots) first`
      )
    }
    console.warn(
      `[test:stories] no baseline for ${slug} (${theme}) — skipping locally, run with --update-snapshots to create one`
    )
    return
  }
  if (!baselineResponse.ok) {
    throw new Error(`[visual:${theme}] baseline fetch -> ${baselineResponse.status}`)
  }

  const baseline = await decodePng(new Uint8Array(await baselineResponse.arrayBuffer()))
  const current = await decodePng(currentBytes)

  if (baseline.width !== current.width || baseline.height !== current.height) {
    await putHarnessPng("diff", `${slug}--${theme}.current.png`, currentBytes)
    throw new Error(
      `[visual:${theme}] dimension mismatch: baseline ${baseline.width}x${baseline.height} vs current ${current.width}x${current.height} — current saved to storybook-tests/diffs/${slug}--${theme}.current.png`
    )
  }

  const { width, height } = current
  const diff = new Uint8ClampedArray(width * height * 4)
  const changed = pixelmatch(baseline.data, current.data, diff, width, height, {
    threshold: CONFIG.pixelThreshold,
  })
  const ratio = changed / (width * height)

  if (ratio > CONFIG.visualDiffRatio) {
    await putHarnessPng("diff", `${slug}--${theme}.diff.png`, await encodePng(diff, width, height))
    throw new Error(
      `[visual:${theme}] ${(ratio * 100).toFixed(2)}% of pixels differ (threshold ${(CONFIG.visualDiffRatio * 100).toFixed(2)}%) — diff image at storybook-tests/diffs/${slug}--${theme}.diff.png`
    )
  }
}

for (const [modulePath, storyModule] of Object.entries(storyModules)) {
  const meta = storyModule.default
  const metaTags = meta.tags ?? []
  const inScope = CONFIG.includeTags.some((tag) => metaTags.includes(tag))
  if (!inScope) {
    continue
  }

  describe(meta.title ?? modulePath, () => {
    for (const [exportName, storyExport] of Object.entries(storyModule)) {
      if (exportName === "default" || !isExportStory(exportName, meta)) {
        continue
      }

      it(storyNameFromExport(exportName), async () => {
        const composed = composeStory(
          storyExport as never,
          meta as never,
          undefined,
          undefined,
          exportName
        )
        const storyId = toId(meta.title ?? modulePath, storyNameFromExport(exportName))

        // Render + play once, in the preview's default globals (theme
        // defaults to dark) — then re-theme in place for each check, the
        // same shape the old postVisit hook had.
        await composed.run()

        for (const theme of THEMES) {
          setTheme(theme)
          await nextPaint()
          await checkAccessibility(storyId, theme)
          await checkVisual(storyId, theme)
        }
      })
    }
  })
}

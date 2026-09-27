import path from "node:path"
import { defineConfig } from "vitest/config"
import react from "@vitejs/plugin-react"
import tailwindcss from "@tailwindcss/vite"
import { playwright } from "@vitest/browser-playwright"

import { storybookHarnessIo } from "./storybook-tests/harness-io.ts"

// https://vitest.dev/config/
//
// Two test projects, two tiers:
//
//   unit   — jsdom, everything from `src/**/*.test.*`. What `bun run test`
//            runs. Unchanged from the pre-projects flat config.
//   stories — Storybook stories as tests (owner decision 2026-09-26:
//            Storybook 10.4, @storybook/addon-vitest): real chromium via
//            the playwright provider, per storybook-tests/harness.spec.ts.
//            What `bun run test:stories` runs — interaction (play), then
//            per theme (dark/light) an allowlisted axe check and a
//            pixelmatch visual diff. Not part of `bun run test`.
//
// The stories project deliberately does NOT extend ./vite.config.ts: the
// app config carries tanstackRouter (route-tree codegen irrelevant to
// stories) and a strict dev-server port. It re-declares the three pieces
// a story render actually needs — react JSX transform, the `@/` alias,
// and @tailwindcss/vite, which `.storybook/preview.ts`'s index.css import
// requires to compile in a real browser.

function isTruthyEnv(value: string | undefined): boolean {
  return value === "1" || value === "true"
}

function numberEnv(value: string | undefined, fallback: number): number {
  const parsed = value === undefined ? Number.NaN : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

/** Run knobs for the stories harness, read from the environment HERE (node
 *  side, where process.env exists) and baked into the browser spec as a
 *  compile-time define — the only channel that reliably reaches a
 *  browser-mode test. Scripts/test-stories.ts sets the same env vars. */
const STORYBOOK_TEST_CONFIG = {
  includeTags: (process.env.STORYBOOK_TEST_INCLUDE_TAGS ?? "ws16-batch1")
    .split(",")
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0),
  updateSnapshots: isTruthyEnv(process.env.STORYBOOK_TEST_UPDATE_SNAPSHOTS),
  ci: isTruthyEnv(process.env.STORYBOOK_TEST_CI) || isTruthyEnv(process.env.CI),
  visualDiffRatio: numberEnv(process.env.STORYBOOK_TEST_VISUAL_THRESHOLD, 0.01),
  pixelThreshold: numberEnv(process.env.STORYBOOK_TEST_PIXEL_THRESHOLD, 0.1),
}

export default defineConfig({
  // vitest's browser-mode stories project lifts its own vite dev server on
  // port 63315 by default. On the Windows sandbox that vitest runs in,
  // port 63315 is **outside** the user-bindable range (EACCES regardless
  // of host) — confirmed by the same test against 127.0.0.1:63315 failing
  // in plain `node:net` too. Pick a free port inside the agentic port
  // pool (17000–17200, `.agents/rules/process/ports.md`); 17180 is the
  // first scratch slot after the reserved 17010/17173. Host pinned to
  // 127.0.0.1 so node's IPv6-default listen doesn't EACCES first.
  server: { host: "127.0.0.1" },
  test: {
    projects: [
      {
        plugins: [react()],
        resolve: {
          alias: {
            "@": path.resolve(import.meta.dirname, "./src"),
          },
        },
        test: {
          name: "unit",
          globals: true,
          environment: "jsdom",
          setupFiles: ["./vitest.setup.ts"],
          include: ["src/**/*.test.{ts,tsx}"],
          exclude: ["e2e/**"],
        },
      },
      {
        plugins: [react(), tailwindcss(), storybookHarnessIo()],
        resolve: {
          alias: {
            "@": path.resolve(import.meta.dirname, "./src"),
          },
        },
        define: {
          __STORYBOOK_TEST_CONFIG__: JSON.stringify(STORYBOOK_TEST_CONFIG),
        },
        test: {
          name: "stories",
          include: ["storybook-tests/**/*.spec.ts"],
          // Generous per-story budget: the first story of a file pays the
          // vite transform of its whole module graph (the reference
          // project measured cold-cache first-tests at ~23s).
          testTimeout: 60_000,
          hookTimeout: 120_000,
          browser: {
            enabled: true,
            headless: true,
            provider: playwright(),
            api: { host: "127.0.0.1", port: 17180 },
            instances: [
              {
                browser: "chromium",
                viewport: { width: 1440, height: 900 },
              },
            ],
          },
        },
      },
    ],
    coverage: {
      provider: "v8",
      reporter: ["text", "json", "html", "lcov"],
      include: ["src/**/*"],
      exclude: [
        "src/app/main.tsx",
        "src/routeTree.gen.ts",
        "src/domains/**/AGENTS.md",
        "src/design/**",
        "src/app/mocks/**",
        "**/*.stories.ts",
        "**/*.stories.tsx",
        "**/*.d.ts",
      ],
      // NOT a live gate yet, and the number below is a target, not a fact.
      // `test:coverage` used to end in `|| true`, so the floor had never
      // failed anything; with that gone the real reading is lines 56.6 /
      // functions 58.6 / branches 57.5 / statements 57.1. The 70 stays as
      // the agreed floor — lowering it to whatever passes today would make
      // the gate a decoration again — and CI runs `test`, not
      // `test:coverage`, until the suites close that gap. Wiring the
      // coverage run into CI before then only buys a permanently red job.
      thresholds: {
        lines: 70,
        functions: 70,
        branches: 70,
        statements: 70,
      },
    },
  },
})

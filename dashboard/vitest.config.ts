import path from "node:path"
import { defineConfig } from "vitest/config"
import react from "@vitejs/plugin-react"
import { playwright } from "@vitest/browser-playwright"

import { storybookTest } from "@storybook/addon-vitest/vitest-plugin"

// https://vitest.dev/config/
//
// Two test projects, two tiers:
//
//   unit   — jsdom, everything from `src/**/*.test.*`. What `bun run test`
//            runs. Unchanged from the pre-projects flat config.
//   stories — Storybook 10 stories as vitest tests via
//            `@storybook/addon-vitest`'s `storybookTest()` plugin (owner
//            resolution 2026-09-26: Storybook 10.6). The plugin generates
//            one vitest test per story; the `play` function runs in real
//            chromium through the playwright provider; addon-a11y
//            (parameters.a11y.test = 'error' in .storybook/preview.ts)
//            turns axe violations into test failures. What
//            `bun run test:stories` runs.
//
// The stories project extends `true` (vitest workspace inheritance) so the
// unit project's react/tailwind/alias chain stays untouched; only the
// `name: 'stories'` project adds the addon-vitest plugin and the browser
// provider below.

export default defineConfig({
  // vitest's browser-mode stories project lifts its own vite dev server on
  // port 63315 by default. On the Windows sandbox that vitest runs in,
  // port 63315 is **outside** the user-bindable range (EACCES regardless
  // of host) — confirmed by the same test against 127.0.0.1:63315 failing
  // in plain `node:net` too. Pin a free port inside the agentic port
  // pool (17000–17200, `.agents/rules/process/ports.md`); 17184 is the
  // first scratch slot after 17173 (Dashboard) and 17183
  // (`comuki-e2e` MinIO console) — 17180 is taken by `comuki-e2e-host`
  // (WS13, `.agents/rules/process/ports.md`). Host pinned to 127.0.0.1 so
  // node's IPv6-default listen doesn't EACCES first.
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
        extends: true,
        plugins: [
          storybookTest({
            configDir: path.resolve(import.meta.dirname, ".storybook"),
            // `ws16-batch1` — runs/chat, the first batch (WS16). `procedures`
            // — the procedure workbench stories (add-project-procedures
            // prototype): added per storybook-tests/README.md's "a future
            // batch adds its tag here".
            tags: { include: ["ws16-batch1", "procedures"] },
          }),
        ],
        resolve: {
          alias: {
            "@": path.resolve(import.meta.dirname, "./src"),
          },
        },
        test: {
          name: "stories",
          // Generous per-story budget: the first story of a file pays the
          // vite transform of its whole module graph (the reference
          // project measured cold-cache first-tests at ~23s).
          testTimeout: 60_000,
          hookTimeout: 120_000,
          browser: {
            enabled: true,
            headless: true,
            provider: playwright(),
            api: { host: "127.0.0.1", port: 17184 },
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
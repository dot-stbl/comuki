import { defineConfig } from "vitest/config"
import react from "@vitejs/plugin-react"
import path from "path"

// https://vitest.dev/config/
export default defineConfig({
  plugins: [react()],

  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },

  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    globals: true,
    include: ["src/**/*.test.{ts,tsx}"],
    exclude: ["e2e/**"],
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

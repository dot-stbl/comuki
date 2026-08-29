import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "path";

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
      thresholds: {
        lines: 70,
        functions: 70,
        branches: 70,
        statements: 70,
      },
    },
  },
});

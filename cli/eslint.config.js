/**
 * CLI-local lint configuration (issue #73 canon remediation).
 *
 * Minimal ESLint flat config mirroring the spike's proven setup:
 *   - typescript-eslint recommended
 *   - consistent-type-imports (use `import type` for type-only imports)
 *   - no-unused-vars (allow `_` prefix for intentional discards)
 *
 * Scoped to cli/ source. The throwaway OpenTUI spike (spikes/opentui)
 * that this config used to exclude was deleted once ADR-0002 was
 * accepted and the production OpenTUI host landed in src/ (see
 * ADR-0002 §9/§10) — nothing here references it anymore.
 */
import tseslint from "typescript-eslint"

export default tseslint.config(
  {
    ignores: [
      "node_modules/**",
      "bin/**",
      "scripts/build.ts",
      // Generated contract artifacts (kubb http/ + Comuki.Codegen.Realtime
      // realtime.ts) — machine-written, read-only; typecheck still covers
      // them via tsconfig include.
      "src/contracts/_generated/**",
      "comuki",
      "comuki.exe",
    ],
  },
  ...tseslint.configs.recommended,
  {
    files: ["src/**/*.ts", "src/**/*.tsx"],
    rules: {
      "@typescript-eslint/consistent-type-imports": [
        "error",
        { prefer: "type-imports", fixStyle: "separate-type-imports" },
      ],
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  {
    files: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    rules: {
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_", caughtErrors: "none" },
      ],
    },
  },
)

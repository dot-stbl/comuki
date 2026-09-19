/**
 * Spike-local lint configuration.
 *
 * Minimal ESLint flat config:
 *   - typescript-eslint with type-checked rules
 *   - consistent-type-imports (use `import type` for type-only imports)
 *   - no-unused-vars (allow `_` prefix for intentional discards)
 *
 * This config is **spike-local**. It does not touch the repo-global
 * `.eslintrc*` or any other directory's lint configuration. The
 * spike may be deleted once the production CLI host is shipped;
 * this config dies with it.
 */
import tseslint from "typescript-eslint"
import importPlugin from "eslint-plugin-import"

export default tseslint.config(
  {
    ignores: [
      "node_modules/**",
      "bin/**",
      ".gitignore",
      "README.md",
      "package.json",
      "tsconfig.json",
      "evidence/**",
    ],
  },
  ...tseslint.configs.recommended,
  {
    files: ["src/**/*.ts", "src/**/*.tsx", "tests/**/*.ts", "tests/**/*.tsx", "scripts/**/*.ts"],
    plugins: { import: importPlugin },
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: "module",
    },
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
    // Tests may have their own fixtures; relax the import rule for
    // `bun:test` types where multiple spellings are common.
    files: ["tests/**/*.ts", "tests/**/*.tsx"],
    rules: {
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_", caughtErrors: "none" },
      ],
    },
  },
)

// Flat config for the three TS agent packages (bun workspace).
//
// Same shape as `dashboard/eslint.config.js` minus the React plugins: these
// packages are headless bun/Claude Code processes, not a browser bundle.
// `.mjs` because the workspace root is CommonJS (no `"type": "module"`),
// while the per-package manifests are ESM.
//
// Note on the typescript pin: this workspace ran `typescript@7`, and
// typescript-eslint refuses to load against it — TS 7 moved the JS compiler
// API out of the package root, so `require("typescript")` returns version
// metadata and nothing else (typescript-eslint#10940 tracks support). The
// devDependency is now `~6`, the same range `dashboard` uses; `tsc --noEmit`
// behaves identically and the whole repo typechecks on one compiler. Raise
// it again once typescript-eslint ships TS >=7 support.
import js from "@eslint/js"
import globals from "globals"
import tseslint from "typescript-eslint"
import { defineConfig, globalIgnores } from "eslint/config"

export default defineConfig([
  globalIgnores(["**/node_modules/**", "**/dist/**"]),
  {
    files: ["**/*.ts"],
    extends: [js.configs.recommended, tseslint.configs.recommended],
    languageOptions: {
      globals: {
        ...globals.node,
        // bun global (Bun.stdin, Bun.file) — @types/bun covers the types,
        // eslint needs the name declared for `no-undef` in non-TS scopes.
        Bun: "readonly",
      },
    },
  },
])

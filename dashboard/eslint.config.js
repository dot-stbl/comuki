import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores([
    'dist',
    'storybook-static',
    // v8 coverage report — html/js written by `test:coverage`, not source.
    'coverage',
    // Kubb-generated OpenAPI client — do not hand-lint. Only `_generated/`
    // is machine-written (see kubb.config.ts `output.path`); the siblings
    // (kubb-client transport, problem/polling helpers, mock stores) are
    // hand-written and get linted like the rest of src.
    'src/shared/api/_generated/**',
  ]),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  // TanStack file routes export `Route` (+ showcase co-locates helpers)
  {
    files: ['src/routes/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  // Kit primitives export helpers / contexts alongside components
  {
    files: ['src/shared/ui/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
      // embla carousel + similar vendor patterns
      'react-hooks/set-state-in-effect': 'off',
    },
  },
  {
    files: ['src/shared/hooks/**/*.{ts,tsx}'],
    rules: {
      'react-hooks/set-state-in-effect': 'off',
    },
  },
])

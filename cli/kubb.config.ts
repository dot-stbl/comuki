import { existsSync } from "node:fs"
import { resolve } from "node:path"
import { defineConfig } from "@kubb/core"
import { pluginOas } from "@kubb/plugin-oas"
import { pluginTs } from "@kubb/plugin-ts"

// Kubb v4 (issue #84 chat vertical slice). Source of truth = the
// backend-emitted OpenAPI document at /comuki.orchestrator/artifacts/openapi.json,
// regenerated on every Debug `dotnet build` via Comuki.Host's
// Microsoft.AspNetCore.OpenApi emission. Run: `bun run generate:contracts`.
// Output is a read-only build artefact under cli/src/contracts/_generated/ —
// the HTTP half of the contract tree only (types, no client/hooks); the
// realtime half is produced by `dotnet run --project
// tools/Comuki.Codegen.Realtime` and lands next to it as a sibling .ts file.

// Emitted by Comuki.Host.csproj (OpenApiDocumentsDirectory resolved relative
// to the csproj; for a worktree that lands at `<worktree>/artifacts/openapi.json`).
// From `cli/`, that's one segment up. The guard runs BEFORE output.clean:
// true fires, otherwise `output.clean` wipes the generated tree on every
// miss instead of failing fast.
//
// Drift gate (`scripts/contracts-drift.ts`) sets `KUBB_INPUT_SPEC` to a
// line-ending-normalized copy of the freshly-built openapi.json. Without
// normalization the .NET emitter's Environment.NewLine (CRLF on Windows,
// LF on Linux) propagates into the generated JSON descriptions and the
// gate drifts between contributor machines.
const SPEC_PATH = process.env.KUBB_INPUT_SPEC ?? "../artifacts/openapi.json"

if (!existsSync(resolve(process.cwd(), SPEC_PATH))) {
  console.error(
    `\n[kubb] СПЕКА НЕ НАЙДЕНА: ${resolve(process.cwd(), SPEC_PATH)}\n` +
      "  Это build-артефакт, он gitignored и в новое дерево не приезжает.\n" +
      "  Собрать: dotnet build comuki.slnx -c Debug\n" +
      "  ВАЖНО: dotnet должен быть в PATH, а не только вызван по пути —\n" +
      "  ApiDescription.Server спавнит `dotnet` из PATH и иначе\n" +
      "  падает с «command not found» (код 127), собрав при этом всё\n" +
      "  остальное успешно. На macOS: PATH=$HOME/.dotnet:$PATH dotnet build …\n" +
      "  Генерация остановлена ДО output.clean — дерево _generated/ не тронуто.\n"
  )
  throw new Error("[kubb] input spec not found — see the message above")
}

export default defineConfig({
  root: ".",
  input: {
    path: SPEC_PATH,
  },
  output: {
    // Two siblings live under cli/src/contracts/_generated/:
    //   http/      — emitted by kubb (this file, types subfolder)
    //   realtime.ts — emitted by dotnet Comuki.Codegen.Realtime
    // The leading underscore signals "machine-written, ignore in code review".
    path: "./src/contracts/_generated/http",
    // Wipe the tree before every run so types removed from the backend
    // don't linger as orphan files — kubb never prunes on its own.
    clean: true,
    // Kubb emits .ts-suffixed imports by default; strip them so generated
    // tree compiles under our tsconfig (no allowImportingTsExtensions).
    extension: { ".ts": "" },
    // No entry re-exports: domains import concrete paths (e.g.
    // `import type { ChatSessionView } from "@/contracts/_generated/http/types/ChatSessionView"`).
    barrelType: false,
    // Disable kubb's default prettier post-hook. Without this, kubb
    // 4.39.2 spawns `prettier --write` on every run and fails with
    // ENOENT when prettier isn't installed, or produces byte-level
    // drift between machines when prettier is installed at a different
    // version. The drift gate then fails. `false` is the supported
    // kubb way to opt out (@kubb/core types.ts:208 —
    // `format?: 'auto' | 'prettier' | 'biome' | 'oxfmt' | false`).
    format: false,
  },
  plugins: [
    pluginOas(),
    pluginTs({
      output: {
        path: "types",
        barrelType: false,
      },
    }),
  ],
})

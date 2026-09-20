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
const SPEC_PATH = "../artifacts/openapi.json"

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
    // Kubb's default done-hook pipes output through prettier; the CLI has no
    // prettier (eslint owns formatting). Disable the hook — raw kubb output
    // is deterministic on its own, which is what the drift diff needs.
    hooks: { done: [] },
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

import { existsSync } from "node:fs";
import { resolve } from "node:path";
import { defineConfig } from "@kubb/core";
import { pluginClient } from "@kubb/plugin-client";
import { pluginOas } from "@kubb/plugin-oas";
import { pluginReactQuery } from "@kubb/plugin-react-query";
import { pluginTs } from "@kubb/plugin-ts";
import { pluginZod } from "@kubb/plugin-zod";

// Kubb v4 (issue #29). Source of truth = the backend-emitted OpenAPI document
// at /comuki.orchestrator/artifacts/openapi.json, regenerated on every Debug
// `dotnet build` via Comuki.Host's Microsoft.AspNetCore.OpenApi emission.
// Run: `bun run generate-api`. Output is a read-only build artefact — never
// hand-edit anything under src/shared/api/{clients,hooks,schemas,types,zod}.

// Integration seam: generated client/hooks import the transport from
// @/shared/api/kubb-client (pluginClient.importPath / pluginReactQuery.client
// .importPath), which reads VITE_API_BASE_URL + forces credentials:'include'
// for cookie auth. Mock-first screens (VITE_USE_MOCK) bypass this surface
// via the hand-written src/shared/api/mock/* stores.

// Emitted by Comuki.Host.csproj (OpenApiDocumentsDirectory resolved relative
// to the csproj; for a worktree that lands at `<worktree>/artifacts/openapi.json`).
// From `dashboard/`, that's one segment up. The guard runs BEFORE
// output.clean: true fires, otherwise `output.clean` wipes the generated tree
// on every miss instead of failing fast (the 1334-files-lost pattern from
// console.x).
const SPEC_PATH = "../artifacts/openapi.json";

if (!existsSync(resolve(process.cwd(), SPEC_PATH))) {
  console.error(
    `\n[kubb] СПЕКА НЕ НАЙДЕНА: ${resolve(process.cwd(), SPEC_PATH)}\n` +
      "  Это build-артефакт, он gitignored и в новое дерево не приезжает.\n" +
      "  Собрать: dotnet build comuki.slnx -c Debug\n" +
      "  ВАЖНО: dotnet должен быть в PATH, а не только вызван по пути —\n" +
      "  ApiDescription.Server спавнит `dotnet` из PATH и иначе\n" +
      "  падает с «command not found» (код 127), собрав при этом всё\n" +
      "  остальное успешно. На macOS: PATH=$HOME/.dotnet:$PATH dotnet build …\n" +
      "  Генерация остановлена ДО output.clean — дерево generated/ не тронуто.\n",
  );
  throw new Error("[kubb] input spec not found — see the message above");
}

export default defineConfig({
  root: ".",
  input: {
    path: SPEC_PATH,
  },
  output: {
    // Emitted tree lives under `_generated/` so `output.clean: true` wipes
    // only kubb's own output. Hand-written siblings — the kubb-client
    // transport adapter (resolved by every hook via `importPath`) and the
    // mock-first seeds in `mock/` — survive every regen. The leading
    // underscore signals "kubb-owned, ignore in code review".
    path: "./src/shared/api/_generated",
    // Wipe the tree before every run so operations/schemas removed from the
    // backend don't linger as orphan files — kubb never prunes on its own.
    clean: true,
    // Kubb emits .ts-suffixed imports by default; strip them so generated
    // tree compiles under our tsconfig (no allowImportingTsExtensions).
    extension: { ".ts": "" },
    // No entry re-exports: domains import concrete paths (e.g.
    // `import { getHealth } from '@/shared/api/_generated/clients/getHealth'`);
    // the root barrel adds nothing but a 597KB index.ts that must be re-read
    // on every regen.
    barrelType: false,
  },
  plugins: [
    pluginOas(),
    pluginTs({
      output: {
        path: "types",
        barrelType: false,
      },
    }),
    pluginZod({
      output: {
        path: "zod",
        barrelType: false,
      },
      // Backend (.NET System.Text.Json) serializes DateTimeOffset with a
      // timezone offset (`...+00:00`); plain z.string().datetime() REJECTS
      // that. `'stringOffset'` emits `z.string().datetime({ offset: true })`
      // so ACL mappers parse the real wire dates. Domain type stays
      // `string` — the screen calls `.datetime({ offset: true })` only when
      // it needs to round-trip the value.
      dateType: "stringOffset",
    }),
    pluginClient({
      output: {
        path: "clients",
        barrelType: false,
      },
      // v4: `client` preset and `importPath` are mutually exclusive at the
      // top level. Use importPath to route every call through OUR transport
      // (VITE_API_BASE_URL + credentials:'include' + 401/403 rejection)
      // instead of kubb's default fetch.
      importPath: "@/shared/api/kubb-client",
      dataReturnType: "data",
    }),
    pluginReactQuery({
      output: {
        path: "hooks",
        barrelType: false,
      },
      client: {
        importPath: "@/shared/api/kubb-client",
      },
      query: {
        methods: ["get"],
      },
      mutation: {
        methods: ["post", "put", "patch", "delete"],
      },
    }),
  ],
});

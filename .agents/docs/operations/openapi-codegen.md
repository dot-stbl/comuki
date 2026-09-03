# OpenAPI emission → FE codegen

The FE/BE contract is the **`artifacts/openapi.json`** document emitted
at `dotnet build` time. The dashboard reads it via **kubb v4.39.2** and
turns it into typed React Query hooks + zod schemas + a hand-wired
transport adapter. The whole pipeline is a one-liner from either end:

```bash
# BE emits openapi.json as a Debug-only build side-effect.
dotnet build comuki.slnx -c Debug

# FE regenerates clients/hooks/types/zod from openapi.json.
cd dashboard && bun run generate-api
```

> Issue: #29 — Wire OpenAPI emission + align kubb with console.x pattern.

## Why this lives where it does

`Microsoft.Extensions.ApiDescription.Server` ships as a build-time
tool that launches the host process as `GetDocument.Insider`,
enumerates endpoints, and stops it. Since .NET 7 every
`IHostedService` starts before the document is captured — without
intervention a plain `dotnet build` would run the orchestrator
migrators and workers (real DB I/O, real Translator launch attempts)
just to emit a JSON file.

`OpenApiBuildTimeExtensions` strips the application's own hosted
services (`Comuki.*` namespaces) during build-time document generation
and leaves the framework's web-host service intact, so document
capture is unchanged.

## Build wiring (`Comuki.Host.csproj`)

```xml
<PropertyGroup>
  <!-- Resolved relative to the csproj: platform/src/host/Comuki.Host
       → ../.. → /comuki.orchestrator; artifacts lives at the repo root. -->
  <OpenApiDocumentsDirectory>../../../../artifacts</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocuments Condition="'$(Configuration)'=='Debug'">true</OpenApiGenerateDocuments>
  <OpenApiGenerateDocuments Condition="'$(Configuration)'=='Release'">false</OpenApiGenerateDocuments>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Server">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>

<!-- Rename Comuki.Host.json → openapi.json so dashboard/kubb.config.ts
     has a single canonical input name. -->
<Target Name="RenameOpenApiOutputToCanonicalName"
        AfterTargets="_GenerateOpenApiDocuments"
        Condition="'$(Configuration)'=='Debug' and Exists('$(MSBuildProjectDirectory)\$(OpenApiDocumentsDirectory)\$(MSBuildProjectName).json')">
  <Move SourceFiles="$(MSBuildProjectDirectory)\$(OpenApiDocumentsDirectory)\$(MSBuildProjectName).json"
        DestinationFiles="$(MSBuildProjectDirectory)\$(OpenApiDocumentsDirectory)\openapi.json"
        OverwriteReadOnlyFiles="true"/>
</Target>
```

Three things to notice:

1. **`OpenApiDocumentsDirectory` is relative to the csproj** — three
   segments up (`../../../../artifacts`) lands at the repo root. This
   is the same on every worktree.
2. **Debug-only.** Release builds skip document generation because the
   `Microsoft.AspNetCore.OpenApi 10.0.9` source generator still emits
   against the 2.x writable `IOpenApiMediaType.Example`, which the
   3.x source generator migration will fix.
3. **The rename target fires *after* `_GenerateOpenApiDocuments`** —
   `Comuki.Host.json` is the framework's hard-coded output name; we
   rename to `openapi.json` so the FE has one canonical input.

## FE wiring (`dashboard/kubb.config.ts`)

```ts
// Emitted by Comuki.Host.csproj; for a worktree the file lands at
// <worktree>/artifacts/openapi.json. From `dashboard/`, that's one
// segment up.
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
```

### The 1334-files-lost guard

The check above runs **before** `output.clean: true` fires. If kubb
sees a missing spec and proceeds, `output.clean` wipes the generated
tree and kubb errors out without restoring it. That's the
"console.x 1334-files-lost incident" — the regenerated tree was
empty, the previous hand-written siblings were nuked, the only
salvage was `git restore`. The guard throws **before** `clean`.

### Plugins

```ts
plugins: [
  pluginOas(),
  pluginTs({ output: { path: "types", barrelType: false } }),
  pluginZod({
    output: { path: "zod", barrelType: false },
    // Backend (.NET System.Text.Json) serializes DateTimeOffset with a
    // timezone offset (`...+00:00`); plain z.string().datetime() REJECTS
    // that. `'stringOffset'` emits `z.string().datetime({ offset: true })`
    // so ACL mappers parse the real wire dates.
    dateType: "stringOffset",
  }),
  pluginClient({
    output: { path: "clients", barrelType: false },
    // v4: `client` preset and `importPath` are mutually exclusive at the
    // top level. Use importPath to route every call through OUR transport
    // (VITE_API_BASE_URL + credentials:'include' + 401/403 rejection)
    // instead of kubb's default fetch.
    importPath: "@/shared/api/kubb-client",
    dataReturnType: "data",
  }),
  pluginReactQuery({
    output: { path: "hooks", barrelType: false },
    client: { importPath: "@/shared/api/kubb-client" },
    query:   { methods: ["get"] },
    mutation:{ methods: ["post", "put", "patch", "delete"] },
  }),
],
```

### Output tree

```
dashboard/src/shared/api/
├── kubb-client.ts                  # hand-written transport adapter
├── mock/                           # hand-written seeds (mock-first screens)
└── _generated/                     # kubb-owned, wiped on every regen
    ├── clients/{getHealth,...}.ts
    ├── hooks/{useGetHealth,...}.ts
    ├── schemas/{...}.zod.ts
    └── types/{...}.ts
```

- `_generated/` lives under a leading underscore: review noise filter,
  no manual edits.
- `output.clean: true` wipes **only** `_generated/`. The hand-written
  `kubb-client.ts` (transport) and `mock/` (seeds) survive every regen.
- `barrelType: false` — no entry re-exports. Domains import concrete
  paths (`@/shared/api/_generated/clients/getHealth`) instead of
  re-reading a 597KB root index on every regen.

## Transport — `kubb-client.ts`

`pluginClient.importPath: '@/shared/api/kubb-client'` routes every
generated hook call through this adapter instead of kubb's default
fetch. Two reasons:

1. **Mock-first by default.** When `VITE_API_BASE_URL` is empty
   (fresh clone, `dev:mock`, Storybook), a generated hook must NOT
   ping a random host. The adapter surfaces the error early — when a
   screen is wired against real backend data, the operator sets
   `VITE_API_BASE_URL=http://localhost:17173` and restarts the dev
   server.
2. **Cookie auth.** Comuki's orchestrator authn is cookie-based
   (`ck_` API key passthrough + cookie session). `fetch` defaults to
   `credentials: 'same-origin'` which LOSES the cookie on the
   cross-origin call to the host. The adapter forces
   `credentials: 'include'` so the dashboard reads/writes the host
   session even when the SPA is served from a different origin
   (deploy pattern: Vite :17173 → host :NNNN).

401/403 → the adapter rejects (no auto-redirect; the route guard
watching `/api/v1/auth/me` owns the bounce).

## Scripts (`dashboard/package.json`)

```json
{
  "scripts": {
    "dev":         "vite",
    "dev:mock":    "vite",
    "predev":      "bun run generate-api",
    "generate-api":"dotnet build ../comuki.slnx && bunx @kubb/cli generate",
    "build":       "tsc -b && vite build",
    "typecheck":   "tsc -b",
    "lint":        "eslint .",
    "test":        "vitest run"
  }
}
```

- `predev` runs `generate-api` before `vite` — slow when the contract
  didn't change, fast enough on hot reload after the first build.
- `dev:mock` skips the predev hook; the operator can serve mock
  screens without rebuilding the API client.
- `generate-api` is the standalone entry point — what you run from
  CI or after a controller change.

## End-to-end sequence

```bash
# 1. Backend change — add a controller, change a DTO, etc.
$EDITOR platform/src/host/Comuki.Host/Runs/Controllers/RunsController.cs

# 2. Build the BE — emits artifacts/openapi.json as a Debug side-effect.
dotnet build comuki.slnx -c Debug

# 3. Regenerate the FE client.
cd dashboard && bun run generate-api
# bunx @kubb/cli generate picks up the renamed openapi.json, regenerates
# clients/hooks/types/zod in src/shared/api/_generated/.

# 4. Wire the new hook into a domain.
$EDITOR dashboard/src/domains/runs/api/queries.ts

# 5. Verify the gate:
bun run typecheck && bun run lint && bun run test

# 6. Commit (per project commit-format.md).
git add ... && git commit -m "[hybrid] feat(dashboard): wire new runs endpoint"
```

## Worktree lifecycle

`artifacts/openapi.json` is gitignored. A fresh worktree has no
spec on disk; the first `bun run generate-api` (or any `bun run
dev`) builds the BE first to produce it. The kubb config's
"spec missing" guard throws before `output.clean`, so the generated
tree from the previous tree is never destroyed.

Cross-worktree tip: the file lives at
`<repo-root>/artifacts/openapi.json`, so every worktree sees the
*same* spec after a build (the BE is in `master` checkout; the FE
worktrees share the artifacts dir).

## Sources

- `platform/src/host/Comuki.Host/Comuki.Host.csproj` — OpenAPI
  emission properties + rename target.
- `platform/src/host/Comuki.Host/OpenApi/OpenApiBuildTimeExtensions.cs` —
  hosted-service stripping during document generation.
- `dashboard/kubb.config.ts` — kubb configuration, fail-fast spec guard.
- `dashboard/src/shared/api/kubb-client.ts` — hand-written transport.
- `dashboard/package.json` — `generate-api` / `predev` script wiring.

## Related

- [oauth-oidc.md](./oauth-oidc.md) — OIDC endpoints ride the same
  pipeline; kubb regenerates their clients automatically.
- [minio.md](./minio.md) — the artifacts list endpoint is in the
  generated client.
- [fesettings.md](./fesettings.md) — `VITE_API_BASE_URL` /
  `VITE_USE_MOCK` semantics that gate the kubb-client adapter.

## Anti-patterns

- ❌ Hand-editing anything under `src/shared/api/_generated/*` — the
  next regen wipes it. Edit the backend or the kubb config instead.
- ❌ Running kubb without the spec on disk and without the fail-fast
  guard — `output.clean: true` wipes the tree on every miss, and the
  console.x 1334-files-lost incident happened exactly this way.
- ❌ Letting `dotnet` only be reachable by absolute path. The
  build-time OpenAPI tool spawns `dotnet` from `PATH`; on macOS that's
  usually `$HOME/.dotnet`. Without it the build appears to succeed
  (the BE compiled fine), but no spec is emitted — kubb fails.
- ❌ Building only `dotnet build platform/src/host/Comuki.Host`
  instead of `dotnet build comuki.slnx` — the OpenAPI generation
  target lives in the host csproj, but the rename step depends on
  the slnx-wide configuration. Per-project builds emit the spec
  without renaming it.
- ❌ Adding a root `barrelType: true` "for convenience" — the 597KB
  index is re-read on every regen and adds nothing; domains import
  concrete paths.
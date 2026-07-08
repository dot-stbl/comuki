import { defineConfig } from "@kubb/core"
import { pluginClient } from "@kubb/plugin-client"
import { pluginOas } from "@kubb/plugin-oas"
import { pluginReactQuery } from "@kubb/plugin-react-query"
import { pluginTs } from "@kubb/plugin-ts"

/**
 * Kubb generates a typed TypeScript API client + React Query hooks from
 * the OpenAPI spec produced by Comuki.Platform.Api.Public.
 *
 * The spec is written next to the .csproj at build-time via
 * Microsoft.Extensions.ApiDescription.Server (see Comuki.Platform.Api.Public.csproj
 * — OpenApiDocumentsDirectory=., --file-name openapi-v1).
 *
 * Workflow:
 *   bun run generate-api
 *     → dotnet build comuki.slnx (regenerates openapi-v1.json if API changed)
 *     → kubb generate (emits 15 TS files into src/api/)
 *
 * The same openapi-v1.json is also consumed by any future TS / C# / external
 * SDK that wants a typed client — it's the single source of truth for
 * Comuki.Platform.Api.Public's surface.
 *
 * KNOWN ISSUE (2026-07-06): `bun run generate-api` is currently BROKEN locally
 * — `kubb generate` fails with "Config failed loading". Root cause is upstream:
 * kubb -> `oas`/`oas-normalize` -> @readme/openapi-parser -> ajv-draft-04@1.0.0,
 * whose code calls `applicator.default()` but `applicator.default` is an Array
 * in every available ajv (7.x and 8.x) — an upstream bug in ajv-draft-04@1.0.0
 * (the only release). Verified not fixable by pinning: tried ajv 7.0.4 / 7.2.4,
 * @kubb aligned at 4.37.9 and 4.39.2, nested overrides. The committed src/api
 * is a stale snapshot from a previously-working run.
 * Mitigation: tsconfig.app.json excludes src/api from `tsc -b`, so the build
 * is green regardless (generated code's internal types are the generator's
 * responsibility, not the app build's; nothing in the app imports @/api yet).
 * Real fix path: upstream oas/@readme/openapi-parser/ajv-draft-04, or replace
 * the kubb/oas generator (e.g. openapi-typescript, orval).
 */
export default defineConfig({
  root: ".",
  input: {
    path: "../platform/src/application/api/Comuki.Platform.Api.Public/openapi-v1.json",
  },
  output: {
    path: "./src/api",
    clean: true,
  },
  plugins: [
    pluginOas(),
    pluginTs(),
    pluginClient({ client: "fetch" }),
    pluginReactQuery({ client: "fetch" }),
  ],
})


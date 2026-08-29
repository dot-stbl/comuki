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
 *     → kubb generate (emits TS files into src/shared/api/)
 *
 * The same openapi-v1.json is also consumed by any future TS / C# / external
 * SDK that wants a typed client — it's the single source of truth for
 * Comuki.Platform.Api.Public's surface.
 */
export default defineConfig({
  root: ".",
  input: {
    path: "../platform/src/application/api/Comuki.Platform.Api.Public/openapi-v1.json",
  },
  output: {
    path: "./src/shared/api",
    clean: true,
  },
  plugins: [
    pluginOas(),
    pluginTs(),
    pluginClient({ client: "fetch" }),
    pluginReactQuery({ client: "fetch" }),
  ],
})


import { defineConfig } from "@kubb/core"
import { pluginClient } from "@kubb/plugin-client"
import { pluginOas } from "@kubb/plugin-oas"
import { pluginReactQuery } from "@kubb/plugin-react-query"
import { pluginTs } from "@kubb/plugin-ts"

/**
 * Kubb generates a typed TypeScript API client + React Query hooks from
 * the OpenAPI spec produced by Comuki.Platform.Api.Public.
 *
 * Workflow:
 *   1. dotnet run --project platform/src/application/api/Comuki.Platform.Api.Public
 *   2. curl http://localhost:5000/openapi/v1.json > dashboard/openapi.v1.json
 *   3. bun run generate-api       (this file + the script in package.json)
 *
 * Output: src/api/{models,client,hooks}/* + index barrel.
 * Used by: dashboard pages from Phase 7 onward (intake, runs, approvals, etc.).
 */
export default defineConfig({
  root: ".",
  input: {
    path: "./openapi.v1.json",
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

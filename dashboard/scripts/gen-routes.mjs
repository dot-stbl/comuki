/**
 * Regenerates `src/routeTree.gen.ts` without starting Vite.
 *
 * The router's tree is normally produced by the Vite plugin, which only runs
 * inside `dev` or `build` — and `build` runs `tsc -b` *first*, so a newly added
 * route file fails to typecheck against a tree that has not been regenerated
 * yet. Agent sessions cannot start a dev server at all (it outlives the turn
 * and takes the harness down with it), so adding a route had no offline path.
 *
 * This calls the same generator the plugin calls, with the same config.
 */
import { Generator, getConfig } from "@tanstack/router-generator"

const root = process.cwd()
const config = await getConfig({ target: "react" }, root)
const generator = new Generator({ config, root })

await generator.run()
console.log(`routeTree.gen.ts written from ${config.routesDirectory}`)

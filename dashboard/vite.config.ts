import path from "path"
import { tanstackRouter } from "@tanstack/router-plugin/vite"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

/**
 * What the console loads on demand, and must not be swept into a vendor chunk.
 *
 * The markdown parser, its whole unified/remark/micromark pipeline and the
 * syntax highlighter are reached only through a dynamic import inside the chat
 * thread — the dock's trigger is mounted in the app shell, so anything the
 * console imports statically is in the first paint of every screen. Leaving
 * these unassigned lets Rolldown keep them in the async chunk they are reached
 * through.
 *
 * The order matters: this is tested **before** the `react` branch below,
 * because `react-markdown` carries that substring in its path and would
 * otherwise land in `vendor-react`, which the entry pulls eagerly — a dynamic
 * import into an eager chunk is not a dynamic import at all.
 */
const LAZY_CONSOLE =
  /node_modules[\\/](?:highlight\.js|react-markdown|unified|remark-[\w-]+|micromark[\w-]*|mdast-util-[\w-]+|hast-util-[\w-]+|unist-util-[\w-]+|vfile[\w-]*|property-information|space-separated-tokens|comma-separated-tokens|html-url-attributes|character-entities[\w-]*|decode-named-character-reference|stringify-entities|markdown-table|longest-streak|trim-lines|style-to-[\w-]+|estree-util-[\w-]+|zwitch|ccount|devlop|bail|trough)[\\/]/

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // tanstackRouter MUST come before react — generates routeTree.gen.ts
    // autoCodeSplitting: true wraps every route's component into its own
    // lazy chunk automatically; combined with createLazyFileRoute on the
    // route files below, the first paint carries only the home + login
    // trees and the per-page bundles fetch on navigation.
    tanstackRouter({ target: "react", autoCodeSplitting: true }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    // Comuki port pool 17000–17200 — see .agents/rules/process/ports.md
    port: 17173,
    strictPort: true,
  },
  define: {
    // CI exports `COMMIT_SHA=<sha>` (or the deploy pipeline passes it through);
    // local `bun run dev` reads `''` and the footer renders an empty slot.
    // The schema in `src/shared/config/env.ts` calls this `VITE_COMMIT_SHA`,
    // so the value here lands at the same name on `import.meta.env`.
    "import.meta.env.VITE_COMMIT_SHA": JSON.stringify(
      process.env.COMMIT_SHA ?? ""
    ),
  },
  build: {
    // Manual chunks split vendor runtime + kubb-generated clients from the
    // app code so the first paint carries only what `/login` and `/` need.
    // React + TanStack + the platform runtime stay cached across deploys;
    // app code is the only part that changes on each release. With the
    // lazy route files (createLazyFileRoute) the per-page bundles ship
    // separately, so the home page no longer pulls the runs/chat/identity
    // screens along for the ride. The `manualChunks` function form keeps
    // this resilient as new kubb clients are added — anything kubb owns
    // lives in `vendor-kubb`, never in app code.
    rolldownOptions: {
      output: {
        manualChunks: (id) => {
          if (id.includes("node_modules")) {
            // Named first on purpose — see LAZY_CONSOLE above.
            if (LAZY_CONSOLE.test(id)) {
              return undefined
            }
            if (id.includes("@tanstack")) {
              return "vendor-tanstack"
            }
            if (id.includes("react")) {
              return "vendor-react"
            }
            if (id.includes("@kubb") || id.includes("/kubb-client")) {
              return "vendor-kubb"
            }
            return "vendor"
          }

          if (id.includes("/src/shared/api/_generated/")) {
            return "vendor-kubb"
          }

          return undefined
        },
      },
    },
  },
})

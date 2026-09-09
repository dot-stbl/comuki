import path from "path"
import { tanstackRouter } from "@tanstack/router-plugin/vite"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

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

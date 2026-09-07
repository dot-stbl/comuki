import path from "path"
import { tanstackRouter } from "@tanstack/router-plugin/vite"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // tanstackRouter MUST come before react — generates routeTree.gen.ts
    tanstackRouter({ target: "react" }),
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
})
